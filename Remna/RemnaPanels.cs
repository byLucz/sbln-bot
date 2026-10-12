using Discord;
using sblngavnav6.Common;
using sblngavnav6.Core;
using sblngavnav6.Data;

namespace sblngavnav6.Remna;

public static class RemnaPanelBuilder
{
    public const string Source = "Remnawave";

    private const int PageSize = 5;

    public static string Scope(ulong channelId, ulong userId) => $"remna:{channelId}:{userId}";

    public static string Status(RemnaNode node) =>
        node.IsDisabled ? "⏸️"
        : node.IsConnected ? "🟢"
        : node.IsConnecting ? "🟡"
        : "🔴";

    public static string Describe(RemnaNode node)
    {
        var traffic = CommonUtils.Text.Bytes(node.TrafficUsedBytes ?? 0)
            + (node.TrafficLimitBytes is > 0 ? $" / {CommonUtils.Text.Bytes(node.TrafficLimitBytes.Value)}" : "");

        var lines = new List<string>
        {
            $"`{node.Address}{(node.Port is null ? "" : $":{node.Port}")}`",
            $"👥 онлайн: {node.UsersOnline ?? 0}, 📶 трафик: {traffic}"
        };

        if (node.IsConnected && node.XrayUptime is > 0)
            lines.Add($"⏱️ аптайм: {CommonUtils.Time.FormatAge(TimeSpan.FromSeconds(node.XrayUptime.Value))}, xray {node.Versions?.Xray ?? "?"}");

        if (!node.IsConnected && !node.IsDisabled)
            lines.Add($"⚠️ {CommonUtils.Text.Truncate(CommonUtils.Text.FirstMeaningfulLine(node.LastStatusMessage, "без сообщения"), 150)}");

        return string.Join("\n", lines);
    }

    public static async Task<(List<Embed> Pages, List<RemnaNode> Nodes)> BuildAsync(RemnaService remna)
    {
        var nodesTask = remna.NodesAsync();
        var statsTask = remna.StatsAsync();
        var nodes = await nodesTask;
        var stats = await statsTask;

        if (!nodes.Ok)
            return ([await EmbedHandler.CreateErrorEmbed(Source, nodes.Error)], []);

        var list = nodes.Data ?? [];
        var header = stats.Ok && stats.Data is { } s
            ? $"🖥️ ноды онлайн: {list.Count(n => n.IsConnected)}/{list.Count}\n"
              + $"👥 юзеров онлайн: {s.OnlineStats?.OnlineNow ?? 0}, за сутки: {s.OnlineStats?.LastDay ?? 0}, всего: {s.Users?.TotalUsers ?? 0}\n"
              + $"📶 трафик за всё время: {CommonUtils.Text.Bytes(s.Nodes?.TotalBytesLifetime ?? 0)}"
            : $"🖥️ ноды онлайн: {list.Count(n => n.IsConnected)}/{list.Count}\n⚠️ статистика: {stats.Error}";

        var total = Pagination.TotalPages(list.Count, PageSize);
        var pages = new List<Embed>(total);

        for (var page = 0; page < total; page++)
            pages.Add(EmbedHandler.Build(new EmbedSpec
            {
                AuthorName = "Панель Remnawave",
                Description = header,
                Color = Color.Teal,
                Fields = list.Count == 0
                    ? [new EmbedFieldSpec("Пусто", "нод нет", false)]
                    : list.Skip(page * PageSize).Take(PageSize)
                        .Select(n => new EmbedFieldSpec($"{Status(n)} {n.Name} {n.CountryCode}", Describe(n), false))
                        .ToList(),
                Footer = EmbedHandler.LoisFooter
            }));

        return (pages, list);
    }

    public static Action<ComponentBuilder, int> BuildControls(List<RemnaNode> nodes) => (builder, page) =>
    {
        var slice = nodes.Skip(page * PageSize).Take(PageSize).ToList();

        if (slice.Count > 0)
            builder.WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("remna_node")
                .WithPlaceholder("🖥️ Выбрать ноду")
                .WithOptions(slice.Select(n => new SelectMenuOptionBuilder()
                    .WithLabel(CommonUtils.Text.Truncate($"{Status(n)} {n.Name}", 100))
                    .WithValue(n.Uuid)
                    .WithDescription(CommonUtils.Text.Truncate(n.Address, 100)))
                    .ToList()), row: 1);

