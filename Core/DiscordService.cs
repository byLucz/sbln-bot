using Discord;
using Discord.WebSocket;
using DiscordTelegramFrontier;
using Microsoft.Extensions.DependencyInjection;
using sblngavnav6.Audio;
using sblngavnav6.Data;
using sblngavnav6.PPM;
using sblngavnav6.Services;
using sblngavnav6.TwitchService;
using System.Runtime.InteropServices;

namespace sblngavnav6.Core
{
    public sealed class DiscordService
    {
        private readonly object _readyLock = new();
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string _readyFile = Environment.GetEnvironmentVariable("SBLN_READY_FILE");
        private ServiceProvider _services;
        private DiscordSocketClient _client;
        private CommandHandler _commandHandler;
        private InteractionHandler _interHandler;
        private AudioSevenService _audioService;
        private StreamMonoService _streams;
        private PpmService _ppm;
        private FrontierService _frontier;
        private WelcomeService _welcome;
        private bool _started;
        private int _running;

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _running, 1) != 0)
                throw new InvalidOperationException("DiscordService уже запущен");

            using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            void Cancel()
            {
                try { stopping.Cancel(); }
                catch (ObjectDisposedException) { }
                catch (AggregateException ex) { Console.Error.WriteLine($"Ошибка отмены фоновых задач: {ex}"); }
            }
            void OnCancelKeyPress(object sender, ConsoleCancelEventArgs args)
            {
                args.Cancel = true;
                Cancel();
            }
            void OnProcessExit(object sender, EventArgs args) => Cancel();

            using var sigterm = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                Cancel();
            });
            Console.CancelKeyPress += OnCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            try
            {
                SetReady(false);
                foreach (var path in new[] { Global.Vars.Cfg.messagesFilePath, Global.Vars.Cfg.booksJsonPath })
                    if (!string.IsNullOrWhiteSpace(path))
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

                if (!DataBase.CanConnect())
                    throw new InvalidOperationException("Старт без БД невозможен");

                stopping.Token.ThrowIfCancellationRequested();
                _services = new ServiceCollection().AddBotServices().BuildServiceProvider();
                _client = _services.GetRequiredService<DiscordSocketClient>();
                _client.Log += LogAsync;
                _client.Ready += OnReadyAsync;
                _client.Disconnected += OnDisconnectedAsync;

                _commandHandler = _services.GetRequiredService<CommandHandler>();
                _interHandler = _services.GetRequiredService<InteractionHandler>();
                _audioService = _services.GetRequiredService<AudioSevenService>();
                _welcome = _services.GetRequiredService<WelcomeService>();
                _ppm = _services.GetRequiredService<PpmService>();
                _frontier = _services.GetRequiredService<FrontierService>();

                await _commandHandler.InitializeAsync();
                await _interHandler.InitializeAsync();
                if (Global.Vars.Cfg.streamsEnabled)
                {
                    DataBase.DownloadStreamers();
                    _streams = _services.GetRequiredService<StreamMonoService>();
                }

                stopping.Token.ThrowIfCancellationRequested();
                await _client.LoginAsync(TokenType.Bot, Global.Vars.Cfg.token);
                await _client.StartAsync();
                await _ready.Task.WaitAsync(TimeSpan.FromSeconds(90), stopping.Token);

                try { await DataBase.ApplyLastStatusAsync(_client); }
                catch (Exception ex) { await LoggingService.LogErrorAsync("EXSRV", "Не удалось восстановить статус", ex); }

                stopping.Token.ThrowIfCancellationRequested();
                if (_streams != null)
                {
                    try { await _streams.CreateStreamMonoAsync(stopping.Token); }
                    catch (OperationCanceledException) when (stopping.IsCancellationRequested) { throw; }
                    catch (Exception ex) { await LoggingService.LogErrorAsync("EXSRV", "Не удалось запустить Twitch-монитор", ex); }
                }

                _audioService.StartCleanup(stopping.Token);
                await _ppm.StartSweeperAsync(stopping.Token);
                await _frontier.StartAsync();

                lock (_readyLock)
                {
                    _started = true;
                    SetReady(_client.ConnectionState == ConnectionState.Connected);
                }
                await Task.Delay(Timeout.Infinite, stopping.Token);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                await LoggingService.LogErrorAsync("EXSRV", "Ошибка запуска или работы бота", ex);
            }
            finally
            {
                try
                {
                    Cancel();
                    lock (_readyLock)
                    {
                        _started = false;
                        SetReady(false);
                    }
                    await ShutdownAsync();
                }
                finally
                {
                    Console.CancelKeyPress -= OnCancelKeyPress;
                    AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
                }
            }
        }

        private async Task ShutdownAsync()
        {
            try
            {
                await ShutdownStepAsync("Лог завершения", () => LoggingService.LogInformationAsync("EXSRV", "Завершение работы..."));

                if (_client != null)
                {
                    _client.Ready -= OnReadyAsync;
                    _client.Disconnected -= OnDisconnectedAsync;
                }

                foreach (var (name, stop) in BuildShutdownSequence())
                    await ShutdownStepAsync(name, stop);

                if (_client != null)
                    _client.Log -= LogAsync;
            }
            finally
            {
                if (_services != null)
                    await ShutdownStepAsync("Контейнер сервисов", () => _services.DisposeAsync().AsTask());
            }
        }

        private IEnumerable<(string Name, Func<Task> Stop)> BuildShutdownSequence()
        {
            if (_frontier != null)
                yield return ("Telegram", _frontier.StopAsync);

            if (_interHandler != null)
                yield return ("Interactions", _interHandler.StopAsync);

            if (_commandHandler != null)
                yield return ("CommandHandler", _commandHandler.StopAsync);

            if (_streams != null)
                yield return ("Twitch", _streams.StopAsync);

            if (_welcome != null)
                yield return ("Welcome", _welcome.StopAsync);

            if (_ppm != null)
                yield return ("PPM", _ppm.StopAsync);

            if (_audioService != null)
            {
                yield return ("AudioSeven cleanup", _audioService.StopCleanupAsync);

                foreach (var guildId in _audioService.GetActiveGuildIds().ToArray())
                {
                    var id = guildId;
                    yield return ($"AudioSeven {id}", () => _audioService.ForceLeaveAsync(id));
                }
            }

            if (_client != null)
            {
                yield return ("Discord Stop", _client.StopAsync);
                yield return ("Discord Logout", _client.LogoutAsync);
            }
        }

        private static async Task ShutdownStepAsync(string name, Func<Task> stop)
        {
            try { await stop(); }
            catch (Exception ex) { await LoggingService.LogErrorAsync("EXSRV", $"Ошибка завершения: {name}", ex); }
        }

        private Task OnReadyAsync()
        {
            lock (_readyLock)
            {
                _ready.TrySetResult();
                if (_started) SetReady(true);
            }
            return Task.CompletedTask;
        }

        private Task OnDisconnectedAsync(Exception exception)
        {
            lock (_readyLock) SetReady(false);
            return Task.CompletedTask;
        }

        private void SetReady(bool value)
        {
            if (string.IsNullOrWhiteSpace(_readyFile)) return;
            try
            {
                if (value) File.WriteAllText(_readyFile, "ready");
                else File.Delete(_readyFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Не удалось обновить readiness-файл: {ex.Message}");
            }
        }

        private static Task LogAsync(LogMessage log)
            => log.Exception?.StackTrace?.Contains("Victoria.LavaNode") == true
                ? Task.CompletedTask
                : LoggingService.LogDiscordAsync(log);
    }
}
