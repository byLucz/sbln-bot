using sblngavnav6.Common;
using sblngavnav6.Data;
using sblngavnav6.Services;
using sblngavnav6.TelegramExtensions.Core;

namespace sblngavnav6.Remna;

public sealed class RemnaWatchService
{
    public const string Name = "R-WATCHER";
    public const string AgentName = "R-WATCHER agent";
    public const int FailThreshold = 2;
    public const string DefaultMaintenance = "плановые работы";

    private sealed class Down
    {
        public int Fails;
        public DateTimeOffset Since;
        public bool Alerted;
    }

    private readonly RemnaService _remna;
    private readonly RemnaStore _store;
    private readonly TelegramNotifier _telegram;
    private readonly Dictionary<string, Down> _nodes = new();
    private readonly Dictionary<string, Down> _apis = new();
    private readonly Dictionary<string, Down> _ports = new();
    private CancellationTokenSource _cts;
    private Task _loop = Task.CompletedTask;

    public RemnaWatchService(RemnaService remna, RemnaStore store, TelegramNotifier telegram)
    {
        _remna = remna;
        _store = store;
        _telegram = telegram;
    }

    public static bool Enabled =>
        Global.Vars.Cfg.remnaEnabled
        && Global.Vars.Cfg.remnaServers.Count > 0
        && !string.IsNullOrWhiteSpace(Global.Vars.Cfg.remnaAlertTelegramChat)
        && !string.IsNullOrWhiteSpace(Global.Vars.Cfg.telegramToken);

