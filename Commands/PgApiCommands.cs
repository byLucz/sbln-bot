using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using sblngavnav6.Core;
using sblngavnav6.Common;
using sblngavnav6.Data;
using sblngavnav6.Services;

namespace sblngavnav6.Commands;

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
        var totalPages = Pagination.TotalPages(Actions.Length, pageSize);
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

        builder.AddPager(page, totalPages, "pgapi_page", row: 1);

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

[RequirePgOperator]
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
        var health = await _pgApi.HealthAsync();
        sw.Stop();

        string pingInfo = health.Ok
            ? $"{sw.ElapsedMilliseconds} мс"
            : health.Status is null ? "недоступен" : $"{health.Error} за {sw.ElapsedMilliseconds} мс";

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

    [RequirePgOperatorInteraction]
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

    [RequirePgOperatorInteraction]
    [ComponentInteraction("pgapi_action:*")]
    public async Task ExecuteAction(string action)
    {
        await DeferAsync(ephemeral: true);

        PgApiResult result;
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
                _             => PgApiResult.Fail("Неизвестное действие")
            };
        }
        catch (Exception ex)
        {
            await LoggingService.LogErrorAsync("PGAPI", $"Ошибка действия {action}", ex);
            result = PgApiResult.Fail($"Ошибка запроса: {ex.Message}");
        }

        await FollowupAsync(
            embed: EmbedHandler.Simple(
                $"pgAPI/{action}",
                result.ToDisplay(),
                result.Ok ? Color.DarkBlue : Color.DarkRed,
                "sbln x lois.media"),
            ephemeral: true);
    }
}
