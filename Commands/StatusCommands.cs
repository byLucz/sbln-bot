using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using sblngavnav6.Common;
using sblngavnav6.Core;
using sblngavnav6.Data;
using sblngavnav6.Services;
using static sblngavnav6.Data.DataRoots;

namespace sblngavnav6.Commands;

public static class StatusPanel
{
    private const string Footer = "sbln статус🎫";
    private const string Custom = "Custom";
    private const string None = "None";
    private const string Streaming = nameof(ActivityType.Streaming);
    public const int MaxText = 32;

    private static readonly SemaphoreSlim ApplyGate = new(1, 1);
    private static long _applyTicket;

    private static readonly (string Key, UserStatus Status, string Label)[] Statuses =
    [
        ("онлайн", UserStatus.Online, "🟢 В сети"),
        ("спит", UserStatus.Idle, "🌙 Неактивен"),
        ("днд", UserStatus.DoNotDisturb, "⛔ Не беспокоить"),
        ("инвиз", UserStatus.Invisible, "👻 Невидимка")
    ];

    private static readonly (string Key, string Label)[] Types =
    [
        (nameof(ActivityType.Playing), "🎮 Играет в"),
        (nameof(ActivityType.Listening), "🎧 Слушает"),
        (nameof(ActivityType.Watching), "📺 Смотрит"),
        (Streaming, "📡 Стримит"),
        (nameof(ActivityType.Competing), "🏆 Соревнуется в"),
        (Custom, "💬 Свой статус"),
        (None, "🚫 Без активности")
    ];

    public static BotStatus Normalize(BotStatus status) => status with
    {
        Pos = Statuses.Any(s => s.Key == status.Pos) ? status.Pos : Statuses[0].Key,
        Type = Types.Any(t => t.Key == status.Type) ? status.Type : Types[0].Key
    };

    public static async Task ApplyAsync(DiscordSocketClient client, BotStatus status)
    {
        status = Normalize(status);

        var userStatus = Statuses.First(s => s.Key == status.Pos).Status;
        if (client.Status != userStatus)
            await client.SetStatusAsync(userStatus);

        var hidden = status.Type == None || string.IsNullOrWhiteSpace(status.Text);
        var link = status.Type == Streaming && !string.IsNullOrWhiteSpace(status.Link) ? status.Link : null;
        var current = client.Activity;

        var unchanged = hidden ? current is null
            : status.Type == Custom ? current is CustomStatusGame custom && custom.State == status.Text
            : current is not null && current is not CustomStatusGame
              && current.Type == (link is null ? Enum.Parse<ActivityType>(status.Type) : ActivityType.Streaming)
              && current.Name == status.Text
              && (current as StreamingGame)?.Url == link;

        if (unchanged)
            return;

        if (hidden)
            await client.SetGameAsync(null);
        else if (status.Type == Custom)
            await client.SetCustomStatusAsync(status.Text);
        else
            await client.SetGameAsync(status.Text, link, Enum.Parse<ActivityType>(status.Type));
    }

    public static async Task<string> SaveAsync(BotStatus status, bool newRecord)
    {
        status = Normalize(status);
        await (newRecord ? DataBase.AddStatus(status) : DataBase.UpdateLastStatus(status));

        return status.Type == None ? "✅ Активность убрана"
            : string.IsNullOrWhiteSpace(status.Text) ? "⚠️ Текст не задан, активность не видна, жми ✏️"
            : status.Type == Streaming && !IsStreamUrl(status.Link) ? "⚠️ Для стрима нужна ссылка на twitch.tv или youtube.com, иначе Discord покажет «Играет в»"
            : "✅ Сохранено";
    }

    public static async Task ApplyLatestAsync(DiscordSocketClient client)
    {
        var ticket = Interlocked.Increment(ref _applyTicket);
        await ApplyGate.WaitAsync();
        try
        {
            if (ticket != Interlocked.Read(ref _applyTicket))
                return;

            await ApplyAsync(client, await DataBase.GetLastStatus());
        }
        catch (Exception ex)
        {
            await LoggingService.LogErrorAsync("STATS", "Не удалось применить статус бота", ex);
        }
        finally
        {
            ApplyGate.Release();
        }
    }

