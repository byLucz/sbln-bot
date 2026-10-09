using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using sblngavnav6.Common;
using sblngavnav6.Core;
using sblngavnav6.Services;

namespace sblngavnav6.Commands;

public static class PgApiPanelBuilder
{
    public const string DefaultProject = "pg";
    public const string Source = "pgAPI";

    private const int PageSize = 5;
    private const string Footer = "sbln x lois.media";

    public static readonly (string Label, string Key)[] Actions =
    [
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

    public static string Scope(ulong channelId, ulong userId) => $"pgapi:{channelId}:{userId}";

    public static async Task<string> CheckAsync(PgApiService pgApi)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var health = await pgApi.HealthAsync();
        sw.Stop();

        if (health.Ok)
            return $"✅ ок, {sw.ElapsedMilliseconds} мс";

        return health.Status is null
            ? "❌ недоступен"
            : $"❌ {health.Error}, {sw.ElapsedMilliseconds} мс";
    }

    public static List<Embed> BuildPages(string health)
    {
        var total = Pagination.TotalPages(Actions.Length, PageSize);
        var pages = new List<Embed>(total);

        for (var page = 0; page < total; page++)
            pages.Add(BuildPanelEmbed(health));

        return pages;
    }

    public static readonly string[] Destructive = ["restart", "restart_bot", "stop", "start"];

    public static Action<ComponentBuilder, int> BuildControls() => (builder, page) =>
    {
        foreach (var (label, key) in Actions.Skip(page * PageSize).Take(PageSize))
        {
            var style = key.Contains("stop") ? ButtonStyle.Danger
                      : key.Contains("restart") ? ButtonStyle.Secondary
                      : ButtonStyle.Primary;

            builder.WithButton(label, $"pgapi_action:{key}", style, row: 1);
        }

        builder.WithButton("Health-check", "pgapi_health", ButtonStyle.Primary, row: 2);
    };

    private static Embed BuildPanelEmbed(string health) => EmbedHandler.Build(new EmbedSpec
    {
        AuthorName = "Панель управления pgAPI",
        Description = "[PG Container v0.2](https://github.com/loismedia/pg)",
        Color = Color.Teal,
        Fields = string.IsNullOrWhiteSpace(health)
            ? null
            : [new EmbedFieldSpec("🩺 Health-check", health, true)],
        Footer = Footer
    });
}

[RequirePgOperator]
public class PgApiCommands : ModuleBase<SocketCommandContext>
{
    private readonly PgApiService _pgApi;
    private readonly PaginatorService _pager;

    public PgApiCommands(PgApiService pgApi, PaginatorService pager)
    {
        _pgApi = pgApi;
        _pager = pager;
    }

    [Command("пг")]
    public async Task PgApiPanel()
    {
        var health = await PgApiPanelBuilder.CheckAsync(_pgApi);

        await _pager.SendAsync(
            Context.Channel,
            PgApiPanelBuilder.BuildPages(health),
            decorate: PgApiPanelBuilder.BuildControls(),
            scope: PgApiPanelBuilder.Scope(Context.Channel.Id, Context.User.Id));
    }
}

public class PgApiInteractions : InteractionModuleBase<SocketInteractionContext>
{
    private readonly PgApiService _pgApi;
    private readonly PaginatorService _pager;

    public PgApiInteractions(PgApiService pgApi, PaginatorService pager)
    {
        _pgApi = pgApi;
        _pager = pager;
    }

    [RequirePgOperatorInteraction]
    [ComponentInteraction("pgapi_health")]
    public async Task Health()
    {
        if (Context.Interaction is not SocketMessageComponent component)
            return;

        await DeferAsync();

        var health = await PgApiPanelBuilder.CheckAsync(_pgApi);

        await _pager.ReplaceAsync(
            component.Message,
            PgApiPanelBuilder.BuildPages(health),
            decorate: PgApiPanelBuilder.BuildControls());
    }

    [RequirePgOperatorInteraction]
    [ComponentInteraction("pgapi_action:*")]
    public async Task ExecuteAction(string action)
    {
        await DeferAsync(ephemeral: true);

        if (PgApiPanelBuilder.Destructive.Contains(action))
            await LoggingService.LogWarningAsync("PGAPI", $"{Context.User.Username} ({Context.User.Id}) жмёт {action}");

        PgApiResult result;
        try
        {
            result = action switch
            {
                "projects" => await _pgApi.ProjectsAsync(),
                "status" => await _pgApi.ProjectStatusAsync(PgApiPanelBuilder.DefaultProject),
                "services" => await _pgApi.ServicesAsync(PgApiPanelBuilder.DefaultProject),
                "logs" => await _pgApi.LogsAsync(PgApiPanelBuilder.DefaultProject),
                "logs_bot" => await _pgApi.LogsAsync(PgApiPanelBuilder.DefaultProject, "bot"),
                "restart" => await _pgApi.RestartProjectAsync(PgApiPanelBuilder.DefaultProject),
                "restart_bot" => await _pgApi.RestartServiceAsync(PgApiPanelBuilder.DefaultProject, "bot"),
                "stop" => await _pgApi.StopProjectAsync(PgApiPanelBuilder.DefaultProject),
                "start" => await _pgApi.StartProjectAsync(PgApiPanelBuilder.DefaultProject),
                _ => PgApiResult.Fail("неизвестное действие")
            };
        }
        catch (Exception ex)
        {
            await LoggingService.LogErrorAsync("PGAPI", $"Ошибка действия {action}", ex);
            result = PgApiResult.Fail($"запрос не удался: {ex.Message}");
        }

        await FollowupAsync(embed: result.Ok
            ? EmbedHandler.Simple($"{PgApiPanelBuilder.Source}/{action}", result.ToDisplay(), Color.DarkBlue, "sbln x lois.media")
            : await EmbedHandler.CreateErrorEmbed($"{PgApiPanelBuilder.Source}/{action}", result.ToDisplay()),
            ephemeral: true);
    }
}
