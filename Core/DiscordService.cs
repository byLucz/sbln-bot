using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using sblngavnav5X.Audio;
using sblngavnav5X.Commands;
using sblngavnav5X.Data;
using sblngavnav5X.GVR;
using sblngavnav5X.PPM;
using sblngavnav5X.Services;
using sblngavnav5X.TwitchService;
using Victoria;
using System.Runtime.InteropServices;
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
        private readonly PpmService _ppm;

        public DiscordService()
        {
            _services = ConfigureServices();

            _client = _services.GetRequiredService<DiscordSocketClient>();
            _commandHandler = _services.GetRequiredService<CommandHandler>();
            _audioService = _services.GetRequiredService<AudioSevenService>();
            _streams = _services.GetRequiredService<StreamMonoService>();
            _interHandler = _services.GetRequiredService<InteractionHandler>();
            _welcomeService = _services.GetRequiredService<WelcomeService>();
            _ppm = _services.GetRequiredService<PpmService>();

            SubscribeDiscordEvents();
        }

        public async Task InitializeAsync()
        {
            string token = Global.Vars.Cfg.token;
            var readyFile = Environment.GetEnvironmentVariable("SBLN_READY_FILE");
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = false;
            void SetReady(bool value)
            {
                if (string.IsNullOrWhiteSpace(readyFile)) return;
                if (value) File.WriteAllText(readyFile, "ready");
                else File.Delete(readyFile);
            }
            SetReady(false);
            _client.Ready += () =>
            {
                ready.TrySetResult();
                if (started) SetReady(true);
                return Task.CompletedTask;
            };
            _client.Disconnected += _ => { SetReady(false); return Task.CompletedTask; };
            foreach (var path in new[] { Global.Vars.Cfg.messagesFilePath, Global.Vars.Cfg.booksJsonPath })
                if (!string.IsNullOrWhiteSpace(path)) Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

            if (!DataBase.CanConnect())
            {
                Environment.ExitCode = 1;
                await LoggingService.LogCriticalAsync("db", "Старт без БД невозможен");
                await _services.DisposeAsync();
                return;
            }

            await _commandHandler.InitializeAsync();
            await _interHandler.InitializeAsync();
            if (Global.Vars.Cfg.streamsEnabled) DataBase.DownloadStreamers();
            if (Global.Vars.Cfg.streamsEnabled)
                _client.Ready += _streams.CreateStreamMonoAsync;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            await ready.Task.WaitAsync(TimeSpan.FromSeconds(90));

            try { await DataBase.ApplyLastStatusAsync(_client); } catch (Exception ex) { await LoggingService.LogErrorAsync("db", "ApplyLastStatus fail", ex); }

            await _ppm.StartSweeperAsync();
            started = true;
            SetReady(_client.ConnectionState == ConnectionState.Connected);

            using var cts = new CancellationTokenSource();
            using var sigterm = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                cts.Cancel();
            });

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { cts.Cancel(); } catch { } };

            try
            {
                await Task.Delay(-1, cts.Token);
            }
            catch (TaskCanceledException) { }

            started = false;
            SetReady(false);
            await ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(20));
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
            try { await _services.DisposeAsync(); } catch { }
        }

        private void SubscribeDiscordEvents()
        {
            _client.Log += LogAsync;
        }

        private Task LogAsync(LogMessage log)
        {
            if (log.Exception?.StackTrace?.Contains("Victoria.LavaNode") == true)
                return Task.CompletedTask;

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
                .AddSingleton<PaginatorService>()
                .AddSingleton<PgApiService>()
                .AddSingleton<PpmServerService>()
                .AddSingleton<PpmService>()
                .AddSingleton<GuildConfig>(_ => new GuildConfig())
                .AddSingleton<GovorConfig>(_ => new GovorConfig())
                .AddLavaNode(x =>
                {
                    x.SelfDeaf = true;
                    x.Hostname = Global.Vars.Cfg.lavaHost;
                    x.Port = Global.Vars.Cfg.lavaPort;
                    x.Authorization = Global.Vars.Cfg.lavaPass;
                })
                .AddHttpClient()
                .BuildServiceProvider();
        }
    }
}
