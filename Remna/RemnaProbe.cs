using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace sblngavnav6.Remna;

public sealed record RemnaProbeTarget(RemnaServer Server, string Label, string Address, int Port, string Sni, bool Tls, bool Quic = false, string[] Alpn = null, bool Reality = false)
{
    public string Endpoint => $"{Address}:{Port}{(Quic ? "/udp" : "")}";
    public string Key => $"{Server.Url}|{Endpoint}|{Sni}";
}

public sealed record RemnaProbeResult(RemnaProbeTarget Target, bool Ok, string Error, int? CertDays, long Ms, bool Expiring = false, DateTime? NotAfter = null);

public static class RemnaProbe
{
    public const int CertWarningDays = 7;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    public static bool QuicSupported { get; } =
        (OperatingSystem.IsLinux() || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) && QuicConnection.IsSupported;

    public static List<RemnaProbeTarget> Targets(RemnaServer server, IEnumerable<RemnaHost> hosts, IReadOnlyCollection<RemnaNode> nodes)
    {
        var inbounds = nodes.SelectMany(node => node.Inbounds)
            .Where(inbound => inbound.Uuid is not null)
            .GroupBy(inbound => inbound.Uuid)
            .ToDictionary(group => group.Key, group => group.First());

        return hosts
            .Where(host => !host.IsDisabled && !string.IsNullOrWhiteSpace(host.Address) && host.Port > 0)
            .Where(host => host.Nodes is not { Count: > 0 } || nodes.Any(node => host.Nodes.Contains(node.Uuid) && node.IsConnected && !node.IsDisabled))
            .Select(host => (Host: host, Inbound: host.Inbound?.ConfigProfileInboundUuid is { } id && inbounds.TryGetValue(id, out var found) ? found : null))
            .Where(entry => entry.Inbound is not { IsUdp: true } || entry.Inbound is { IsQuic: true, HasObfs: false })
            .Select(entry =>
            {
                var (host, inbound) = entry;
                var quic = inbound is { IsQuic: true };
                var security = string.Equals(host.SecurityLayer, "DEFAULT", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(host.SecurityLayer)
                    ? inbound?.Security
                    : host.SecurityLayer;
                var tls = quic || security is not null && (security.Equals("tls", StringComparison.OrdinalIgnoreCase) || security.Equals("reality", StringComparison.OrdinalIgnoreCase));
                var sni = host.OverrideSniFromAddress || string.IsNullOrWhiteSpace(host.Sni)
                    ? host.OverrideSniFromAddress ? host.Address : inbound?.ServerNames.FirstOrDefault() ?? host.Address
                    : host.Sni;
                var alpn = !quic ? null
                    : host.Alpn?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: > 0 } fromHost ? fromHost
                    : inbound.Alpns is { Count: > 0 } fromInbound ? fromInbound.ToArray()
                    : ["h3"];
                var reality = !quic && string.Equals(security, "reality", StringComparison.OrdinalIgnoreCase);
                return new RemnaProbeTarget(server, string.IsNullOrWhiteSpace(host.Remark) ? host.Address : host.Remark,
                    host.Address, host.Port, sni, tls, quic, alpn, reality);
            })
            .DistinctBy(target => target.Key)
            .ToList();
    }

