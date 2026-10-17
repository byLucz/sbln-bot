using Discord.WebSocket;
using DiscordTelegramFrontier;
using Microsoft.Extensions.DependencyInjection;
using sblngavnav6.TelegramExtensions.Core;
using System.Globalization;
using System.Net;
using System.Text;
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
    public async Task InfoAsync()
    {
        var (target, unknown) = Target();
        if (unknown is not null)
        {
            await ReplyAsync($"не знаю {unknown}, пусть сначала что-нибудь напишет в чат 😕");
            return;
        }

        if (target is null && Context.Chat.Type == ChatType.Private)
            target = Context.User;

        if (target is not null)
        {
            await UserInfoAsync(target);
            return;
        }

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

    private (User? Target, string? Unknown) Target()
    {
        var message = Context.Message;
        var text = message.Text ?? message.Caption ?? "";

        foreach (var entity in (message.Text is not null ? message.Entities : message.CaptionEntities) ?? [])
        {
            if (entity.Type == MessageEntityType.TextMention && entity.User is { } mentioned)
                return (mentioned, null);

            if (entity.Type != MessageEntityType.Mention || entity.Offset + entity.Length > text.Length)
                continue;

            var username = text.Substring(entity.Offset, entity.Length);
            return Context.FindUser(username) is { } known ? (known, null) : (null, username);
        }

        return (message.ReplyToMessage?.From, null);
    }

    private async Task UserInfoAsync(User user)
    {
        ChatFullInfo? profile = null;
        try
        {
            profile = await Context.Bot.GetChat(user.Id, Context.CancellationToken);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode is 400 or 403) { }

        var name = string.Join(" ", new[] { user.FirstName, user.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        var usernames = (profile?.ActiveUsernames ?? (user.Username is null ? [] : [user.Username])).ToList();

        var text = new StringBuilder()
            .Append("👤 <b>").Append(WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(name) ? "Без имени" : name)).AppendLine("</b>");
        if (!string.IsNullOrWhiteSpace(profile?.Bio))
            text.AppendLine().AppendLine(WebUtility.HtmlEncode(profile.Bio));

        text.AppendLine()
            .AppendLine("🪪 <b>Профиль</b>")
            .Append("Юзернейм: ").AppendLine(usernames.Count == 0 ? "нет" : string.Join(", ", usernames.Select(username => "@" + WebUtility.HtmlEncode(username))))
            .Append("ID: <code>").Append(user.Id.ToString(CultureInfo.InvariantCulture)).AppendLine("</code>")
            .Append("Язык: ").AppendLine(string.IsNullOrWhiteSpace(user.LanguageCode) ? "нет данных" : WebUtility.HtmlEncode(user.LanguageCode))
            .Append("Premium: ").AppendLine(user.IsPremium ? "да" : "нет");
        if (profile?.Birthdate is { } birthdate)
            text.Append("День рождения: ").AppendLine(birthdate.Year is { } year
                ? $"{birthdate.Day:00}.{birthdate.Month:00}.{year}"
                : $"{birthdate.Day:00}.{birthdate.Month:00}");

        var linked = Context.Services.GetService<FrontierOptions>()?.UserToDiscord.TryGetValue(user.Id, out var discordId) == true
            ? Context.Services.GetService<DiscordSocketClient>()?.GetUser(discordId) is { } discord
                ? $"@{WebUtility.HtmlEncode(discord.Username)} (<code>{discordId.ToString(CultureInfo.InvariantCulture)}</code>)"
                : $"<code>{discordId.ToString(CultureInfo.InvariantCulture)}</code>"
            : "не привязан";
        text.AppendLine()
            .AppendLine("🔗 <b>Discord</b>")
            .AppendLine(linked);

        await ReplyAsync(text.AppendLine().Append("<i>sbln инфа🔭</i>").ToString(), ParseMode.Html);
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
