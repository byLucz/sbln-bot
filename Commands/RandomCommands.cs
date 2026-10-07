using Discord;
using Discord.Commands;
using sblngavnav6.Common;
using static sblngavnav6.Common.CommonUtils.Chat;
using sblngavnav6.Data;
using static sblngavnav6.Data.DataRoots;
using System.Runtime.InteropServices;
using DiscordTelegramFrontier;

namespace sblngavnav6.Commands;

public class RandomCommands : ModuleBase<SocketCommandContext>
{
    private static readonly TimeSpan PastaTtl = TimeSpan.FromMinutes(10);
    private static readonly SemaphoreSlim PastaGate = new(1, 1);

    private const int PageSize = 100;

    private static List<PastaEntry> _pasta;
    private static DateTimeOffset _pastaLoadedAt;
    private static ulong _pastaNewestId;

    [Frontier]
    [Command("ролл")]
    public async Task Roll(int min, int max)
    {
        if (min > max)
            (min, max) = (max, min);

        await ReplyAsync($"Твое число - {CommonUtils.RandomNumber(min, max)}");
    }

    [FrontierAsImage]
    [Command("выбери")]
    public async Task ChooseAsync([Remainder] string options)
    {
        var items = options.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                           .Where(item => !string.IsNullOrWhiteSpace(item))
                           .ToList();

        if (items.Count == 0)
        {
            await ReplyAsync("нет вариантов для выбора чел");
            return;
        }

        const string footer = "sbln выбератор🤔";

        (string Description, Color Color)[] thinking =
        [
            ("<a:NERDALERT:1275220081390911579> Дай подумать...", Color.DarkBlue),
            ("<:aga:1254820158669717565>  Хм, что же выбрать еп...", Color.Orange),
            ("<:agerge:1275215945769685044>  Надо выбрать что-то вайбовое...", Color.Green)
        ];

        var frames = thinking
            .Select(frame => EmbedHandler.Build(new EmbedSpec
            {
                Description = frame.Description,
                Color = frame.Color,
                Footer = footer
            }))
            .ToList();

        frames.Add(EmbedHandler.Build(new EmbedSpec
        {
            Description = $"**Я выбираю:** `{items.RandomList()}`",
            Color = Color.Gold,
            Footer = footer
        }));

        await AnimateAsync(Context.Channel, frames, TimeSpan.FromSeconds(2));
    }

    [Frontier]
    [Command("гэй")]
    public async Task GayCommand([Optional] IGuildUser user)
    {
        var target = (IUser)user ?? Context.User;

        if (target is null)
        {
            await ReplyAsync("не понял кого проверять, укажи кентика");
            return;
        }

        var percentage = CommonUtils.RandomNumber(0, 101);
        var pronoun = target.Id == Context.User?.Id ? "Ты" : target.Id == Context.Client.CurrentUser.Id ? "Я" : "Он";
        var verdict = percentage < 33 ? "гетеро" : percentage < 66 ? "биби" : "гэй";

        await ReplyAsync($"**{target.Mention}** уровень гейства - **{percentage}%**. \n{pronoun} **{verdict}**! ");
    }

