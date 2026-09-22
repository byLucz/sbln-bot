using System.Globalization;
using System.Net;
using System.Text;
using sblngavnav6.TelegramExtensions.Core;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace sblngavnav6.TelegramExtensions.Modules;

public sealed class ChatInfoModule : TelegramModuleBase
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    [Command("инфа")]
    [Alias("информация")]
    [RequireGroup]
    public async Task InfoAsync()
    {
        ChatFullInfo chat;
        try
        {
            chat = await Context.Bot.GetChat(Context.Chat.Id, Context.CancellationToken);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode is 400 or 403)
        {
            await ReplyAsync("не могу посмотреть инфу, проверь мой доступ к чату 😕");
            return;
        }

        int? memberCount = null;
        try
        {
            memberCount = await Context.Bot.GetChatMemberCount(chat.Id, Context.CancellationToken);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode is 400 or 403) { }

        var username = chat.Username ?? chat.ActiveUsernames?.FirstOrDefault();
        var keyboard = string.IsNullOrWhiteSpace(username) ? null : new InlineKeyboardMarkup(
            InlineKeyboardButton.WithUrl(chat.Type == ChatType.Channel ? "🔗 Открыть канал" : "🔗 Открыть группу",
                $"https://t.me/{Uri.EscapeDataString(username)}"));
        await ReplyAsync(CreateText(chat, memberCount), ParseMode.Html, keyboard);
    }

    private static string CreateText(ChatFullInfo chat, int? memberCount)
    {
        var type = chat.Type switch
        {
            ChatType.Channel => "канал",
            ChatType.Supergroup when chat.IsForum => "группа с темами",
            _ => "группа"
        };
        var text = new StringBuilder()
            .Append("ℹ️ <b>").Append(WebUtility.HtmlEncode(chat.Title ?? "Без названия")).AppendLine("</b>");
        if (!string.IsNullOrWhiteSpace(chat.Description))
            text.AppendLine().AppendLine(WebUtility.HtmlEncode(chat.Description));

        text.AppendLine()
            .AppendLine("📅 <b>Основное</b>")
            .AppendLine($"Тип: {type}")
            .Append(chat.Type == ChatType.Channel ? "Подписчиков: " : "Участников: ")
            .Append("<b>").Append(memberCount?.ToString("N0", Russian) ?? "нет данных").AppendLine("</b>")
            .Append("ID: <code>").Append(chat.Id.ToString(CultureInfo.InvariantCulture)).AppendLine("</code>");

        text.AppendLine()
            .AppendLine("🛡️ <b>Прочее</b>")
            .AppendLine($"Защита контента: {(chat.HasProtectedContent ? "вкл" : "выкл")}")
            .AppendLine($"Автоудаление: {Duration(chat.MessageAutoDeleteTime)}");
        if (chat.Type == ChatType.Supergroup)
        {
            text.AppendLine($"Медленный режим: {Duration(chat.SlowModeDelay)}");
            if (chat.JoinByRequest) text.AppendLine("Вступление: по заявке");
        }
        if (chat.LinkedChatId is { } linkedChatId)
            text.Append(chat.Type == ChatType.Channel ? "Обсуждения: " : "Связанный канал: ")
                .Append("<code>").Append(linkedChatId.ToString(CultureInfo.InvariantCulture)).AppendLine("</code>");
        return text.AppendLine().Append("<i>sbln инфа🔭</i>").ToString();
    }

    private static string Duration(int? seconds)
        => seconds switch
        {
            null or <= 0 => "выкл",
            _ when seconds % 86400 == 0 => $"{seconds / 86400} дн.",
            _ when seconds % 3600 == 0 => $"{seconds / 3600} ч.",
            _ when seconds % 60 == 0 => $"{seconds / 60} мин.",
            _ => $"{seconds} сек."
        };
}
