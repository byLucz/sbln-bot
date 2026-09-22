using DiscordTelegramFrontier;
using Microsoft.Extensions.DependencyInjection;
using sblngavnav6.TelegramExtensions.Core;
using Telegram.Bot.Types.Enums;

namespace sblngavnav6.TelegramExtensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTelegramExtensions(this IServiceCollection services)
    {
        var modules = typeof(ServiceCollectionExtensions).Assembly.GetExportedTypes()
            .Where(type => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters &&
                typeof(TelegramModuleBase).IsAssignableFrom(type)).ToArray();
        var commands = new CommandCatalog(modules);
        foreach (var module in modules) services.AddTransient(module);
        services.AddSingleton(commands);
        return services.AddFrontierModule<CommandHandler>(UpdateType.Message, UpdateType.ChannelPost);
    }
}
