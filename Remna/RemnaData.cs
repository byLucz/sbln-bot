using System.Text.Json.Serialization;

namespace sblngavnav6.Remna;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RemnaEnvelope<List<RemnaNode>>))]
[JsonSerializable(typeof(RemnaEnvelope<RemnaNode>))]
[JsonSerializable(typeof(RemnaEnvelope<RemnaEvent>))]
[JsonSerializable(typeof(RemnaEnvelope<RemnaStats>))]
[JsonSerializable(typeof(RemnaEnvelope<RemnaHealth>))]
internal partial class RemnaJsonContext : JsonSerializerContext { }

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
        public int? UsersOnline { get; set; }
        public int? NotifyPercent { get; set; }
        public double? XrayUptime { get; set; }
        public RemnaNodeVersions Versions { get; set; }
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
