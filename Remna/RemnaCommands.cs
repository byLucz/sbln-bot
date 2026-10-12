using Discord;
using Discord.Commands;
using Discord.Interactions;
using DiscordTelegramFrontier;
using sblngavnav6.Common;
using sblngavnav6.Core;
using sblngavnav6.Services;

namespace sblngavnav6.Remna;

[RequireDevGuild]
[RequireSuperuser]
public class RemnaCommands : ModuleBase<SocketCommandContext>
{
    private readonly RemnaService _remna;
    private readonly PaginatorService _pager;

    public RemnaCommands(RemnaService remna, PaginatorService pager)
    {
        _remna = remna;
        _pager = pager;
    }

    [Frontier(Buttons = true)]
    [Command("рв")]
    [Alias("ремна", "впн")]
    public async Task RemnaPanel()
    {
        var (pages, nodes) = await RemnaPanelBuilder.BuildAsync(_remna);

        await _pager.SendAsync(
            Context.Channel,
            pages,
            decorate: RemnaPanelBuilder.BuildControls(nodes),
            scope: RemnaPanelBuilder.Scope(Context.Channel.Id, Context.User.Id));
    }

    [Frontier]
    [Command("рв диаг")]
    [Alias("ремна диаг", "впн диаг")]
    public async Task RemnaDiagnostics()
    {
        await ReplyAsync(embed: await RemnaPanelBuilder.BuildDiagnosticsAsync(_remna));
    }
}

public class RemnaInteractions : InteractionModuleBase<IInteractionContext>
{
    private readonly RemnaService _remna;
    private readonly PaginatorService _pager;

    public RemnaInteractions(RemnaService remna, PaginatorService pager)
    {
        _remna = remna;
        _pager = pager;
    }

    [RequireDevGuildInteraction]
    [RequireSuperuserInteraction]
    [ComponentInteraction("remna_refresh")]
    public async Task Refresh()
    {
        if (Context.Interaction is not IComponentInteraction component)
            return;

        await DeferAsync();

        var (pages, nodes) = await RemnaPanelBuilder.BuildAsync(_remna);

        await _pager.ReplaceAsync(component.Message, pages, decorate: RemnaPanelBuilder.BuildControls(nodes));
    }

    [RequireDevGuildInteraction]
    [RequireSuperuserInteraction]
    [ComponentInteraction("remna_diag")]
    public async Task Diagnostics()
    {
        await DeferAsync(ephemeral: true);
        await FollowupAsync(embed: await RemnaPanelBuilder.BuildDiagnosticsAsync(_remna), ephemeral: true);
    }

    [RequireDevGuildInteraction]
    [RequireSuperuserInteraction]
    [ComponentInteraction("remna_node")]
    public async Task SelectNode(string[] values)
    {
        await DeferAsync(ephemeral: true);

        var result = await _remna.NodeAsync(values[0]);
        if (!result.Ok)
        {
            await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed(RemnaPanelBuilder.Source, result.Error), ephemeral: true);
            return;
        }

        var (embed, components) = RemnaPanelBuilder.BuildNodeCard(result.Data);
        await FollowupAsync(embed: embed, components: components, ephemeral: true);
    }

    [RequireDevGuildInteraction]
    [RequireSuperuserInteraction]
    [ComponentInteraction("remna_act:*:*")]
    public async Task NodeAction(string action, string uuid)
    {
        await DeferAsync();

        if (action != "show")
            await LoggingService.LogWarningAsync("REMNA", $"{Context.User.Username} ({Context.User.Id}) жмёт {action} для ноды {uuid}");

        var error = action switch
        {
            "restart" => (await _remna.RestartNodeAsync(uuid)).Error,
            "enable" => (await _remna.EnableNodeAsync(uuid)).Error,
            "disable" => (await _remna.DisableNodeAsync(uuid)).Error,
            "show" => null,
            _ => "неизвестное действие"
        };

        if (error is not null)
        {
            await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed($"{RemnaPanelBuilder.Source}/{action}", error), ephemeral: true);
            return;
        }

        var node = await _remna.NodeAsync(uuid);
        if (!node.Ok)
        {
            await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed(RemnaPanelBuilder.Source, node.Error), ephemeral: true);
            return;
        }

        var (embed, components) = RemnaPanelBuilder.BuildNodeCard(node.Data);
        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = action == "restart" ? "🔄 рестарт отправлен" : "";
            properties.Embed = embed;
            properties.Components = components;
        });
    }

    [RequireDevGuildInteraction]
    [RequireSuperuserInteraction]
    [ComponentInteraction("remna_restart_all")]
    public async Task RestartAll()
    {
        await DeferAsync(ephemeral: true);

        await LoggingService.LogWarningAsync("REMNA", $"{Context.User.Username} ({Context.User.Id}) жмёт рестарт всех нод");

        var result = await _remna.RestartAllAsync();

        await FollowupAsync(embed: result.Ok
            ? EmbedHandler.Simple($"{RemnaPanelBuilder.Source}/restart-all", "🔄 рестарт всех нод отправлен", Color.DarkBlue, EmbedHandler.LoisFooter)
            : await EmbedHandler.CreateErrorEmbed($"{RemnaPanelBuilder.Source}/restart-all", result.Error),
            ephemeral: true);
    }
}
