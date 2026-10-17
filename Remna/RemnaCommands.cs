using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.Rest;
using DiscordTelegramFrontier;
using sblngavnav6.Common;
using sblngavnav6.Core;
using sblngavnav6.Data;
using sblngavnav6.Services;

namespace sblngavnav6.Remna;

[RequireDevAccess]
public class RemnaCommands : ModuleBase<SocketCommandContext>
{
    private readonly RemnaService _remna;
    private readonly RemnaStore _store;
    private readonly RemnaWatchService _watch;
    private readonly PaginatorService _pager;

    public RemnaCommands(RemnaService remna, RemnaStore store, RemnaWatchService watch, PaginatorService pager)
    {
        _remna = remna;
        _store = store;
        _watch = watch;
        _pager = pager;
    }

    [Frontier(Buttons = true)]
    [Command("рв")]
    [Alias("ремна")]
    public async Task RemnaPanel()
    {
        var (pages, nodes) = await RemnaPanelBuilder.BuildAsync(_remna, _store);

        await _pager.SendAsync(
            Context.Channel,
            pages,
            decorate: RemnaPanelBuilder.BuildControls(_remna, nodes),
            scope: RemnaPanelBuilder.Scope(Context.Channel.Id, Context.User.Id));
    }

    [Frontier]
    [Command("рв диаг")]
    [Alias("ремна диаг")]
    public async Task RemnaDiagnostics()
    {
        await ReplyAsync(embed: await RemnaPanelBuilder.BuildDiagnosticsAsync(_remna, _store));
    }

    [Frontier]
    [Discord.Commands.RequireOwner(ErrorMessage = "недостаточно прав")]
    [Command("рв тех")]
    [Alias("ремна тех")]
    public async Task RemnaMaintenance(string node, [Remainder] string message = null)
    {
        var target = await FindNodeAsync(node, "тех");
        if (target is null)
            return;

        await LoggingService.LogWarningAsync("REMNA", $"{Context.User.Username} ({Context.User.Id}) ставит тех. работы на {target.Node.Name}");

        var error = await _watch.StartMaintenanceAsync(target.Server, target.Node, message, Context.User.Username);
        if (error is not null)
        {
            await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed($"{RemnaPanelBuilder.Source}/тех", error));
            return;
        }

        var refreshed = await _remna.NodeAsync(target.Server, target.Node.Uuid);
        var (embed, _) = RemnaPanelBuilder.BuildNodeCard(_remna, target.Server, refreshed.Data ?? target.Node,
            await _store.MaintenanceAsync(target.Server, target.Node.Uuid), owner: true);
        await ReplyAsync(embed: embed);
    }

    [Frontier(Buttons = true)]
    [Command("рв рагент")]
    [Alias("ремна рагент")]
    public async Task RemnaAgents()
    {
        var (embed, nodes) = await RemnaPanelBuilder.BuildAgentsAsync(_remna);

        await _pager.SendAsync(
            Context.Channel,
            [embed],
            decorate: RemnaPanelBuilder.BuildAgentControls(_remna, nodes),
            scope: $"remna-agent:{Context.Channel.Id}:{Context.User.Id}");
    }

    private async Task<RemnaNodeRef> FindNodeAsync(string node, string action)
    {
        var all = (await _remna.AllNodesAsync()).Refs();
        var matches = all.Where(n => string.Equals(n.Node.Name, node, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
            matches = all.Where(n => n.Node.Name?.Contains(node, StringComparison.OrdinalIgnoreCase) == true).ToList();

        if (matches.Count == 1)
            return matches[0];

        await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed($"{RemnaPanelBuilder.Source}/{action}", matches.Count == 0
            ? $"нода «{node}» не найдена"
            : $"под «{node}» подходит несколько нод: {string.Join(", ", matches.Select(m => _remna.Multi ? $"{m.Node.Name} / {m.Server.Name}" : m.Node.Name))}"));
        return null;
    }
}

public class RemnaMaintenanceModal : IModal
{
    public string Title => "🛠 Тех. работы";

    [InputLabel("Что делаем")]
    [ModalTextInput("message", TextInputStyle.Paragraph, placeholder: RemnaWatchService.DefaultMaintenance, maxLength: 300)]
    [RequiredInput(false)]
    public string Message { get; set; }
}

public class RemnaInteractions : InteractionModuleBase<IInteractionContext>
{
    private readonly RemnaService _remna;
    private readonly RemnaStore _store;
    private readonly RemnaWatchService _watch;
    private readonly PaginatorService _pager;

    public RemnaInteractions(RemnaService remna, RemnaStore store, RemnaWatchService watch, PaginatorService pager)
    {
        _remna = remna;
        _store = store;
        _watch = watch;
        _pager = pager;
    }

    [RequireDevAccessInteraction]
    [ComponentInteraction("remna_refresh")]
    public async Task Refresh()
    {
        if (Context.Interaction is not IComponentInteraction component)
            return;

        await DeferAsync();

        var (pages, nodes) = await RemnaPanelBuilder.BuildAsync(_remna, _store);

        await _pager.ReplaceAsync(component.Message, pages, decorate: RemnaPanelBuilder.BuildControls(_remna, nodes));
    }

