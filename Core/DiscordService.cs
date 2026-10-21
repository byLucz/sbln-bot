using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using sblngavnav5X.Audio;
using sblngavnav5X.Commands;
using sblngavnav5X.Data;
using sblngavnav5X.GVR;
using sblngavnav5X.Services;
using sblngavnav5X.TwitchService;
using System.Runtime.InteropServices;
using Victoria;
using CommandService = Discord.Commands.CommandService;
using CommandServiceConfig = Discord.Commands.CommandServiceConfig;

namespace sblngavnav5X.Core
{
    public class DiscordService
    {
        public static readonly string ReadyFile = Environment.GetEnvironmentVariable("SBLN_READY_FILE");

        private ServiceProvider _services;
        private DiscordSocketClient _client;
        private AudioSevenService _audioService;

        public async Task InitializeAsync()
        {
            SetReady(false);

            try
            {
                _ = Utils.token;
            }
            catch (TypeInitializationException ex)
            {
                await LoggingService.LogCriticalAsync("EXSRV", $"Конфиг не загружен: {ex.InnerException?.Message}");
                Environment.ExitCode = 1;
                return;
            }

            if (!DataBase.CanConnect())
            {
                await LoggingService.LogCriticalAsync("EXSRV", "Старт без БД невозможен");
                Environment.ExitCode = 1;
                return;
            }

            _services = ConfigureServices();
            _client = _services.GetRequiredService<DiscordSocketClient>();
            _audioService = _services.GetRequiredService<AudioSevenService>();
            var commandHandler = _services.GetRequiredService<CommandHandler>();

            if (Utils.Modules.Welcome)
                _services.GetRequiredService<WelcomeService>();

            if (Utils.Modules.Streams)
            {
                var streams = _services.GetRequiredService<StreamMonoService>();
                _client.Ready += streams.CreateStreamMonoAsync;
            }

            _client.Log += LogAsync;
            _client.Ready += () => { SetReady(true); return Task.CompletedTask; };
            _client.Disconnected += _ => { SetReady(false); return Task.CompletedTask; };

            await commandHandler.InitializeAsync();

            if (Utils.Modules.Streams)
                DataBase.DownloadStreamers();

            using var cts = new CancellationTokenSource();
            void Cancel()
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                Cancel();
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Cancel();
            using var sigterm = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                Cancel();
            });

            await _client.LoginAsync(TokenType.Bot, Utils.token);
            await _client.StartAsync();

            try { await DataBase.ApplyLastStatusAsync(_client); }
            catch (Exception ex) { await LoggingService.LogErrorAsync("EXSRV", "Не удалось восстановить статус", ex); }

            try
            {
                await Task.Delay(-1, cts.Token);
            }
            catch (TaskCanceledException) { }

            await ShutdownAsync();
        }

        private async Task ShutdownAsync()
        {
            SetReady(false);
            await LoggingService.LogInformationAsync("EXSRV", "Завершение работы...");

            foreach (var guildId in _audioService.GetActiveGuildIds().ToArray())
            {
                try { await _audioService.ForceLeaveAsync(guildId); } catch { }
            }

            try { await _client.LogoutAsync(); } catch { }
            try { await _client.StopAsync(); } catch { }
            try { await _services.DisposeAsync(); } catch { }
        }

        private static void SetReady(bool value)
        {
            if (string.IsNullOrWhiteSpace(ReadyFile)) return;
            try
            {
                if (value) File.WriteAllText(ReadyFile, "ready");
                else File.Delete(ReadyFile);
            }
            catch { }
        }

        private Task LogAsync(LogMessage log)
        {
            if (log.Exception?.StackTrace?.Contains("Victoria.LavaNode") == true)
                return Task.CompletedTask;

            return LoggingService.LogDiscordAsync(log);
        }

        private static ServiceProvider ConfigureServices()
        {
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds
                    | GatewayIntents.GuildMembers
                    | GatewayIntents.GuildMessages
                    | GatewayIntents.GuildMessageReactions
                    | GatewayIntents.MessageContent
                    | GatewayIntents.GuildVoiceStates
                    | GatewayIntents.GuildEmojis
                    | GatewayIntents.DirectMessages
            };

            return new ServiceCollection()
                .AddLogging()
                .AddSingleton(new DiscordSocketClient(config))
                .AddSingleton(new CommandService(new CommandServiceConfig
                {
                    DefaultRunMode = Discord.Commands.RunMode.Async,
                    LogLevel = LogSeverity.Info
                }))
                .AddSingleton<CommandHandler>()
                .AddSingleton<AudioSevenService>()
                .AddSingleton<GVRMessagesHandler>()
                .AddSingleton<WeatherHelp>()
                .AddSingleton<StreamMonoService>()
                .AddSingleton<WelcomeService>()
                .AddSingleton<ReactionPager>()
                .AddSingleton<GovorConfig>(_ => new GovorConfig())
                .AddLavaNode(x =>
                {
                    x.Hostname = Utils.lavaHost;
                    x.Port = Utils.lavaPort;
                    x.Authorization = Utils.lavaPass;
                    x.SelfDeaf = true;
                })
                .AddHttpClient()
                .BuildServiceProvider();
        }
    }
}