    public static async Task<RemnaProbeResult> ProbeAsync(RemnaProbeTarget target, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (target.Quic)
                return OperatingSystem.IsLinux() || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                    ? await ProbeQuicAsync(target, sw, cts.Token)
                    : new RemnaProbeResult(target, true, null, null, 0);

            using var tcp = new TcpClient();
            await tcp.ConnectAsync(target.Address, target.Port, cts.Token);

            if (!target.Tls)
                return new RemnaProbeResult(target, true, null, null, sw.ElapsedMilliseconds);

            var errors = SslPolicyErrors.None;
            DateTime? notAfter = null;
            await using var ssl = new SslStream(tcp.GetStream(), false, (_, certificate, _, policyErrors) =>
            {
                errors = policyErrors;
                if (certificate is X509Certificate2 x509)
                    notAfter = x509.NotAfter.ToUniversalTime();
                return true;
            });

            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = target.Sni,
                ApplicationProtocols = [SslApplicationProtocol.Http2, SslApplicationProtocol.Http11]
            }, cts.Token);

            return Evaluate(target, errors, notAfter, sw.ElapsedMilliseconds, strict: !target.Reality);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new RemnaProbeResult(target, false, $"таймаут {Timeout.TotalSeconds:0}с", null, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is SocketException or IOException or System.Security.Authentication.AuthenticationException or QuicException)
        {
            return new RemnaProbeResult(target, false, ex.GetBaseException().Message, null, sw.ElapsedMilliseconds);
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    private static async Task<RemnaProbeResult> ProbeQuicAsync(RemnaProbeTarget target, System.Diagnostics.Stopwatch sw, CancellationToken cancellationToken)
    {
        var errors = SslPolicyErrors.None;
        DateTime? notAfter = null;

        await using var connection = await QuicConnection.ConnectAsync(new QuicClientConnectionOptions
        {
            RemoteEndPoint = new DnsEndPoint(target.Address, target.Port),
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                TargetHost = target.Sni,
                ApplicationProtocols = (target.Alpn ?? ["h3"]).Select(alpn => new SslApplicationProtocol(alpn)).ToList(),
                RemoteCertificateValidationCallback = (_, certificate, _, policyErrors) =>
                {
                    errors = policyErrors;
                    if (certificate is X509Certificate2 x509)
                        notAfter = x509.NotAfter.ToUniversalTime();
                    return true;
                }
            }
        }, cancellationToken);

        await connection.CloseAsync(0, cancellationToken);
        return Evaluate(target, errors, notAfter, sw.ElapsedMilliseconds, strict: false);
    }

    private static RemnaProbeResult Evaluate(RemnaProbeTarget target, SslPolicyErrors errors, DateTime? notAfter, long ms, bool strict)
    {
        var days = notAfter is { } expires ? (int)Math.Floor((expires - DateTime.UtcNow).TotalDays) : (int?)null;

        if (target.Reality)
            return new RemnaProbeResult(target, true, null, null, ms);

        if (errors != SslPolicyErrors.None)
            return !strict
                ? new RemnaProbeResult(target, true, null, null, ms)
                : new RemnaProbeResult(target, false,
                    days is < 0 ? $"сертификат истёк {-days} дн. назад"
                    : errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) ? $"сертификат не для {target.Sni}"
                    : errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable) ? "сервер не отдал сертификат"
                    : "недоверенная цепочка сертификата",
                    days, ms);

        return days is <= CertWarningDays
            ? new RemnaProbeResult(target, false, $"сертификат истекает через {days} дн.", days, ms, Expiring: true, NotAfter: notAfter)
            : new RemnaProbeResult(target, true, null, days, ms, NotAfter: notAfter);
    }

    public static async Task<List<RemnaProbeResult>> CheckAsync(RemnaService remna, IEnumerable<RemnaServerNodes> servers, CancellationToken cancellationToken = default)
    {
        var targets = new List<RemnaProbeTarget>();
        foreach (var entry in servers.Where(s => s.Result.Ok))
        {
            var hosts = await remna.HostsAsync(entry.Server, cancellationToken);
            if (hosts.Ok)
                targets.AddRange(Targets(entry.Server, hosts.Data ?? [], entry.Result.Data ?? [])
                    .Where(target => !target.Quic || QuicSupported));
        }

        using var gate = new SemaphoreSlim(8);
        return (await Task.WhenAll(targets.Select(async target =>
        {
            await gate.WaitAsync(cancellationToken);
            try { return await ProbeAsync(target, cancellationToken); }
            finally { gate.Release(); }
        }))).ToList();
    }
}
