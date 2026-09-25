using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using sblngavnav6.Audio;
using sblngavnav6.Commands;
using sblngavnav6.Data;
using sblngavnav6.GVR;
using sblngavnav6.PPM;
using sblngavnav6.Services;
using sblngavnav6.TelegramExtensions;
using sblngavnav6.TwitchService;
using Victoria;
using DiscordTelegramFrontier;
using CommandService = Discord.Commands.CommandService;

namespace sblngavnav6.Core
{
    internal static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddBotServices(this IServiceCollection services)
        {
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.All
            };

            return services
                .AddLogging()
                .AddSingleton(_ => new DiscordSocketClient(config))
                .AddSingleton<CommandService>()
                .AddSingleton<CommandHandler>()
                .AddSingleton<InteractionService>(provider =>
                {
                    var client = provider.GetRequiredService<DiscordSocketClient>();
                    var interactionConfig = new InteractionServiceConfig
                    {
                        DefaultRunMode = RunMode.Async,
                        LogLevel = LogSeverity.Info
                    };
                    return new InteractionService(client, interactionConfig);
                })
                .AddSingleton<InteractionHandler>()
                .AddSingleton<AudioSevenService>()
                .AddSingleton<GVRMessagesHandler>()
                .AddSingleton<WeatherHelp>()
                .AddSingleton<StreamMonoService>()
                .AddSingleton<WelcomeService>()
                .AddSingleton<PaginatorService>()
                .AddSingleton<PgApiService>()
                .AddSingleton<PpmServerService>()
                .AddSingleton<PpmService>()
                .AddSingleton<GovorConfig>()
                .AddTelegramExtensions()
                .AddFrontier(o =>
                {
                    o.TelegramToken = Global.Vars.Cfg.telegramToken;
                    o.DefaultGuildId = Global.Vars.Cfg.telegramDefaultGuild;
                    foreach (var (left, right) in Global.Vars.Cfg.IdMap("Telegram:ChatGuild"))
                        o.Chat(left, right);
                    foreach (var (left, right) in Global.Vars.Cfg.IdMap("Telegram:UserLink"))
                        o.User(left, right);
                })
                .AddLavaNode(x =>
                {
                    x.SelfDeaf = true;
                    x.Hostname = Global.Vars.Cfg.lavaHost;
                    x.Port = Global.Vars.Cfg.lavaPort;
                    x.Authorization = Global.Vars.Cfg.lavaPass;
                })
                .AddHttpClient();
        }
    }
}