    [RequireDevAccessInteraction]
    [ComponentInteraction("remna_diag")]
    public async Task Diagnostics()
    {
        await DeferAsync(ephemeral: true);
        await FollowupAsync(embed: await RemnaPanelBuilder.BuildDiagnosticsAsync(_remna, _store), ephemeral: true);
    }

    [RequireDevAccessInteraction]
    [ComponentInteraction("remna_node")]
    public async Task SelectNode(string[] values)
    {
        await DeferAsync(ephemeral: true);

        var parts = values[0].Split(':', 2);
        await ShowCardAsync(_remna.Server(parts[0]), parts.Length > 1 ? parts[1] : "", null, "", newMessage: true);
    }

    [RequireDevAccessInteraction]
    [ComponentInteraction("remna_act:*:*:*")]
    public async Task NodeAction(string action, string serverIndex, string uuid)
    {
        await DeferAsync();

        var server = _remna.Server(serverIndex);
        if (action != "show")
            await LoggingService.LogWarningAsync("REMNA", $"{Context.User.Username} ({Context.User.Id}) жмёт {action} для ноды {uuid} на {server?.Name}");

        var error = action switch
        {
            "restart" => (await _remna.RestartNodeAsync(server, uuid)).Error,
            "enable" => (await _remna.EnableNodeAsync(server, uuid)).Error,
            "disable" => (await _remna.DisableNodeAsync(server, uuid)).Error,
            "mtend" => await SuperuserGate.IsBotOwnerAsync(Context.Client, Context.User)
                ? await EndMaintenanceAsync(server, uuid)
                : "недостаточно прав",
            "show" => null,
            _ => "неизвестное действие"
        };

        await ShowCardAsync(server, uuid, error, action == "restart" ? "🔄 рестарт отправлен" : "");
    }

    [RequireDevAccessInteraction]
    [RequireOwnerInteraction]
    [ComponentInteraction("remna_mt:*:*")]
    public async Task StartMaintenance(string serverIndex, string uuid)
    {
        try
        {
            await RespondWithModalAsync<RemnaMaintenanceModal>($"remna_mtm:{serverIndex}:{uuid}");
        }
        catch (NotSupportedException)
        {
            await DeferAsync();
            var server = _remna.Server(serverIndex);
            var error = await BeginMaintenanceAsync(server, uuid, null);
            await ShowCardAsync(server, uuid, error,
                $"🛠 тех. работы начаты с сообщением «{RemnaWatchService.DefaultMaintenance}», свой текст: рв тех <нода> <текст>");
        }
    }

    [RequireDevAccessInteraction]
    [RequireOwnerInteraction]
    [ModalInteraction("remna_mtm:*:*")]
    public async Task SubmitMaintenance(string serverIndex, string uuid, RemnaMaintenanceModal modal)
    {
        await DeferAsync(ephemeral: true);

        var server = _remna.Server(serverIndex);
        await ShowCardAsync(server, uuid, await BeginMaintenanceAsync(server, uuid, modal.Message), "");
    }

