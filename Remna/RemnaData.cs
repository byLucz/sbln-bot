using sblngavnav6.Common;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace sblngavnav6.Remna;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RemnaEnvelope<List<RemnaNode>>))]
[JsonSerializable(typeof(RemnaEnvelope<RemnaNode>))]
[JsonSerializable(typeof(RemnaEnvelope<RemnaEvent>))]
[JsonSerializable(typeof(RemnaEnvelope<RemnaStats>))]
[JsonSerializable(typeof(RemnaEnvelope<RemnaHealth>))]
[JsonSerializable(typeof(RemnaState))]
[JsonSerializable(typeof(RemnaEnvelope<List<RemnaHost>>))]
[JsonSerializable(typeof(RemnaScan))]
internal partial class RemnaJsonContext : JsonSerializerContext { }

public sealed record RemnaServer(int Index, string Name, string Url, string Token);

public sealed record RemnaNodeRef(RemnaServer Server, RemnaNode Node)
{
    public string Key => $"{Server.Index}:{Node.Uuid}";
}

public sealed record RemnaServerNodes(RemnaServer Server, RemnaResult<List<RemnaNode>> Result);

public static class RemnaServerNodesExtensions
{
    public static List<RemnaNodeRef> Refs(this IEnumerable<RemnaServerNodes> servers)
        => servers.Where(s => s.Result.Ok)
            .SelectMany(s => (s.Result.Data ?? []).Select(node => new RemnaNodeRef(s.Server, node)))
            .ToList();
}

