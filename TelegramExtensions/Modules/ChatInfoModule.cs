using System.Globalization;
using System.Net;
using System.Text;
using DiscordTelegramFrontier;
using sblngavnav6.TelegramExtensions.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace sblngavnav6.TelegramExtensions.Modules;

public sealed class ChatInfoModule : IFrontierModule
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public async Task<bool> HandleAsync(FrontierUpdateContext context)
    {
        if (context.AddressedToAnotherBot) return false;
        var message = context.Update.Message ?? context.Update.ChannelPost;
        if (message is null) return false;
        if (context.Command is not ("информация" or "инфа") &&
            !string.Equals(message.Text?.Trim(), "инфа", StringComparison.OrdinalIgnoreCase)) return false;

        if (message.Chat.Type == ChatType.Private)
        {
            await context.ReplyAsync("напиши инфа в группе или канале, тут смотреть нечего 😋");
            return true;
        }

        ChatFullInfo chat;
        try
        {
            chat = await context.Bot.GetChat(message.Chat.Id, context.CancellationToken);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode is 400 or 403)
        {
            await context.ReplyAsync("не могу посмотреть инфу, проверь мой доступ к чату 😕");
            return true;
        }

        int? memberCount = null;
        try
        {
            memberCount = await context.Bot.GetChatMemberCount(chat.Id, context.CancellationToken);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode is 400 or 403) { }

        context.Items.TryGetValue(ChatStatisticsMiddleware.StatisticsKey, out var statistics);
        var username = chat.Username ?? chat.ActiveUsernames?.FirstOrDefault();
        var keyboard = string.IsNullOrWhiteSpace(username) ? null : new InlineKeyboardMarkup(
            InlineKeyboardButton.WithUrl(chat.Type == ChatType.Channel ? "🔗 Открыть канал" : "🔗 Открыть группу",
                $"https://t.me/{Uri.EscapeDataString(username)}"));
        await context.ReplyAsync(CreateText(chat, memberCount, statistics as ChatStatistics), ParseMode.Html, keyboard);
        return true;
    }

    private static string CreateText(ChatFullInfo chat, int? memberCount, ChatStatistics? statistics)
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
            .AppendLine($"Создание: {statistics?.CreatedAtUtc?.ToString("dd.MM.yyyy HH:mm 'UTC'", Russian) ?? "нет данных"}")
            .Append(chat.Type == ChatType.Channel ? "Подписчиков: " : "Участников: ")
            .Append("<b>").Append(memberCount?.ToString("N0", Russian) ?? "нет данных").AppendLine("</b>")
            .Append("ID: <code>").Append(chat.Id.ToString(CultureInfo.InvariantCulture)).AppendLine("</code>")
            .AppendLine()
            .AppendLine("💬 <b>Активность</b>");
        if (statistics is null)
        {
            text.AppendLine("Счётчик пока недоступен 😕");
        }
        else
        {
            text.Append("Сообщений учтено: <b>").Append(statistics.ReceivedMessages.ToString("N0", Russian)).AppendLine("</b>")
                .AppendLine($"С {statistics.ObservedSinceUtc.ToString("dd.MM.yyyy HH:mm", Russian)} UTC")
                .AppendLine("<i>Считаю только то, что получаю, включая служебные. Удалённые не вычитаю.</i>");
        }

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