    [Frontier]
    [Command("паста")]
    public async Task RandomMessageAsync()
    {
        var pasta = await LoadPastaAsync(Context.Client.GetChannel(858713660352233473) as ITextChannel);

        if (pasta is not { Count: > 0 })
        {
            await ReplyAsync("В этом канале нет доступных сообщений");
            return;
        }

        var entry = pasta.RandomList();

        await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
        {
            AuthorName = entry.Author,
            AuthorIconUrl = entry.AvatarUrl,
            Description = entry.Content,
            Color = Color.Red,
            Footer = "sbln паста-карбонара🍝"
        }));
    }

    private static async Task<List<PastaEntry>> LoadPastaAsync(ITextChannel channel)
    {
        if (channel is null)
            return null;

        if (_pasta is { Count: > 0 } && DateTimeOffset.UtcNow - _pastaLoadedAt < PastaTtl)
            return _pasta;

        await PastaGate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_pasta is { Count: > 0 } && DateTimeOffset.UtcNow - _pastaLoadedAt < PastaTtl)
                return _pasta;

            if (_pasta is { Count: > 0 })
                await TopUpAsync(channel).ConfigureAwait(false);
            else
                await CrawlAsync(channel).ConfigureAwait(false);

            _pastaLoadedAt = DateTimeOffset.UtcNow;

            return _pasta;
        }
        finally
        {
            PastaGate.Release();
        }
    }

    private static async Task CrawlAsync(ITextChannel channel)
    {
        var collected = new List<PastaEntry>();
        ulong? before = null;

        while (true)
        {
            var batch = (before is null
                ? await channel.GetMessagesAsync(PageSize).FlattenAsync().ConfigureAwait(false)
                : await channel.GetMessagesAsync(before.Value, Direction.Before, PageSize).FlattenAsync().ConfigureAwait(false))
                .ToList();

            if (batch.Count == 0)
                break;

            before = batch.Min(message => message.Id);
            _pastaNewestId = Math.Max(_pastaNewestId, batch.Max(message => message.Id));

            collected.AddRange(Keep(batch));

            if (batch.Count < PageSize)
                break;
        }

        _pasta = collected;
    }

    private static async Task TopUpAsync(ITextChannel channel)
    {
        while (true)
        {
            var batch = (await channel.GetMessagesAsync(_pastaNewestId, Direction.After, PageSize).FlattenAsync().ConfigureAwait(false)).ToList();

            if (batch.Count == 0)
                break;

            _pastaNewestId = batch.Max(message => message.Id);
            _pasta.AddRange(Keep(batch));

            if (batch.Count < PageSize)
                break;
        }
    }

    private static IEnumerable<PastaEntry> Keep(IEnumerable<IMessage> batch) => batch
        .Where(message => !message.Author.IsBot && !string.IsNullOrWhiteSpace(message.Content))
        .Select(message => new PastaEntry(message.Author.Username, Avatar(message.Author), message.Content));

    [FrontierAsImage]
    [Command("волк")]
    public Task WolfMeme() => SendMemeAsync("wolfs", null);

    [FrontierAsImage]
    [Command("8 яиц")]
    [Alias("?")]
    public Task EightEggs([Remainder] string args = null) => SendMemeAsync("quotes", null);

    [FrontierAsImage]
    [Command("погладить")]
    public Task Pat([Remainder] string input) => SendMemeAsync("pat", $"{Who} погладил {input}💕");

    [FrontierAsImage]
    [Command("чмокнуть")]
    public Task Kiss([Remainder] string input) => SendMemeAsync("kiss", $"{Who} чмокнул {input}💕");

    [FrontierAsImage]
    [Command("обнять")]
    public Task Hug([Remainder] string input) => SendMemeAsync("hug", $"{Who} обнял {input}💕");

    [FrontierAsImage]
    [Command("ф")]
    public Task F([Remainder] string input) =>
        SendMemeAsync("pressf", $"{Who} дает респект {input} <:sadge:853604643456024576>");

    [FrontierAsImage]
    [Command("кусь")]
    public Task Kus([Remainder] string input) => SendMemeAsync("bite", $"{Who} куснул {input}💕");

    [FrontierAsImage]
    [Command("бухнуть")]
    public Task Buhat([Remainder] string input) =>
        SendMemeAsync("drunk", $"{Who} хочет бухнуть с {input} \U0001f974");

    [FrontierAsImage]
    [Command("заткнуть")]
    [Alias("завали ебало")]
    public Task Zavali([Remainder] string input) => SendMemeAsync("stfu", $"{Who} затыкает {input} 🤐");

    private string Who => Context.User?.Mention ?? "кто-то";

    private async Task SendMemeAsync(string category, string title)
    {
        var url = await DataBase.GetRandomMeme(category);

        if (string.IsNullOrWhiteSpace(url))
        {
            await ReplyAsync("Мемов пока нет =(");
            return;
        }

        await ReplyAsync(embed: await EmbedHandler.CreateFImgEmbed(title, url));
    }
}