    public static TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(15, Global.Vars.Cfg.remnaWatchSeconds));

    public void Start(CancellationToken cancellationToken = default)
    {
        if (!Enabled || _cts != null) return;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = LoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts == null) return;
        await _cts.CancelAsync();
        await _loop;
    }

    public async Task<string> StartMaintenanceAsync(RemnaServer server, RemnaNode node, string message, string by)
    {
        var disabled = await _remna.DisableNodeAsync(server, node.Uuid);
        if (!disabled.Ok)
            return disabled.Error;

        message = string.IsNullOrWhiteSpace(message) ? DefaultMaintenance : message.Trim();
        await _store.StartMaintenanceAsync(new RemnaMaintenance
        {
            Server = server.Url,
            Uuid = node.Uuid,
            Node = node.Name,
            Message = message,
            By = by,
            Since = DateTimeOffset.UtcNow
        });

        await AlertAsync($"🛠 {node.Name} {node.CountryCode} ушла на тех. работы", server,
            detail: $"{message}\nот {by}");
        return null;
    }

    public async Task<string> EndMaintenanceAsync(RemnaServer server, RemnaNode node, string by)
    {
        var enabled = await _remna.EnableNodeAsync(server, node.Uuid);
        if (!enabled.Ok)
            return enabled.Error;

        var entry = await _store.EndMaintenanceAsync(server, node.Uuid);
        await AlertAsync($"✅ {node.Name} {node.CountryCode}: тех. работы завершены", server,
            detail: entry is null
                ? $"от {by}"
                : $"{entry.Message}\nдлились {CommonUtils.Time.FormatAge(DateTimeOffset.UtcNow - entry.Since)}, завершил {by}");
        return null;
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        await LoggingService.LogInformationAsync("REMNA", $"{Name} запущен, серверов {_remna.Servers.Count}, интервал {Interval.TotalSeconds:0}с");
        if (!RemnaProbe.QuicSupported)
            await LoggingService.LogWarningAsync("REMNA", $"{Name}: QUIC недоступен (нет libmsquic), Hysteria и TUIC не проверяются");

        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                var servers = await _remna.AllNodesAsync(cancellationToken);
                foreach (var entry in servers)
                {
                    try { await CheckAsync(entry.Server, entry.Result); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception ex) { await LoggingService.LogErrorAsync("REMNA", $"{Name}: ошибка проверки {entry.Server.Name}", ex); }
                }

                try { await CheckPortsAsync(servers, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { await LoggingService.LogErrorAsync("REMNA", $"{Name}: ошибка проверки клиентских портов", ex); }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task CheckAsync(RemnaServer server, RemnaResult<List<RemnaNode>> result)
    {
        var now = DateTimeOffset.UtcNow;

        if (!result.Ok)
        {
            if (!_apis.TryGetValue(server.Url, out var api))
                _apis[server.Url] = api = new Down { Since = now };

            if (++api.Fails == FailThreshold)
            {
                api.Alerted = true;
                await _store.OpenIncidentAsync(new RemnaIncident { Server = server.Url, Node = $"API {server.Name}", Message = result.Error, Start = api.Since });
                await AlertAsync($"❌ API Remnawave {server.Name} не отвечает", server, detail: result.Error, sinceLabel: "падает с", since: api.Since);
            }
            return;
        }

        _apis.Remove(server.Url, out var recovered);
        var apiIncident = await _store.CloseIncidentAsync(server, null, now);
        if (recovered is { Alerted: true } || apiIncident is not null)
            await AlertAsync($"✅ API Remnawave {server.Name} снова отвечает", server,
                detail: $"простой {CommonUtils.Time.FormatAge(now - (apiIncident?.Start ?? recovered.Since))}");

        var seen = new HashSet<string>();

        foreach (var node in result.Data ?? [])
        {
            var key = $"{server.Url}|{node.Uuid}";
            seen.Add(key);
            _nodes.TryGetValue(key, out var state);

            if (node.IsDisabled || node.IsConnected)
            {
                var incident = await _store.CloseIncidentAsync(server, node.Uuid, now);
                if (state is { Alerted: true } || incident is not null)
                    await AlertAsync($"✅ {node.Name} {node.CountryCode} снова в строю", server,
                        detail: node.IsDisabled
                            ? "нода выключена вручную"
                            : $"простой {CommonUtils.Time.FormatAge(now - (incident?.Start ?? state.Since))}, онлайн: {node.UsersOnline ?? 0}");

                _nodes.Remove(key);
                continue;
            }

            if (state is null)
                _nodes[key] = state = new Down { Since = node.LastStatusChange ?? now };

            if (++state.Fails == FailThreshold)
            {
                state.Alerted = true;
                var message = CommonUtils.Text.FirstMeaningfulLine(node.LastStatusMessage, "без сообщения", 300);
                if (await _store.HasOpenIncidentAsync(server, node.Uuid))
                    continue;

                await _store.OpenIncidentAsync(new RemnaIncident { Server = server.Url, Uuid = node.Uuid, Node = node.Name, Message = message, Start = state.Since });
                await AlertAsync($"🔴 {node.Name} {node.CountryCode} упала", server,
                    address: node.Address,
                    detail: $"⚠️ {message}",
                    sinceLabel: "лежит с",
                    since: state.Since);
            }
        }

        foreach (var gone in _nodes.Keys.Where(key => key.StartsWith(server.Url + "|", StringComparison.Ordinal) && !seen.Contains(key)).ToList())
            _nodes.Remove(gone);
    }

    private async Task CheckPortsAsync(List<RemnaServerNodes> servers, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var results = await RemnaProbe.CheckAsync(_remna, servers, cancellationToken);
        var active = results.Select(r => PortIncident(r.Target)).ToHashSet();
        var keys = results.Select(r => r.Target.Key).ToHashSet();

        foreach (var server in servers.Where(s => s.Result.Ok).Select(s => s.Server))
            await _store.CloseIncidentsAsync(server, i => i.Uuid?.StartsWith("port:", StringComparison.Ordinal) == true && !active.Contains(i.Uuid), now);
        foreach (var gone in _ports.Keys.Where(key => !keys.Contains(key)).ToList())
            _ports.Remove(gone);

        foreach (var result in results)
        {
            var target = result.Target;
            var key = target.Key;
            var incidentId = PortIncident(target);
            var where = target.Endpoint;

            if (result.Ok || result.Expiring)
            {
                _ports.Remove(key, out var state);
                var incident = await _store.CloseIncidentAsync(target.Server, incidentId, now);
                if (state is { Alerted: true } || incident is not null)
                    await AlertAsync($"🔐 {target.Label} снова доступен клиентам", target.Server,
                        detail: $"{where}\nпростой {CommonUtils.Time.FormatAge(now - (incident?.Start ?? state.Since))}");

                if (result is { Expiring: true, NotAfter: { } notAfter, CertDays: { } days }
                    && await _store.MarkCertWarningAsync(key, new DateTimeOffset(notAfter, TimeSpan.Zero), days))
                    await AlertAsync($"📜 сертификат {target.Label} истекает через {result.CertDays} дн.", target.Server, detail: where);
                continue;
            }

            if (!_ports.TryGetValue(key, out var down))
                _ports[key] = down = new Down { Since = now };

            if (++down.Fails != FailThreshold || await _store.HasOpenIncidentAsync(target.Server, incidentId))
            {
                down.Alerted |= down.Fails >= FailThreshold;
                continue;
            }

            down.Alerted = true;
            await _store.OpenIncidentAsync(new RemnaIncident { Server = target.Server.Url, Uuid = incidentId, Node = $"порт {target.Label}", Message = result.Error, Start = down.Since });
            await AlertAsync($"🔐 {target.Label} недоступен клиентам", target.Server,
                detail: $"{where}\n⚠️ {result.Error}", sinceLabel: "с", since: down.Since);
        }
    }

    private static string PortIncident(RemnaProbeTarget target) => $"port:{target.Endpoint}:{target.Sni}";

    private async Task AlertAsync(
        string title,
        RemnaServer server,
        string address = null,
        string detail = null,
        string sinceLabel = null,
        DateTimeOffset? since = null)
    {
        await LoggingService.LogWarningAsync("REMNA", $"{Name}: {title}: {address} {detail?.Replace('\n', ' ')}".TrimEnd());

        if (!Enabled)
            return;

        var html = string.Join("\n", new[]
        {
            $"<b>{TelegramNotifier.Html(title)}</b>",
            _remna.Multi ? $"🌐 {TelegramNotifier.Html(server.Name)}" : null,
            address is null ? null : $"<code>{TelegramNotifier.Html(address)}</code>",
            detail is null ? null : TelegramNotifier.Html(detail),
            since is null ? null : $"{sinceLabel} {CommonUtils.Time.Clock(since.Value)}",
            $"<i>{Name}</i>"
        }.Where(line => line is not null));

        try
        {
            await _telegram.SendHtmlAsync(Global.Vars.Cfg.remnaAlertTelegramChat, Global.Vars.Cfg.remnaAlertTelegramTopic, html);
        }
        catch (Exception ex)
        {
            await LoggingService.LogErrorAsync("REMNA", $"{Name}: не удалось отправить алерт в Telegram", ex);
        }
    }
}
