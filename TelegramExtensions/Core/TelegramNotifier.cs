using DiscordTelegramFrontier;
using System.Net;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace sblngavnav6.TelegramExtensions.Core;

public sealed class TelegramNotifier(FrontierService frontier)
{
    public bool Available => frontier.Bot is not null;

    public static string Html(string? text) => WebUtility.HtmlEncode(text ?? "");

    public async Task SendHtmlAsync(string chat, int topic, string html, CancellationToken cancellationToken = default)
    {
        var bot = frontier.Bot ?? throw new InvalidOperationException("Telegram-токен не задан");

        await bot.SendMessage(
            long.TryParse(chat, out var chatId) ? chatId : chat,
            html,
            parseMode: ParseMode.Html,
            messageThreadId: topic == 0 ? null : topic,
            linkPreviewOptions: true,
            cancellationToken: cancellationToken);
    }
}
