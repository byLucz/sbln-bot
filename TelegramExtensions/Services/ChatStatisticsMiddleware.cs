using System.Diagnostics;
using System.Text.Json;
using DiscordTelegramFrontier;
using Telegram.Bot.Types.Enums;

namespace sblngavnav6.TelegramExtensions.Services;

public sealed class ChatStatisticsMiddleware(ChatStatisticsStore store) : IFrontierMiddleware
{
    internal static readonly object StatisticsKey = new();

    public async Task<bool> InvokeAsync(FrontierUpdateContext context, FrontierUpdateDelegate next)
    {
        var message = context.Update.Message ?? context.Update.ChannelPost;
        if (message?.Chat.Type is ChatType.Group or ChatType.Supergroup or ChatType.Channel)
        {
            try
            {
                context.Items[StatisticsKey] = await store.ObserveAsync(context.Bot.BotId, message, context.CancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Trace.TraceWarning($"Could not record Telegram chat statistics for {message.Chat.Id}: {ex}");
            }
        }
        return await next(context);
    }
}
