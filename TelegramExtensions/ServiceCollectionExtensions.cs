using DiscordTelegramFrontier;
using Microsoft.Extensions.DependencyInjection;
using sblngavnav6.TelegramExtensions.Modules;
using sblngavnav6.TelegramExtensions.Services;
using Telegram.Bot.Types.Enums;

namespace sblngavnav6.TelegramExtensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTelegramExtensions(this IServiceCollection services, string? dataDirectory = null)
        => services.AddSingleton(_ => new ChatStatisticsStore(dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "data")))
            .AddFrontierMiddleware<ChatStatisticsMiddleware>(UpdateType.Message, UpdateType.ChannelPost)
            .AddFrontierModule<ChatInfoModule>(UpdateType.Message, UpdateType.ChannelPost);
}