    [RequireDevAccessInteraction]
    [ComponentInteraction("remna_agents_refresh")]
    public async Task AgentsRefresh()
    {
        await DeferAsync();
        var (embed, nodes) = await RemnaPanelBuilder.BuildAgentsAsync(_remna);
        var components = _pager.BuildControls(RemnaPanelBuilder.BuildAgentControls(_remna, nodes));
        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Embed = embed;
            properties.Components = components;
        });
    }

    [RequireDevAccessInteraction]
    [ComponentInteraction("remna_agent")]
    public async Task SelectAgent(string[] values)
    {
        await DeferAsync(ephemeral: true);
        var parts = values[0].Split(':', 2);
        await ShowAgentAsync(_remna.Server(parts[0]), parts.Length > 1 ? parts[1] : "", newMessage: true);
    }

    [RequireDevAccessInteraction]
    [ComponentInteraction("remna_ag:*:*:*")]
    public async Task AgentAction(string action, string serverIndex, string uuid)
    {
        await DeferAsync();
        var server = _remna.Server(serverIndex);

        if (action == "show")
        {
            await ShowAgentAsync(server, uuid, newMessage: false);
            return;
        }

        var node = await _remna.NodeAsync(server, uuid);
        if (!node.Ok)
        {
            await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed(RemnaWatchService.AgentName, node.Error), ephemeral: true);
            return;
        }

        if (action == "clean")
        {
            if (!await SuperuserGate.IsBotOwnerAsync(Context.Client, Context.User))
            {
                await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed(RemnaWatchService.AgentName, "недостаточно прав"), ephemeral: true);
                return;
            }

            await LoggingService.LogWarningAsync("REMNA", $"{Context.User.Username} ({Context.User.Id}) чистит диск на {node.Data.Name}");
            var progress = await FollowupAsync(embed: EmbedHandler.Simple(RemnaWatchService.AgentName,
                $"🧹 чищу диск на {node.Data.DisplayName}, это до пары минут", Color.DarkBlue, EmbedHandler.LoisFooter), ephemeral: true);
            var result = await _remna.AgentCleanAsync(node.Data);
            var embed = result is { Ok: true, Data.Ok: true }
                ? EmbedHandler.Simple(RemnaWatchService.AgentName,
                    $"🧹 {node.Data.DisplayName}: освобождено {CommonUtils.Text.Bytes(result.Data.Freed)}, свободно {CommonUtils.Text.Bytes(result.Data.Free)}",
                    Color.Green, EmbedHandler.LoisFooter)
                : await EmbedHandler.CreateErrorEmbed(RemnaWatchService.AgentName, result.Error ?? result.Data?.Error ?? "очистка не удалась");
            await (progress is RestFollowupMessage followup ? followup.ModifyAsync(properties => properties.Embed = embed) : progress.ModifyAsync(properties => properties.Embed = embed));
            return;
        }

        if (action == "sni")
        {
            var current = node.Data.Snis.FirstOrDefault();
            var progress = await FollowupAsync(embed: EmbedHandler.Simple(RemnaWatchService.AgentName,
                $"🔎 {RemnaWatchService.AgentName} на {node.Data.DisplayName} сканирует домены, это до пары минут", Color.DarkBlue, EmbedHandler.LoisFooter), ephemeral: true);
            var scan = await _remna.ScanAsync(node.Data, current);
            var embed = scan.Ok
                ? RemnaPanelBuilder.BuildScanEmbed(_remna, new RemnaNodeRef(server, node.Data), current, scan.Data)
                : await EmbedHandler.CreateErrorEmbed(RemnaWatchService.AgentName, scan.Error);
            await (progress is RestFollowupMessage followup ? followup.ModifyAsync(properties => properties.Embed = embed) : progress.ModifyAsync(properties => properties.Embed = embed));
        }
    }

    [RequireDevAccessInteraction]
    [ComponentInteraction("remna_restart_all")]
    public async Task RestartAll()
    {
        await DeferAsync(ephemeral: true);

        await LoggingService.LogWarningAsync("REMNA", $"{Context.User.Username} ({Context.User.Id}) жмёт рестарт всех нод");

        var results = await Task.WhenAll(_remna.Servers.Select(async server => (Server: server, Result: await _remna.RestartAllAsync(server))));
        var failed = results.Where(r => !r.Result.Ok).ToList();

        await FollowupAsync(embed: failed.Count == 0
            ? EmbedHandler.Simple($"{RemnaPanelBuilder.Source}/restart-all", "🔄 рестарт всех нод отправлен", Color.DarkBlue, EmbedHandler.LoisFooter)
            : await EmbedHandler.CreateErrorEmbed($"{RemnaPanelBuilder.Source}/restart-all",
                string.Join("\n", failed.Select(f => $"{f.Server.Name}: {f.Result.Error}"))),
            ephemeral: true);
    }

    private async Task ShowAgentAsync(RemnaServer server, string uuid, bool newMessage)
    {
        var node = await _remna.NodeAsync(server, uuid);
        if (!node.Ok)
        {
            await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed(RemnaWatchService.AgentName, node.Error), ephemeral: true);
            return;
        }

        var (embed, components) = RemnaPanelBuilder.BuildAgentCard(server, node.Data, await _remna.AgentHealthAsync(node.Data),
            await SuperuserGate.IsBotOwnerAsync(Context.Client, Context.User));
        if (newMessage)
            await FollowupAsync(embed: embed, components: components, ephemeral: true);
        else
            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Embed = embed;
                properties.Components = components;
            });
    }

    private async Task<string> BeginMaintenanceAsync(RemnaServer server, string uuid, string message)
    {
        var node = await _remna.NodeAsync(server, uuid);
        if (!node.Ok)
            return node.Error;

        await LoggingService.LogWarningAsync("REMNA", $"{Context.User.Username} ({Context.User.Id}) ставит тех. работы на {node.Data.Name}");
        return await _watch.StartMaintenanceAsync(server, node.Data, message, Context.User.Username);
    }

    private async Task<string> EndMaintenanceAsync(RemnaServer server, string uuid)
    {
        var node = await _remna.NodeAsync(server, uuid);
        return node.Ok ? await _watch.EndMaintenanceAsync(server, node.Data) : node.Error;
    }

    private async Task ShowCardAsync(RemnaServer server, string uuid, string error, string content, bool newMessage = false)
    {
        if (error is not null)
        {
            await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed(RemnaPanelBuilder.Source, error), ephemeral: true);
            return;
        }

        var node = await _remna.NodeAsync(server, uuid);
        if (!node.Ok)
        {
            await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed(RemnaPanelBuilder.Source, node.Error), ephemeral: true);
            return;
        }

        var (embed, components) = RemnaPanelBuilder.BuildNodeCard(_remna, server, node.Data, await _store.MaintenanceAsync(server, uuid),
            await SuperuserGate.IsBotOwnerAsync(Context.Client, Context.User));
        if (newMessage)
        {
            await FollowupAsync(embed: embed, components: components, ephemeral: true);
            return;
        }

        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = content;
            properties.Embed = embed;
            properties.Components = components;
        });
    }
}