    public static (Embed Embed, MessageComponent Components) Build(BotStatus status, string note = null)
    {
        status = Normalize(status);
        var type = Types.First(t => t.Key == status.Type);

        var preview = status.Type == None || string.IsNullOrWhiteSpace(status.Text) ? "без активности"
            : status.Type == Custom ? status.Text
            : $"{type.Label[(type.Label.IndexOf(' ') + 1)..]} **{status.Text}**";

        var fields = new List<EmbedFieldSpec>
        {
            new("Статус", Statuses.First(s => s.Key == status.Pos).Label, true),
            new("Активность", type.Label, true),
            new("Как видно в профиле", preview)
        };

        if (status.Type == Streaming)
            fields.Add(new EmbedFieldSpec("Ссылка", string.IsNullOrWhiteSpace(status.Link) ? "`не задана`" : status.Link));

        var embed = EmbedHandler.Build(new EmbedSpec
        {
            Title = "🎫 Статус бота",
            Description = note,
            Color = Color.Green,
            Fields = fields,
            Footer = Footer
        });

        var components = new ComponentBuilder()
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("stsel:pos")
                .WithPlaceholder("Статус")
                .WithOptions(Statuses.Select(s => new SelectMenuOptionBuilder(s.Label, s.Key, isDefault: s.Key == status.Pos)).ToList()), row: 0)
            .WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId("stsel:type")
                .WithPlaceholder("Активность")
                .WithOptions(Types.Select(t => new SelectMenuOptionBuilder(t.Label, t.Key, isDefault: t.Key == status.Type)).ToList()), row: 1)
            .WithButton(status.Type == Streaming ? "✏️ Текст и ссылка" : "✏️ Текст", "stedit", ButtonStyle.Primary, row: 2)
            .Build();

        return (embed, components);
    }

    private static bool IsStreamUrl(string link) =>
        Uri.TryCreate(link?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Host.EndsWith("twitch.tv", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase));
}

public class StatusCommands : ModuleBase<SocketCommandContext>
{
    [Command("ст")]
    [Alias("статус")]
    [Discord.Commands.RequireOwner(ErrorMessage = "недостаточно прав")]
    public async Task StatusPanelAsync()
    {
        var (embed, components) = StatusPanel.Build(await DataBase.GetLastStatus());
        await ReplyAsync(embed: embed, components: components);
    }
}

public class StatusModal : IModal
{
    public string Title => "Статус бота";

    [InputLabel("Текст активности")]
    [ModalTextInput("text", TextInputStyle.Short, "что показать в профиле", maxLength: StatusPanel.MaxText)]
    [RequiredInput(false)]
    public string Text { get; set; }

    [InputLabel("Ссылка, только для стрима")]
    [ModalTextInput("link", TextInputStyle.Short, "https://twitch.tv/...", maxLength: 200)]
    [RequiredInput(false)]
    public string Link { get; set; }
}

public class StatusInteractions : InteractionModuleBase<SocketInteractionContext>
{
    [RequireOwnerInteraction]
    [ComponentInteraction("stsel:*")]
    public async Task Select(string field, string[] values)
    {
        var current = await DataBase.GetLastStatus();
        var value = values is { Length: > 0 } ? values[0] : null;

        var next = field switch
        {
            "pos" => current with { Pos = value },
            "type" => current with { Type = value },
            _ => current
        };

        await UpdateAsync((SocketMessageComponent)Context.Interaction, next, newRecord: false);
    }

    [RequireOwnerInteraction]
    [ComponentInteraction("stedit")]
    public async Task Edit()
    {
        var current = await DataBase.GetLastStatus();
        await Context.Interaction.RespondWithModalAsync("stmodal", new StatusModal
        {
            Text = string.IsNullOrWhiteSpace(current.Text) ? null : CommonUtils.Text.Truncate(current.Text, StatusPanel.MaxText),
            Link = string.IsNullOrWhiteSpace(current.Link) ? null : current.Link
        });
    }

    [RequireOwnerInteraction]
    [ModalInteraction("stmodal")]
    public async Task Save(StatusModal modal)
    {
        var current = await DataBase.GetLastStatus();
        var next = current with { Text = modal.Text?.Trim() ?? "", Link = modal.Link?.Trim() ?? "" };
        await UpdateAsync((SocketModal)Context.Interaction, next, newRecord: next.Text != current.Text || next.Link != current.Link);
    }

    private async Task UpdateAsync(IDiscordInteraction interaction, BotStatus next, bool newRecord)
    {
        var note = await StatusPanel.SaveAsync(next, newRecord);
        var (embed, components) = StatusPanel.Build(next, note);

        Action<MessageProperties> update = message =>
        {
            message.Embed = embed;
            message.Components = components;
        };

        if (interaction is SocketModal modal)
            await modal.UpdateAsync(update);
        else
            await ((SocketMessageComponent)interaction).UpdateAsync(update);

        await StatusPanel.ApplyLatestAsync(Context.Client);
    }
}
