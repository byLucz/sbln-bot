using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using sblngavnav5X.Audio;
using sblngavnav5X.Commands;
using sblngavnav5X.Data;
using sblngavnav5X.GVR;
using sblngavnav5X.Services;
using sblngavnav5X.TwitchService;
using TwitchLib.Communication.Interfaces;
using Victoria;
using CommandService = Discord.Commands.CommandService;

namespace sblngavnav5X.Core
{
    public class DiscordService
    {
        public readonly DiscordSocketClient _client;
        private readonly CommandHandler _commandHandler;
        private readonly InteractionHandler _interHandler;
        private readonly CommandService _commandService;
        private readonly ServiceProvider _services;
        private readonly AudioSevenService _audioService;
        private readonly StreamMonoService _streams;



        public DiscordService()
        {

            _services = ConfigureServices();

            _client = _services.GetRequiredService<DiscordSocketClient>();
            _commandHandler = _services.GetRequiredService<CommandHandler>();
            _audioService = _services.GetRequiredService<AudioSevenService>();
            _streams = _services.GetRequiredService<StreamMonoService>();
            _interHandler = _services.GetRequiredService<InteractionHandler>();

            SubscribeDiscordEvents();
        }

        public async Task InitializeAsync()
        {
            string Token = Utils.token;
            _client.Ready += _streams.CreateStreamMonoAsync;
            await _client.LoginAsync(TokenType.Bot, Token);
            await _client.StartAsync();
            await _commandHandler.InitializeAsync();
            await _interHandler.InitializeAsync();
            var welcomeService = new WelcomeService(_client);
            await DataBase.ApplyLastStatusAsync(_client);
            DataBase.DownloadStreamers();
            var kuma = new KumaReporter(_client, Utils.kumaConn);
            await Task.Delay(-1);

        }

        private void SubscribeDiscordEvents()
        {
            _client.Log += LogAsync;
        }

        private async Task LogAsync(LogMessage logMessage)
        {
            await LoggingService.LogAsync(logMessage.Source, logMessage.Severity, logMessage.Message);
        }

        private ServiceProvider ConfigureServices()
        {
            var config = new DiscordSocketConfig()
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
                    var config = new InteractionServiceConfig
                    {
                        DefaultRunMode = RunMode.Async,
                        LogLevel = LogSeverity.Info, 
                    };
                    return new InteractionService(client, config);
                })
                .AddSingleton<InteractionHandler>()
                .AddSingleton<AudioSevenService>()
                .AddLavaNode(x =>
                {
                    x.SelfDeaf = true;
                })
                .AddSingleton<WeatherHelp>()
                .AddSingleton<StreamMonoService>()
                .AddSingleton<GuildConfig>(x => new GuildConfig())
                .AddSingleton<GovorConfig>(x => new GovorConfig())
                .AddTransient<HttpClient>()

                .BuildServiceProvider();
        }

        internal Task SetGameAsync(string args)
        {
            throw new NotImplementedException();
        }
    }
}