        builder.WithButton("Обновить", "remna_refresh", ButtonStyle.Primary, row: 2);
        builder.WithButton("Диагностика", "remna_diag", ButtonStyle.Secondary, row: 2);
        builder.WithButton("Рестарт всех", "remna_restart_all", ButtonStyle.Danger, row: 2);
    };

    public static async Task<Embed> BuildDiagnosticsAsync(RemnaService remna)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var health = await remna.HealthAsync();
        sw.Stop();
        var nodes = await remna.NodesAsync();

        var problems = 0;
        var fields = new List<EmbedFieldSpec>
        {
            new("🔌 API", health.Ok ? $"✅ ок, {sw.ElapsedMilliseconds} мс" : $"❌ {health.Error}", false)
        };
        if (!health.Ok) problems++;

        if (health.Ok && health.Data?.RuntimeMetrics is { Count: > 0 } runtimes)
            fields.Add(new("🩺 Бэкенд", string.Join("\n", runtimes.Take(6).Select(r =>
                $"{(r.EventLoopP99Ms > 100 ? "⚠️" : "✅")} {r.InstanceType}: RAM {CommonUtils.Text.Bytes(r.Rss)}, "
                + $"event loop p99 {r.EventLoopP99Ms:0} мс, аптайм {CommonUtils.Time.FormatAge(TimeSpan.FromSeconds(r.Uptime))}")), false));

        if (!nodes.Ok)
        {
            problems++;
            fields.Add(new("🖥️ Ноды", $"❌ {nodes.Error}", false));
        }
        else
        {
            var list = nodes.Data ?? [];
            var down = list.Where(n => !n.IsDisabled && !n.IsConnected).ToList();
            var disabled = list.Where(n => n.IsDisabled).ToList();
            var traffic = list.Where(n => n.TrafficLimitBytes is > 0
                && (n.TrafficUsedBytes ?? 0) / n.TrafficLimitBytes.Value * 100 >= (n.NotifyPercent ?? 90)).ToList();
            problems += down.Count + traffic.Count;

            fields.Add(new($"🔴 Лежат ({down.Count}/{list.Count})", down.Count == 0
                ? "✅ все ноды на связи"
                : string.Join("\n", down.Select(n =>
                    $"{Status(n)} **{n.Name}**{(n.LastStatusChange is { } at ? $", лежит {CommonUtils.Time.FormatAge(DateTimeOffset.UtcNow - at)}" : "")}: "
                    + CommonUtils.Text.Truncate(CommonUtils.Text.FirstMeaningfulLine(n.LastStatusMessage, "без сообщения"), 200))), false));

            if (traffic.Count > 0)
                fields.Add(new("📶 Трафик на пределе", string.Join("\n", traffic.Select(n =>
                    $"**{n.Name}**: {CommonUtils.Text.Bytes(n.TrafficUsedBytes ?? 0)} / {CommonUtils.Text.Bytes(n.TrafficLimitBytes.Value)}")), false));

            if (disabled.Count > 0)
                fields.Add(new("⏸️ Выключены", string.Join(", ", disabled.Select(n => n.Name)), false));
        }

        fields.Add(new("🐕 Вотчер", RemnaWatchService.Enabled
            ? $"каждые {RemnaWatchService.Interval.TotalSeconds:0}с, алерт после {RemnaWatchService.FailThreshold} провалов подряд"
              + $"\nTelegram: `{Global.Vars.Cfg.remnaAlertTelegramChat}`{(Global.Vars.Cfg.remnaAlertTelegramTopic == 0 ? "" : $", тема {Global.Vars.Cfg.remnaAlertTelegramTopic}")}"
            : !Global.Vars.Cfg.remnaEnabled ? "выключен, модуль отключён (System:RemnaEnabled)" : "выключен, задай Remna:AlertTelegramChat и Telegram:Token", false));

        return EmbedHandler.Build(new EmbedSpec
        {
            AuthorName = "Диагностика Remnawave",
            Description = problems == 0 ? "✅ проблем нет" : $"⚠️ проблем: {problems}",
            Color = !health.Ok || !nodes.Ok ? Color.Red : problems > 0 ? Color.Orange : Color.Green,
            Fields = fields,
            Timestamp = DateTimeOffset.UtcNow,
            Footer = EmbedHandler.LoisFooter
        });
    }

    public static (Embed Embed, MessageComponent Components) BuildNodeCard(RemnaNode node) =>
    (
        EmbedHandler.Build(new EmbedSpec
        {
            AuthorName = $"{Status(node)} {node.Name} {node.CountryCode}",
            Description = Describe(node),
            Color = node.IsDisabled ? Color.LightGrey : node.IsConnected ? Color.Green : Color.Red,
            Footer = EmbedHandler.LoisFooter
        }),
        new ComponentBuilder()
            .WithButton("Рестарт", $"remna_act:restart:{node.Uuid}", ButtonStyle.Secondary)
            .WithButton(node.IsDisabled ? "Включить" : "Выключить",
                $"remna_act:{(node.IsDisabled ? "enable" : "disable")}:{node.Uuid}",
                node.IsDisabled ? ButtonStyle.Success : ButtonStyle.Danger)
            .WithButton("Обновить", $"remna_act:show:{node.Uuid}", ButtonStyle.Primary)
            .Build()
    );
}
