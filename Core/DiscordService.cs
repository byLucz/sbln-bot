using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using sblngavnav5X.Audio;
using sblngavnav5X.Commands;
using sblngavnav5X.Data;
using sblngavnav5X.GVR;
using sblngavnav5X.Services;
using sblngavnav5X.TwitchService;
using Victoria;
using CommandService = Discord.Commands.CommandService;

namespace sblngavnav5X.Core
{
    public class DiscordService
    {
        public readonly DiscordSocketClient _client;
        private readonly CommandHandler _commandHandler;
        private readonly InteractionHandler _interHandler;
        private readonly ServiceProvider _services;
        private readonly AudioSevenService _audioService;
        private readonly StreamMonoService _streams;
        private readonly WelcomeService _welcomeService;

        public DiscordService()
        {
            _services = ConfigureServices();

            _client = _services.GetRequiredService<DiscordSocketClient>();
            _commandHandler = _services.GetRequiredService<CommandHandler>();
            _audioService = _services.GetRequiredService<AudioSevenService>();
            _streams = _services.GetRequiredService<StreamMonoService>();
            _interHandler = _services.GetRequiredService<InteractionHandler>();
            _welcomeService = _services.GetRequiredService<WelcomeService>();

            SubscribeDiscordEvents();
        }

        public async Task InitializeAsync()
        {
            string token = Utils.token;

            _client.Ready += _streams.CreateStreamMonoAsync;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            await _commandHandler.InitializeAsync();
            await _interHandler.InitializeAsync();

            await DataBase.ApplyLastStatusAsync(_client);
            DataBase.DownloadStreamers();

            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

            try
            {
                await Task.Delay(-1, cts.Token);
            }
            catch (TaskCanceledException) { }

            await ShutdownAsync();
        }

        private async Task ShutdownAsync()
        {
            await LoggingService.LogInformationAsync("EXSRV", "Завершение работы...");

            foreach (var guildId in _audioService.GetActiveGuildIds().ToArray())
            {
                try { await _audioService.ForceLeaveAsync(guildId); } catch { }
            }

            try { await _client.LogoutAsync(); } catch { }
            try { await _client.StopAsync(); } catch { }

            _services.Dispose();
        }

        private void SubscribeDiscordEvents()
        {
            _client.Log += LogAsync;
        }

        private Task LogAsync(LogMessage log)
        {
            return LoggingService.LogDiscordAsync(log);
        }

        private ServiceProvider ConfigureServices()
        {
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.All
            };

            return new ServiceCollection()
                .AddLogging()
                .AddSingleton(new DiscordSocketClient(config))
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
                .AddSingleton<GuildConfig>(_ => new GuildConfig())
                .AddSingleton<GovorConfig>(_ => new GovorConfig())
                .AddLavaNode(x =>
                {
                    x.SelfDeaf = true;
                })
                .AddHttpClient()
                .BuildServiceProvider();
        }
    }
}