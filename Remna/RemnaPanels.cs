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

    public static string Status(RemnaNode node, RemnaMaintenance maintenance = null) =>
        maintenance is not null ? "🛠"
        : node.IsDisabled ? "⏸️"
        : node.IsConnected ? "🟢"
        : node.IsConnecting ? "🟡"
        : "🔴";

    public static string Describe(RemnaNode node, RemnaMaintenance maintenance = null)
    {
        var stats = node.System?.Stats;
        var total = node.System?.Info?.MemoryTotal is > 0 and var known ? known : (stats?.MemoryUsed ?? 0) + (stats?.MemoryFree ?? 0);
        var resources = stats is null || total <= 0
            ? "🧠 RAM: нет данных"
            : $"🧠 RAM {CommonUtils.Text.Bytes(stats.MemoryUsed)}/{CommonUtils.Text.Bytes(total)} ({stats.MemoryUsed / total * 100:0}%)"
              + (stats.LoadAvg is { Count: > 0 } load ? (node.System.Info?.Cpus is > 0 and var cpus ? $", ⚙️ load {load[0] / cpus * 100:0}%" : $", ⚙️ load {load[0].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}") : "");

        var online = $"👥 онлайн: {node.UsersOnline ?? 0}"
            + (node.IsConnected && node.XrayUptime is > 0 ? $", ⏱️ аптайм: {CommonUtils.Time.FormatAge(TimeSpan.FromSeconds(node.XrayUptime.Value))}" : "");
        var sni = node.Snis is { Count: > 0 } snis
            ? $"🎭 SNI: {string.Join(", ", snis.Take(3))}{(snis.Count > 3 ? $" +{snis.Count - 3}" : "")}"
            : "🎭 SNI: нет";

        var lines = new List<string>
        {
            $"`{node.Address}{(node.Port is null ? "" : $":{node.Port}")}`",
            online,
            resources,
            sni + (node.Versions?.Xray is { Length: > 0 } xray ? $", xray {xray}" : "")
        };

        if (maintenance is not null)
            lines.Add($"🛠 тех. работы {CommonUtils.Time.FormatAge(DateTimeOffset.UtcNow - maintenance.Since)}: {maintenance.Message} ({maintenance.By})");
        else if (!node.IsConnected && !node.IsDisabled)
            lines.Add($"⚠️ {CommonUtils.Text.FirstMeaningfulLine(node.LastStatusMessage, "без сообщения", 150)}");

        return string.Join("\n", lines);
    }

    private static string Title(RemnaService remna, RemnaNodeRef item, RemnaMaintenance maintenance)
        => $"{Status(item.Node, maintenance)} {item.Node.DisplayName}{(remna.Multi ? $" / {item.Server.Name}" : "")}";

    private static RemnaMaintenance Find(List<RemnaMaintenance> maintenance, RemnaNodeRef item)
        => maintenance.FirstOrDefault(m => m.Server == item.Server.Url && m.Uuid == item.Node.Uuid);

    public static async Task<(List<Embed> Pages, List<RemnaNodeRef> Nodes)> BuildAsync(RemnaService remna, RemnaStore store)
    {
        if (remna.Servers.Count == 0)
            return ([await EmbedHandler.CreateErrorEmbed(Source, "не задан ни один сервер в Remna:Servers")], []);

        var nodesTask = remna.AllNodesAsync();
        var statsTask = Task.WhenAll(remna.Servers.Select(server => remna.StatsAsync(server)));
        var servers = await nodesTask;
        var stats = await statsTask;
        var maintenance = await store.AllMaintenanceAsync();

        var list = servers.Refs();
        var ok = stats.Where(s => s.Ok && s.Data is not null).Select(s => s.Data).ToList();

        var header = new List<string>
        {
            $"🖥️ ноды онлайн: {list.Count(n => n.Node.IsConnected)}/{list.Count}"
                + (remna.Multi ? $", серверов: {servers.Count(s => s.Result.Ok)}/{servers.Count}" : ""),
            $"👥 юзеров онлайн: {ok.Sum(s => s.OnlineStats?.OnlineNow ?? 0)}, за сутки: {ok.Sum(s => s.OnlineStats?.LastDay ?? 0)}, всего: {ok.Sum(s => s.Users?.TotalUsers ?? 0)}",
            $"📶 трафик за всё время: {CommonUtils.Text.Bytes(ok.Sum(s => s.Nodes?.TotalBytesLifetime ?? 0))}"
        };
        header.AddRange(servers.Where(s => !s.Result.Ok).Select(s => $"❌ {s.Server.Name}: {s.Result.Error}"));

        var total = Pagination.TotalPages(list.Count, PageSize);
        var pages = new List<Embed>(total);

        for (var page = 0; page < total; page++)
            pages.Add(EmbedHandler.Build(new EmbedSpec
            {
                AuthorName = "Панель Remnawave",
                Description = string.Join("\n", header),
                Color = Color.Teal,
                Fields = list.Count == 0
                    ? [new EmbedFieldSpec("Пусто", "нод нет", false)]
                    : list.Skip(page * PageSize).Take(PageSize)
                        .Select(item =>
                        {
                            var entry = Find(maintenance, item);
                            return new EmbedFieldSpec(Title(remna, item, entry), Describe(item.Node, entry), false);
                        })
                        .ToList(),
                Footer = EmbedHandler.LoisFooter
            }));

        return (pages, list);
    }

    public static Action<ComponentBuilder, int> BuildControls(RemnaService remna, List<RemnaNodeRef> nodes) => (builder, page) =>
    {
        var slice = nodes.Skip(page * PageSize).Take(PageSize).ToList();

        if (slice.Count > 0)
            builder.WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("remna_node")
                .WithPlaceholder("🖥️ Выбрать ноду")
                .WithOptions(slice.Select(item => new SelectMenuOptionBuilder()
                    .WithLabel(CommonUtils.Text.Truncate($"{Status(item.Node)} {item.Node.Name}", 100))
                    .WithValue(item.Key)
                    .WithDescription(CommonUtils.Text.Truncate(remna.Multi ? $"{item.Server.Name} / {item.Node.Address}" : item.Node.Address, 100)))
                    .ToList()), row: 1);

        builder.WithButton("Обновить", "remna_refresh", ButtonStyle.Primary, row: 2);
        builder.WithButton("Диагностика", "remna_diag", ButtonStyle.Secondary, row: 2);
        builder.WithButton("Рестарт всех", "remna_restart_all", ButtonStyle.Danger, row: 2);
    };

    public static async Task<Embed> BuildDiagnosticsAsync(RemnaService remna, RemnaStore store)
    {
        var problems = 0;
        var failed = false;
        var fields = new List<EmbedFieldSpec>();

        var serversTask = remna.AllNodesAsync();
        var checks = await Task.WhenAll(remna.Servers.Select(async server =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var health = await remna.HealthAsync(server);
            sw.Stop();
            return (Server: server, Health: health, Elapsed: sw.ElapsedMilliseconds);
        }));

        fields.Add(new("🔌 API", checks.Length == 0
            ? "❌ не задан ни один сервер в Remna:Servers"
            : string.Join("\n", checks.Select(c => $"{(c.Health.Ok ? "✅" : "❌")} {c.Server.Name}: "
                + (c.Health.Ok ? $"{c.Elapsed} мс" : c.Health.Error))), false));
        problems += checks.Count(c => !c.Health.Ok) + (checks.Length == 0 ? 1 : 0);
        failed |= checks.Length == 0 || checks.Any(c => !c.Health.Ok);

        var runtimes = checks.Where(c => c.Health.Ok && c.Health.Data?.RuntimeMetrics is { Count: > 0 })
            .SelectMany(c => c.Health.Data.RuntimeMetrics.Take(6).Select(r =>
                $"{(r.EventLoopP99Ms > 100 ? "⚠️" : "✅")} {(remna.Multi ? $"{c.Server.Name} " : "")}{r.InstanceType}: RAM {CommonUtils.Text.Bytes(r.Rss)}, "
                + $"event loop p99 {r.EventLoopP99Ms:0} мс, аптайм {CommonUtils.Time.FormatAge(TimeSpan.FromSeconds(r.Uptime))}"))
            .ToList();
        if (runtimes.Count > 0)
            fields.Add(new("🩺 Бэкенд", string.Join("\n", runtimes), false));

        var servers = await serversTask;
        var maintenance = await store.AllMaintenanceAsync();
        foreach (var broken in servers.Where(s => !s.Result.Ok))
        {
            problems++;
            failed = true;
            fields.Add(new($"🖥️ Ноды {broken.Server.Name}", $"❌ {broken.Result.Error}", false));
        }

        var list = servers.Refs();
        var down = list.Where(n => !n.Node.IsDisabled && !n.Node.IsConnected).ToList();
        var traffic = list.Where(n => n.Node.TrafficLimitBytes is > 0
            && (n.Node.TrafficUsedBytes ?? 0) / n.Node.TrafficLimitBytes.Value * 100 >= (n.Node.NotifyPercent ?? 90)).ToList();
        var disabled = list.Where(n => n.Node.IsDisabled && Find(maintenance, n) is null).ToList();
        problems += down.Count + traffic.Count;

        fields.Add(new($"🔴 Лежат ({down.Count}/{list.Count})", down.Count == 0
            ? "✅ все ноды на связи"
            : string.Join("\n", down.Select(n =>
                $"{Status(n.Node)} **{n.Node.Name}**{(remna.Multi ? $" / {n.Server.Name}" : "")}"
                + $"{(n.Node.LastStatusChange is { } at ? $", лежит {CommonUtils.Time.FormatAge(DateTimeOffset.UtcNow - at)}" : "")}: "
                + CommonUtils.Text.FirstMeaningfulLine(n.Node.LastStatusMessage, "без сообщения", 200))), false));

        if (maintenance.Count > 0)
            fields.Add(new("🛠 Тех. работы", string.Join("\n", maintenance.Select(m =>
                $"**{m.Node}**, {CommonUtils.Time.FormatAge(DateTimeOffset.UtcNow - m.Since)}: {m.Message} ({m.By})")), false));

        if (traffic.Count > 0)
            fields.Add(new("📶 Трафик на пределе", string.Join("\n", traffic.Select(n =>
                $"**{n.Node.Name}**: {CommonUtils.Text.Bytes(n.Node.TrafficUsedBytes ?? 0)} / {CommonUtils.Text.Bytes(n.Node.TrafficLimitBytes.Value)}")), false));

        if (disabled.Count > 0)
            fields.Add(new("⏸️ Выключены", string.Join(", ", disabled.Select(n => n.Node.Name)), false));

        var probes = await RemnaProbe.CheckAsync(remna, servers);
        var unreachable = probes.Where(p => !p.Ok).ToList();
        problems += unreachable.Count;
        if (probes.Count > 0)
            fields.Add(new($"🔐 Клиентские подключения ({probes.Count - unreachable.Count}/{probes.Count})", unreachable.Count == 0
                ? $"✅ порты отвечают, ближайший сертификат: {probes.Where(p => p.CertDays is not null).Select(p => p.CertDays).DefaultIfEmpty().Min()?.ToString() ?? "-"} дн."
                : string.Join("\n", unreachable.Select(p =>
                    $"{(p.Expiring ? "⚠️" : "❌")} **{p.Target.Label}** `{p.Target.Endpoint}`{(p.Target.Tls ? $" SNI {p.Target.Sni}" : "")}: {p.Error}")), false));

        var incidents = await store.IncidentsAsync();
        var now = DateTimeOffset.UtcNow;
        fields.Add(new($"📜 Падения за {RemnaStore.HistoryWindow.TotalHours:0}ч", incidents.Count == 0
            ? "✅ падений не было"
            : string.Join("\n", incidents
                .GroupBy(i => (i.Server, i.Uuid, i.Node))
                .Select(g => (g.Key.Node, Count: g.Count(), Open: g.Any(i => i.End is null),
                    Downtime: TimeSpan.FromTicks(g.Sum(i => ((i.End ?? now) - (i.Start < now - RemnaStore.HistoryWindow ? now - RemnaStore.HistoryWindow : i.Start)).Ticks)),
                    Last: g.First()))
                .OrderByDescending(g => g.Downtime)
                .Select(g => $"{(g.Open ? "🔴" : "🟢")} **{g.Node}**: {g.Count} раз, простой {CommonUtils.Time.FormatAge(g.Downtime)}, "
                    + $"последнее {CommonUtils.Time.FormatAge(now - g.Last.Start)} назад: {CommonUtils.Text.Truncate(g.Last.Message, 120)}")), false));

        fields.Add(new($"🐕 {RemnaWatchService.Name}", RemnaWatchService.Enabled
            ? $"каждые {RemnaWatchService.Interval.TotalSeconds:0}с"
              + $"\nTelegram: `{Global.Vars.Cfg.remnaAlertTelegramChat}`{(Global.Vars.Cfg.remnaAlertTelegramTopic == 0 ? "" : $", тема {Global.Vars.Cfg.remnaAlertTelegramTopic}")}"
            : !Global.Vars.Cfg.remnaEnabled ? "выключен, модуль отключён (System:RemnaEnabled)" : "выключен, задай Remna:Servers, Remna:AlertTelegramChat и Telegram:Token", false));

        return EmbedHandler.Build(new EmbedSpec
        {
            AuthorName = "Диагностика Remnawave",
            Description = problems == 0 ? "✅ проблем нет" : $"⚠️ проблем: {problems}",
            Color = failed ? Color.Red : problems > 0 ? Color.Orange : Color.Green,
            Fields = fields,
            Timestamp = DateTimeOffset.UtcNow,
            Footer = EmbedHandler.LoisFooter
        });
    }

    public static Embed BuildScanEmbed(RemnaService remna, RemnaNodeRef target, string current, RemnaScan scan)
    {
        var best = scan.Results?.FirstOrDefault();
        var mine = scan.Current;
        var lines = new List<string>
        {
            current is null ? "🎭 текущий SNI не найден в профиле ноды"
                : mine is null ? $"🎭 текущий SNI `{current}`: агент его не проверил"
                : mine.Ok ? $"🎭 текущий SNI `{current}`: ✅ {mine.Score} очк., tcp {mine.TcpMs:0.#} мс, tls {mine.TlsMs:0.#} мс"
                : $"🎭 текущий SNI `{current}`: ❌ {mine.Error}"
        };

        if (best is not null && !string.Equals(best.Domain, current, StringComparison.OrdinalIgnoreCase))
        {
            if (mine is not { Ok: true })
                lines.Add($"💡 стоит сменить на `{best.Domain}` ({best.Score} очк.)");
            else if (best.Score > mine.Score + 5)
                lines.Add($"💡 лучше текущего: `{best.Domain}` (+{best.Score - mine.Score} очк.)");
            else
                lines.Add("👌 текущий SNI в числе лучших");
        }

        lines.Add($"🔎 прошли {scan.Passed}/{scan.Total} доменов за {scan.ElapsedMs / 1000.0:0.#}с, {RemnaWatchService.AgentName} {scan.Host} v{scan.Version}");

        return EmbedHandler.Build(new EmbedSpec
        {
            AuthorName = $"Скан SNI: {target.Node.DisplayName}{(remna.Multi ? $" / {target.Server.Name}" : "")}",
            Description = string.Join("\n", lines),
            Color = mine is { Ok: true } ? Color.Green : Color.Orange,
            Fields =
            [
                new EmbedFieldSpec("🏆 Лучшие кандидаты", CommonUtils.Text.CodeTable(
                    new[] { (IReadOnlyList<string>)["домен", "очки", "tcp", "tls", "серт"] }
                        .Concat((scan.Results ?? []).Select(r => (IReadOnlyList<string>)
                            [r.Domain, r.Score.ToString(), $"{r.TcpMs:0.#}", $"{r.TlsMs:0.#}", $"{r.CertDays}д"])),
                    "ни один домен не прошёл проверку"), false)
            ],
            Footer = EmbedHandler.LoisFooter
        });
    }

    public static (Embed Embed, MessageComponent Components) BuildNodeCard(RemnaService remna, RemnaServer server, RemnaNode node, RemnaMaintenance maintenance, bool owner)
    {
        var id = $"{server.Index}:{node.Uuid}";
        var buttons = new ComponentBuilder()
            .WithButton("Рестарт", $"remna_act:restart:{id}", ButtonStyle.Secondary);

        if (maintenance is not null)
        {
            if (owner)
                buttons.WithButton("✅ Работы завершены", $"remna_act:mtend:{id}", ButtonStyle.Success);
        }
        else
        {
            buttons.WithButton(node.IsDisabled ? "Включить" : "Выключить",
                $"remna_act:{(node.IsDisabled ? "enable" : "disable")}:{id}",
                node.IsDisabled ? ButtonStyle.Success : ButtonStyle.Danger);
            if (owner)
                buttons.WithButton("🛠 Тех. работы", $"remna_mt:{id}", ButtonStyle.Secondary);
        }

        buttons.WithButton("Обновить", $"remna_act:show:{id}", ButtonStyle.Primary);

        return (
            EmbedHandler.Build(new EmbedSpec
            {
                AuthorName = Title(remna, new RemnaNodeRef(server, node), maintenance),
                Description = Describe(node, maintenance),
                Color = maintenance is not null ? Color.Orange : node.IsDisabled ? Color.LightGrey : node.IsConnected ? Color.Green : Color.Red,
                Footer = EmbedHandler.LoisFooter
            }),
            buttons.Build());
    }
}
