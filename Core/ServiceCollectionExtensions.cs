using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordTelegramFrontier;
using Microsoft.Extensions.DependencyInjection;
using sblngavnav6.Audio8;
using sblngavnav6.Commands;
using sblngavnav6.Data;
using sblngavnav6.GVR;
using sblngavnav6.PPM;
using sblngavnav6.Services;
using sblngavnav6.Services.Twitch;
using sblngavnav6.TelegramExtensions;
using sblngavnav6.TelegramExtensions.Core;
using sblngavnav6.TwitchService;
using System.Net.Sockets;
using Telegram.Bot.Exceptions;
using CommandService = Discord.Commands.CommandService;
using CommandServiceConfig = Discord.Commands.CommandServiceConfig;

namespace sblngavnav6.Core
{
    internal static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddBotServices(this IServiceCollection services)
        {
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds
                    | GatewayIntents.GuildMembers
                    | GatewayIntents.GuildMessages
                    | GatewayIntents.MessageContent
                    | GatewayIntents.GuildVoiceStates
                    | GatewayIntents.GuildEmojis
                    | GatewayIntents.GuildPresences
                    | GatewayIntents.DirectMessages
            };

            return services
                .AddLogging()
                .AddSingleton(_ => new DiscordSocketClient(config))
                .AddSingleton(_ => new CommandService(new CommandServiceConfig
                {
                    DefaultRunMode = Discord.Commands.RunMode.Async,
                    LogLevel = LogSeverity.Info
                }))
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
                .AddSingleton<GVRDb>()
                .AddSingleton<GVRMessagesHandler>()
                .AddSingleton<WeatherClient>()
                .AddSingleton<StreamMonoService>()
                .AddSingleton<StreamerRegistry>()
                .AddSingleton<WelcomeService>()
                .AddSingleton<PaginatorService>()
                .AddSingleton<PgApiService>()
                .AddSingleton<PpmServerService>()
                .AddSingleton<PpmPanels>()
                .AddSingleton<PpmService>()
                .AddSingleton<GVRConfig>()
                .AddSingleton<ITelegramHelpSource, TelegramHelpSource>()
                .AddTelegramExtensions()
                .AddFrontier(o =>
                {
                    o.TelegramToken = Global.Vars.Cfg.telegramToken;
                    o.DefaultGuildId = Global.Vars.Cfg.telegramDefaultGuild;
                    o.ErrorHandler = ex => _ = ReportTelegramAsync(ex);
                    foreach (var (left, right) in Global.Vars.Cfg.IdMap("Telegram:ChatGuild"))
                        o.Chat(left, right);
                    foreach (var (left, right) in Global.Vars.Cfg.IdMap("Telegram:UserLink"))
                        o.User(left, right);
                })
                .AddAudio8()
                .AddHttpClient();
        }

        private static Task ReportTelegramAsync(Exception ex)
        {
            if (!IsTransient(ex))
                return LoggingService.LogErrorAsync("DTFTG", "Ошибка Telegram-моста", ex);

            var reason = ex.InnerException is null ? ex.Message : $"{ex.Message}: {ex.GetBaseException().Message}";
            return LoggingService.LogWarningAsync("DTFTG", $"Telegram недоступен, сетевой сбой ({reason})");
        }

        private static bool IsTransient(Exception ex)
        {
            for (var current = ex; current is not null; current = current.InnerException)
            {
                if (current is ApiRequestException)
                    return false;

                if (current is RequestException or HttpRequestException or TimeoutException or IOException or SocketException)
                    return true;
            }

            return false;
        }
    }
}