public sealed class RemnaEnvelope<T>
{
    public T Response { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed class RemnaNode
{
    public string Uuid { get; set; }
    public string Name { get; set; }
    public string Address { get; set; }
    public int? Port { get; set; }
    public bool IsConnected { get; set; }
    public bool IsDisabled { get; set; }
    public bool IsConnecting { get; set; }
    public string LastStatusMessage { get; set; }
    public DateTimeOffset? LastStatusChange { get; set; }
    public double? TrafficUsedBytes { get; set; }
    public double? TrafficLimitBytes { get; set; }
    public string CountryCode { get; set; }

    public string DisplayName => CommonUtils.Text.Flag(CountryCode) is { Length: > 0 } flag && Name?.Contains(flag) != true
        ? $"{flag} {Name}"
        : Name;
    public int? UsersOnline { get; set; }
    public int? NotifyPercent { get; set; }
    public double? XrayUptime { get; set; }
    public RemnaNodeVersions Versions { get; set; }
    public RemnaNodeProfile ConfigProfile { get; set; }
    public RemnaNodeSystem System { get; set; }

    public IEnumerable<RemnaInbound> Inbounds => ConfigProfile?.ActiveInbounds ?? [];

    public List<string> Snis => Inbounds.SelectMany(inbound => inbound.ServerNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

public sealed class RemnaNodeSystem
{
    public RemnaNodeSystemInfo Info { get; set; }
    public RemnaNodeSystemStats Stats { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed class RemnaNodeSystemInfo
{
    public int Cpus { get; set; }
    public double MemoryTotal { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed class RemnaNodeSystemStats
{
    public double MemoryFree { get; set; }
    public double MemoryUsed { get; set; }
    public double Uptime { get; set; }
    public List<double> LoadAvg { get; set; }
}

public sealed class RemnaNodeProfile
{
    public List<RemnaInbound> ActiveInbounds { get; set; }
}

public sealed class RemnaInbound
{
    private static readonly string[] UdpMarkers = ["hysteria", "tuic", "wireguard", "kcp", "quic"];
    private static readonly string[] QuicMarkers = ["hysteria", "tuic", "quic"];

    public string Uuid { get; set; }
    public string Tag { get; set; }
    public string Type { get; set; }
    public string Network { get; set; }
    public string Security { get; set; }
    public JsonElement? RawInbound { get; set; }

    public bool IsUdp
    {
        get
        {
            var raw = RawInbound is { ValueKind: JsonValueKind.Object } value ? value : default;
            var protocol = raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("protocol", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            var transport = raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("streamSettings", out var stream)
                && stream.TryGetProperty("network", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;

            return Matches(UdpMarkers, protocol, transport);
        }
    }

    public bool IsQuic
    {
        get
        {
            var raw = RawInbound is { ValueKind: JsonValueKind.Object } value ? value : default;
            var protocol = raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("protocol", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            var transport = raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("streamSettings", out var stream)
                && stream.TryGetProperty("network", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;

            return Matches(QuicMarkers, protocol, transport);
        }
    }

    public bool HasObfs => RawInbound is { ValueKind: JsonValueKind.Object } raw && ContainsKey(raw, "obfs");

    public List<string> Alpns
        => RawInbound is { ValueKind: JsonValueKind.Object } raw && raw.TryGetProperty("streamSettings", out var stream)
            && stream.TryGetProperty("tlsSettings", out var tls) && tls.TryGetProperty("alpn", out var alpn) && alpn.ValueKind == JsonValueKind.Array
            ? alpn.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()).ToList()
            : [];

    private bool Matches(string[] markers, string protocol, string transport)
        => new[] { Type, Network, protocol, transport }
            .Any(v => v is not null && markers.Any(marker => v.Contains(marker, StringComparison.OrdinalIgnoreCase)));

    private static bool ContainsKey(JsonElement element, string key) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Any(p => p.NameEquals(key) || ContainsKey(p.Value, key)),
        JsonValueKind.Array => element.EnumerateArray().Any(item => ContainsKey(item, key)),
        _ => false
    };

    public IEnumerable<string> ServerNames
    {
        get
        {
            if (RawInbound is not { ValueKind: JsonValueKind.Object } raw || !raw.TryGetProperty("streamSettings", out var stream))
                yield break;

            if (stream.TryGetProperty("realitySettings", out var reality) && reality.TryGetProperty("serverNames", out var names)
                && names.ValueKind == JsonValueKind.Array)
                foreach (var name in names.EnumerateArray())
                    if (name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } value)
                        yield return value;

            if (stream.TryGetProperty("tlsSettings", out var tls) && tls.TryGetProperty("serverName", out var server)
                && server.ValueKind == JsonValueKind.String && server.GetString() is { Length: > 0 } single)
                yield return single;
        }
    }
}

public sealed class RemnaHost
{
    public string Uuid { get; set; }
    public string Remark { get; set; }
    public string Address { get; set; }
    public int Port { get; set; }
    public string Sni { get; set; }
    public bool IsDisabled { get; set; }
    public string SecurityLayer { get; set; }
    public bool OverrideSniFromAddress { get; set; }
    public string Alpn { get; set; }
    public RemnaHostInbound Inbound { get; set; }
    public List<string> Nodes { get; set; }
}

public sealed class RemnaHostInbound
{
    public string ConfigProfileInboundUuid { get; set; }
}

public sealed class RemnaScan
{
    public string Version { get; set; }
    public string Host { get; set; }
    [JsonPropertyName("elapsed_ms")]
    public long ElapsedMs { get; set; }
    public int Total { get; set; }
    public int Passed { get; set; }
    public RemnaScanResult Current { get; set; }
    public List<RemnaScanResult> Results { get; set; }
}

public sealed class RemnaScanResult
{
    public string Domain { get; set; }
    public bool Ok { get; set; }
    public string Ip { get; set; }
    public string Tls { get; set; }
    public string Alpn { get; set; }
    [JsonPropertyName("cert_days")]
    public int? CertDays { get; set; }
    [JsonPropertyName("tcp_ms")]
    public double? TcpMs { get; set; }
    [JsonPropertyName("tls_ms")]
    public double? TlsMs { get; set; }
    [JsonPropertyName("jitter_ms")]
    public double? JitterMs { get; set; }
    public int Score { get; set; }
    public string Error { get; set; }
}

public sealed class RemnaNodeVersions
{
    public string Xray { get; set; }
    public string Node { get; set; }
}

public sealed class RemnaEvent
{
    public bool EventSent { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed class RemnaStats
{
    public double Uptime { get; set; }
    public RemnaStatsUsers Users { get; set; }
    public RemnaStatsOnline OnlineStats { get; set; }
    public RemnaStatsNodes Nodes { get; set; }
}

public sealed class RemnaHealth
{
    public List<RemnaRuntime> RuntimeMetrics { get; set; }
}

public sealed class RemnaRuntime
{
    public string InstanceType { get; set; }
    public double Rss { get; set; }
    public double EventLoopP99Ms { get; set; }
    public double Uptime { get; set; }
}

public sealed class RemnaStatsUsers
{
    public int TotalUsers { get; set; }
}

public sealed class RemnaStatsOnline
{
    public int OnlineNow { get; set; }
    public int LastDay { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed class RemnaStatsNodes
{
    public int TotalOnline { get; set; }
    public double TotalBytesLifetime { get; set; }
}

public sealed class RemnaMaintenance
{
    public string Server { get; set; }
    public string Uuid { get; set; }
    public string Node { get; set; }
    public string Message { get; set; }
    public string By { get; set; }
    public DateTimeOffset Since { get; set; }
}

public sealed class RemnaIncident
{
    public string Server { get; set; }
    public string Uuid { get; set; }
    public string Node { get; set; }
    public string Message { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset? End { get; set; }
}

public sealed class RemnaState
{
    public List<RemnaMaintenance> Maintenance { get; set; } = [];
    public List<RemnaIncident> Incidents { get; set; } = [];
    public Dictionary<string, RemnaCertWarning> CertAlerts { get; set; } = [];
}

public sealed class RemnaCertWarning
{
    public DateTimeOffset NotAfter { get; set; }
    public int Days { get; set; }
}
