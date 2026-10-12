using sblngavnav6.Common;
using sblngavnav6.Data;
using sblngavnav6.Services;
using sblngavnav6.TelegramExtensions.Core;

namespace sblngavnav6.Remna;

public sealed class RemnaWatchService
{
    public const int FailThreshold = 2;

    private sealed class Down
    {
        public int Fails;
        public DateTimeOffset Since;
        public bool Alerted;
    }

    private readonly RemnaService _remna;
    private readonly TelegramNotifier _telegram;
    private readonly Dictionary<string, Down> _nodes = new();
    private Down _api;
    private CancellationTokenSource _cts;
    private Task _loop = Task.CompletedTask;

    public RemnaWatchService(RemnaService remna, TelegramNotifier telegram)
    {
        _remna = remna;
        _telegram = telegram;
    }

    public static bool Enabled =>
        Global.Vars.Cfg.remnaEnabled
        && !string.IsNullOrWhiteSpace(Global.Vars.Cfg.remnaAlertTelegramChat)
        && !string.IsNullOrWhiteSpace(Global.Vars.Cfg.telegramToken)
        && !string.IsNullOrWhiteSpace(Global.Vars.Cfg.remnaBaseUrl);

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

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        await LoggingService.LogInformationAsync("REMNA", $"Вотчер нод запущен, интервал {Interval.TotalSeconds:0}с");

        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try { await CheckAsync(cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { await LoggingService.LogErrorAsync("REMNA", "Ошибка проверки нод", ex); }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var result = await _remna.NodesAsync(cancellationToken);

        if (!result.Ok)
        {
            _api ??= new Down { Since = now };
            if (++_api.Fails == FailThreshold)
            {
                _api.Alerted = true;
                await AlertAsync("❌ API Remnawave не отвечает", detail: result.Error, sinceLabel: "падает с", since: _api.Since);
            }
            return;
        }

        if (_api is { Alerted: true })
            await AlertAsync("✅ API Remnawave снова отвечает", detail: $"простой {CommonUtils.Time.FormatAge(now - _api.Since)}");
        _api = null;

        var seen = new HashSet<string>();

        foreach (var node in result.Data ?? [])
        {
            seen.Add(node.Uuid);
            _nodes.TryGetValue(node.Uuid, out var state);

            if (node.IsDisabled || node.IsConnected)
            {
                if (state is { Alerted: true })
                    await AlertAsync($"✅ {node.Name} {node.CountryCode} снова в строю",
                        detail: node.IsDisabled
                            ? "нода выключена вручную"
                            : $"простой {CommonUtils.Time.FormatAge(now - state.Since)}, онлайн: {node.UsersOnline ?? 0}");

                _nodes.Remove(node.Uuid);
                continue;
            }

            if (state is null)
                _nodes[node.Uuid] = state = new Down { Since = node.LastStatusChange ?? now };

            if (++state.Fails == FailThreshold)
            {
                state.Alerted = true;
                await AlertAsync($"🔴 {node.Name} {node.CountryCode} упала",
                    address: node.Address,
                    detail: $"⚠️ {CommonUtils.Text.Truncate(CommonUtils.Text.FirstMeaningfulLine(node.LastStatusMessage, "без сообщения"), 300)}",
                    sinceLabel: "лежит с",
                    since: state.Since);
            }
        }

        foreach (var gone in _nodes.Keys.Where(uuid => !seen.Contains(uuid)).ToList())
            _nodes.Remove(gone);
    }

    private async Task AlertAsync(
        string title,
        string address = null,
        string detail = null,
        string sinceLabel = null,
        DateTimeOffset? since = null)
    {
        await LoggingService.LogWarningAsync("REMNA", $"{title}: {address} {detail}".TrimEnd());

        var html = string.Join("\n", new[]
        {
            $"<b>{TelegramNotifier.Html(title)}</b>",
            address is null ? null : $"<code>{TelegramNotifier.Html(address)}</code>",
            detail is null ? null : TelegramNotifier.Html(detail),
            since is null ? null : $"{sinceLabel} {since.Value.UtcDateTime:dd.MM HH:mm} UTC"
        }.Where(line => line is not null));

        try
        {
            await _telegram.SendHtmlAsync(Global.Vars.Cfg.remnaAlertTelegramChat, Global.Vars.Cfg.remnaAlertTelegramTopic, html);
        }
        catch (Exception ex)
        {
            await LoggingService.LogErrorAsync("REMNA", "Не удалось отправить алерт в Telegram", ex);
        }
    }
}
