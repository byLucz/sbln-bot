using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using sblngavnav5X.Core;
using sblngavnav5X.Data;
using sblngavnav5X.Services;

namespace sblngavnav5X.Commands;

public class RequirePgRoleAttribute : Discord.Commands.PreconditionAttribute
{
    public override Task<Discord.Commands.PreconditionResult> CheckPermissionsAsync(
        ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        if (Utils.pgApiRoleId == 0)
            return Task.FromResult(Discord.Commands.PreconditionResult.FromError("pgApiRoleId не задан в Utils"));

        if (context.User is not IGuildUser gu || !gu.RoleIds.Contains(Utils.pgApiRoleId))
            return Task.FromResult(Discord.Commands.PreconditionResult.FromError("Нет доступа к pgAPI"));

        return Task.FromResult(Discord.Commands.PreconditionResult.FromSuccess());
    }
}

public class RequirePgRoleInteractionAttribute : Discord.Interactions.PreconditionAttribute
{
    public override Task<Discord.Interactions.PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
    {
        if (Utils.pgApiRoleId == 0)
            return Task.FromResult(Discord.Interactions.PreconditionResult.FromError("pgApiRoleId не задан в Utils"));

        if (context.User is not IGuildUser gu || !gu.RoleIds.Contains(Utils.pgApiRoleId))
            return Task.FromResult(Discord.Interactions.PreconditionResult.FromError("Нет доступа к pgAPI"));

        return Task.FromResult(Discord.Interactions.PreconditionResult.FromSuccess());
    }
}

public static class PgApiPanelBuilder
{
    public const string DefaultProject = "pg";

    public static readonly (string Label, string Key)[] Actions =
    [
        ("Health-check", "health"),
        ("Проекты",      "projects"),
        ("Статус pg",    "status"),
        ("Сервисы pg",   "services"),
        ("Логи pg",      "logs"),
        ("Логи bot",     "logs_bot"),
        ("Рестарт pg",   "restart"),
        ("Рестарт bot",  "restart_bot"),
        ("Стоп pg",      "stop"),
        ("Старт pg",     "start")
    ];

    public static MessageComponent BuildPageComponents(int page)
    {
        const int pageSize = 5;
        var totalPages = (int)Math.Ceiling(Actions.Length / (double)pageSize);
        page = Math.Clamp(page, 0, totalPages - 1);

        var builder = new ComponentBuilder();
        var items = Actions.Skip(page * pageSize).Take(pageSize).ToArray();

        foreach (var item in items)
        {
            var style = item.Key.Contains("stop")    ? ButtonStyle.Danger
                      : item.Key.Contains("restart") ? ButtonStyle.Secondary
                      : ButtonStyle.Primary;
            builder.WithButton(item.Label, $"pgapi_action:{item.Key}", style, row: 0);
        }

        builder.WithButton("◀️", $"pgapi_page:{Math.Max(page - 1, 0)}",          ButtonStyle.Secondary, disabled: page == 0,              row: 1);
        builder.WithButton($"Стр. {page + 1}/{totalPages}", "pgapi_page:noop",   ButtonStyle.Secondary, disabled: true,                   row: 1);
        builder.WithButton("▶️", $"pgapi_page:{Math.Min(page + 1, totalPages - 1)}", ButtonStyle.Secondary, disabled: page >= totalPages - 1, row: 1);

        return builder.Build();
    }

    private static string? _cachedPing;

    public static Embed BuildPanelEmbed(int page, string? pingInfo = null)
    {
        if (pingInfo is not null) _cachedPing = pingInfo;

        var eb = EmbedHandler.FieldsEmbed("Панель управления pgAPI", Color.Teal, "sbln x lois.media")
            .WithDescription("[PG Container v0.2](https://github.com/loismedia/pg)");

        if (_cachedPing is not null)
            eb.AddField("⏱️ Время отклика", _cachedPing, inline: true);

        return eb.Build();
    }
}

[RequirePgRole]
public class PgApiCommands : ModuleBase<SocketCommandContext>
{
    private readonly PgApiService _pgApi;

    public PgApiCommands(PgApiService pgApi)
    {
        _pgApi = pgApi;
    }

    [Command("пг")]
    public async Task PgApiPanel()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string health = await _pgApi.HealthAsync();
        sw.Stop();

        string pingInfo = health.StartsWith("❌")
            ? "недоступен"
            : $"{sw.ElapsedMilliseconds} мс";

        await Context.Channel.SendMessageAsync(
            embed: PgApiPanelBuilder.BuildPanelEmbed(0, pingInfo),
            components: PgApiPanelBuilder.BuildPageComponents(0));
    }
}

public class PgApiInteractions : InteractionModuleBase<SocketInteractionContext>
{
    private readonly PgApiService _pgApi;

    public PgApiInteractions(PgApiService pgApi)
    {
        _pgApi = pgApi;
    }

    [RequirePgRoleInteraction]
    [ComponentInteraction("pgapi_page:*")]
    public async Task ChangePage(string pageRaw)
    {
        if (!int.TryParse(pageRaw, out var page))
            page = 0;

        if (Context.Interaction is not SocketMessageComponent component)
            return;

        await component.UpdateAsync(msg =>
        {
            msg.Embed = PgApiPanelBuilder.BuildPanelEmbed(page);
            msg.Components = PgApiPanelBuilder.BuildPageComponents(page);
        });
    }

    [RequirePgRoleInteraction]
    [ComponentInteraction("pgapi_action:*")]
    public async Task ExecuteAction(string action)
    {
        await DeferAsync(ephemeral: true);

        string result;
        try
        {
            result = action switch
            {
                "health"      => await _pgApi.HealthAsync(),
                "projects"    => await _pgApi.ProjectsAsync(),
                "status"      => await _pgApi.ProjectStatusAsync(PgApiPanelBuilder.DefaultProject),
                "services"    => await _pgApi.ServicesAsync(PgApiPanelBuilder.DefaultProject),
                "logs"        => await _pgApi.LogsAsync(PgApiPanelBuilder.DefaultProject),
                "logs_bot"    => await _pgApi.LogsAsync(PgApiPanelBuilder.DefaultProject, "bot"),
                "restart"     => await _pgApi.RestartProjectAsync(PgApiPanelBuilder.DefaultProject),
                "restart_bot" => await _pgApi.RestartServiceAsync(PgApiPanelBuilder.DefaultProject, "bot"),
                "stop"        => await _pgApi.StopProjectAsync(PgApiPanelBuilder.DefaultProject),
                "start"       => await _pgApi.StartProjectAsync(PgApiPanelBuilder.DefaultProject),
                _             => "❌ Неизвестное действие"
            };
        }
        catch (Exception ex)
        {
            result = $"❌ Ошибка запроса: {ex.Message}";
        }

        bool success = !result.StartsWith("❌");
        await FollowupAsync(
            embed: EmbedHandler.Simple($"pgAPI/{action}", result, success ? Color.DarkBlue : Color.DarkRed, "sbln x lois.media"),
            ephemeral: true);
    }
}
