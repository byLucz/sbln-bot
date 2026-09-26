using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Events;
using Lavalink4NET.Extensions;
using Lavalink4NET.InactivityTracking;
using Lavalink4NET.InactivityTracking.Events;
using Lavalink4NET.InactivityTracking.Extensions;
using Lavalink4NET.InactivityTracking.Trackers.Users;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using sblngavnav6.Core;
using sblngavnav6.Data;
using sblngavnav6.Services;

namespace sblngavnav6.Audio8
{
    public sealed class Audio8Runtime : IAsyncDisposable
    {
        private readonly IAudioService _audio;
        private readonly IInactivityTrackingService _inactivity;
        private readonly Audio8Service _service;
        private readonly Audio8Interactions _interactions;
        private readonly Audio8StatsTracker _stats;
        private readonly Audio8Persistence _persistence;

        private CancellationTokenSource _snapshotCts;
        private Task _snapshotTask = Task.CompletedTask;
        private bool _started;
        private bool _disposed;

        internal Audio8Runtime(
            IAudioService audio,
            IInactivityTrackingService inactivity,
            Audio8Service service,
            Audio8Interactions interactions,
            Audio8StatsTracker stats,
            Audio8Persistence persistence)
        {
            _persistence = persistence;
            _audio = audio;
            _inactivity = inactivity;
            _service = service;
            _interactions = interactions;
            _stats = stats;

            _inactivity.PlayerInactive += OnPlayerInactiveAsync;
            _audio.ConnectionReady += OnConnectionReadyAsync;
            _audio.ConnectionClosed += OnConnectionClosedAsync;
        }

        private Task OnConnectionReadyAsync(object sender, ConnectionReadyEventArgs args) =>
            LoggingService.LogInformationAsync(
                Audio8Constants.LogSource,
                $"Lavalink подключен: {Global.Vars.Cfg.lavaHost}:{Global.Vars.Cfg.lavaPort}");

        private Task OnConnectionClosedAsync(object sender, ConnectionClosedEventArgs args)
        {
            var reason = args.Exception?.Message
                ?? args.CloseStatusDescription
                ?? args.CloseStatus?.ToString()
                ?? "причина неизвестна";

            var message =
                $"Lavalink отключен ({Global.Vars.Cfg.lavaHost}:{Global.Vars.Cfg.lavaPort}): {reason}. " +
                $"Переподключение {(args.AllowReconnect ? "будет" : "не планируется")}";

            return args.AllowReconnect
                ? LoggingService.LogWarningAsync(Audio8Constants.LogSource, message, args.Exception)
                : LoggingService.LogCriticalAsync(Audio8Constants.LogSource, message, args.Exception);
        }

        private async Task OnPlayerInactiveAsync(object sender, PlayerInactiveEventArgs args)
        {
            await LoggingService.LogInformationAsync(
                Audio8Constants.LogSource,
                $"Войс опустел, отключаюсь g={args.Player.GuildId}");

            await _service.LeaveAsync(args.Player.GuildId).ConfigureAwait(false);
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
                return;

            _started = true;

            await _audio.StartAsync(cancellationToken).ConfigureAwait(false);

            if (!await WaitForLavalinkAsync(cancellationToken).ConfigureAwait(false))
            {
                await LoggingService.LogCriticalAsync(
                    Audio8Constants.LogSource,
                    $"Lavalink не ответил за {Audio8Constants.LavalinkReadyTimeout.TotalSeconds:0}с " +
                    $"({Global.Vars.Cfg.lavaHost}:{Global.Vars.Cfg.lavaPort}). " +
                    "Музыка не заработает: проверь, что Lavalink запущен, пароль совпадает и порт доступен из контейнера бота.");
            }

            await _inactivity.StartAsync(cancellationToken).ConfigureAwait(false);
            _service.StartCleanup(cancellationToken);

            await RestoreAsync(cancellationToken).ConfigureAwait(false);

            _snapshotCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _snapshotTask = SnapshotLoopAsync(_snapshotCts.Token);

            await LoggingService.LogInformationAsync(Audio8Constants.LogSource, "Audio8 запущен");
        }

        private async Task<bool> WaitForLavalinkAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Audio8Constants.LavalinkReadyTimeout);

            try
            {
                await _audio.WaitForReadyAsync(timeout.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync(Audio8Constants.LogSource, "Ошибка подключения к Lavalink", ex);
                return false;
            }
        }

        private async Task RestoreAsync(CancellationToken cancellationToken)
        {
            var state = await _persistence.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (state.IsEmpty)
                return;

            _service.RestoreGuildSettings(state.Guilds);

            if (state.Guilds.Count > 0)
                await LoggingService.LogInformationAsync(
                    Audio8Constants.LogSource,
                    $"Настройки восстановлены для гильдий: {state.Guilds.Count}");

            if (state.Players.Count == 0)
                return;

            var restored = 0;

            foreach (var snapshot in state.Players)
            {
                try
                {
                    if (await _service.RestoreAsync(snapshot, cancellationToken).ConfigureAwait(false))
                        restored++;
                }
                catch (Exception ex)
                {
                    await LoggingService.LogWarningAsync(
                        Audio8Constants.LogSource,
                        $"Не удалось восстановить плеер g={snapshot.GuildId}: {ex.Message}");
                }
            }

            await LoggingService.LogInformationAsync(
                Audio8Constants.LogSource,
                $"Восстановлено плееров: {restored} из {state.Players.Count}");
        }

        private async Task SnapshotLoopAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(Audio8Constants.SnapshotInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                    await _persistence.SaveAsync(_service.CaptureState(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }

        public async Task StopAsync()
        {
            if (!_started)
                return;

            _started = false;

            if (_snapshotCts is not null)
            {
                await _snapshotCts.CancelAsync().ConfigureAwait(false);
                await _snapshotTask.ConfigureAwait(false);
                _snapshotCts.Dispose();
                _snapshotCts = null;
            }

            try { await _persistence.SaveAsync(_service.CaptureState()).ConfigureAwait(false); }
            catch (Exception ex)
            {
                await LoggingService.LogWarningAsync(Audio8Constants.LogSource, $"Снимок состояния не сохранён: {ex.Message}");
            }

            foreach (var guildId in _service.GetActiveGuildIds())
                await _service.LeaveAsync(guildId).ConfigureAwait(false);

            await _service.StopCleanupAsync().ConfigureAwait(false);

            try { await _inactivity.StopAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                await LoggingService.LogWarningAsync(Audio8Constants.LogSource, $"Остановка трекера простоя: {ex.Message}");
            }

            try { await _audio.StopAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                await LoggingService.LogWarningAsync(Audio8Constants.LogSource, $"Остановка Lavalink: {ex.Message}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;
            _inactivity.PlayerInactive -= OnPlayerInactiveAsync;
            _audio.ConnectionReady -= OnConnectionReadyAsync;
            _audio.ConnectionClosed -= OnConnectionClosedAsync;

            await StopAsync().ConfigureAwait(false);

            _interactions.Dispose();
            _stats.Dispose();

            await _service.DisposeAsync().ConfigureAwait(false);
        }
    }

    public static class Audio8Registration
    {
        public static IServiceCollection AddAudio8(this IServiceCollection services)
        {
            services
                .AddLavalink()
                .ConfigureLavalink(options =>
                {
                    options.BaseAddress = new Uri($"http://{Global.Vars.Cfg.lavaHost}:{Global.Vars.Cfg.lavaPort}");
                    options.Passphrase = Global.Vars.Cfg.lavaPass;
                    options.ReadyTimeout = TimeSpan.FromSeconds(30);
                    options.ResumptionOptions = new(TimeSpan.FromMinutes(1));
                });

            services
                .AddInactivityTracking()
                .ConfigureInactivityTracking(options =>
                {
                    options.DefaultTimeout = Audio8Constants.InactivityTimeout;
                    options.TrackingMode = InactivityTrackingMode.Any;
                    options.InactivityBehavior = PlayerInactivityBehavior.None;
                    options.UseDefaultTrackers = false;
                })
                .AddInactivityTracker<UsersInactivityTracker>()
                .Configure<UsersInactivityTrackerOptions>(options =>
                {
                    options.Timeout = Audio8Constants.InactivityTimeout;
                    options.Threshold = 1;
                    options.ExcludeBots = true;
                });

            return services
                .AddLogging(builder => builder.AddProvider(new Audio8LoggerProvider()))
                .AddSingleton<Audio8MessageStates>()
                .AddSingleton<Audio8Persistence>()
                .AddSingleton<Audio8StatsTracker>()
                .AddSingleton<Audio8TtsComposer>()
                .AddSingleton(provider => new Audio8Service(
                    provider.GetRequiredService<IAudioService>(),
                    provider.GetRequiredService<DiscordSocketClient>(),
                    provider.GetRequiredService<PaginatorService>(),
                    provider.GetRequiredService<Audio8MessageStates>(),
                    provider.GetRequiredService<Audio8StatsTracker>(),
                    provider.GetRequiredService<Audio8Persistence>()))
                .AddSingleton<Audio8SearchService>()
                .AddSingleton<Audio8Interactions>()
                .AddSingleton<Audio8VoteService>()
                .AddSingleton(provider => new Audio8Runtime(
                    provider.GetRequiredService<IAudioService>(),
                    provider.GetRequiredService<IInactivityTrackingService>(),
                    provider.GetRequiredService<Audio8Service>(),
                    provider.GetRequiredService<Audio8Interactions>(),
                    provider.GetRequiredService<Audio8StatsTracker>(),
                    provider.GetRequiredService<Audio8Persistence>()));
        }
    }

    internal sealed class Audio8State
    {
        public List<Audio8GuildSettings> Guilds { get; set; } = [];

        public List<Audio8GuildSnapshot> Players { get; set; } = [];

        public bool IsEmpty => Guilds.Count == 0 && Players.Count == 0;
    }

    internal sealed class Audio8GuildSettings
    {
        public ulong GuildId { get; set; }
        public string SearchPrefix { get; set; } = Audio8Query.YouTubePrefix;
        public List<Audio8RecentPlaylist> RecentPlaylists { get; set; } = [];
    }

    internal sealed class Audio8GuildSnapshot
    {
        public ulong GuildId { get; set; }
        public ulong VoiceChannelId { get; set; }
        public ulong TextChannelId { get; set; }
        public string CurrentTrack { get; set; }
        public long PositionMs { get; set; }
        public List<string> Queue { get; set; } = [];
        public TrackRepeatMode RepeatMode { get; set; }
        public float Volume { get; set; } = 1f;
        public int BassBoostLevel { get; set; } = Audio8Constants.MinBassBoost;
        public string FilterPreset { get; set; } = Audio8Constants.NoFilterPreset;
        public bool Paused { get; set; }
    }

    internal sealed class Audio8Persistence
    {
        private const string FileName = "audio8-state.json";
        private const int MaxStoredTracks = 500;

        private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly string _path = ResolvePath();

        private int? _lastFingerprint;

        public DateTimeOffset? LastSavedAtUtc { get; private set; }

        public int LastSavedGuilds { get; private set; }

        public async Task SaveAsync(Audio8State state, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!ShouldWrite(state))
                    return;

                if (state.IsEmpty)
                {
                    if (File.Exists(_path))
                        File.Delete(_path);

                    LastSavedAtUtc = DateTimeOffset.UtcNow;
                    LastSavedGuilds = 0;
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(_path));

                var temp = _path + ".tmp";
                await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, Json), cancellationToken).ConfigureAwait(false);
                File.Move(temp, _path, overwrite: true);

                LastSavedAtUtc = DateTimeOffset.UtcNow;
                LastSavedGuilds = Math.Max(state.Players.Count, state.Guilds.Count);
                _lastFingerprint = Fingerprint(state);
            }
            catch (Exception ex) when (IsIoFailure(ex))
            {
                await LoggingService.LogWarningAsync(Audio8Constants.LogSource, $"Не удалось сохранить состояние Audio8: {ex.Message}");
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<Audio8State> LoadAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!File.Exists(_path))
                    return new Audio8State();

                var raw = (await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false)).TrimStart();

                if (raw.StartsWith('['))
                {
                    var legacy = JsonSerializer.Deserialize<List<Audio8GuildSnapshot>>(raw, Json) ?? [];
                    return new Audio8State { Players = legacy };
                }

                return JsonSerializer.Deserialize<Audio8State>(raw, Json) ?? new Audio8State();
            }
            catch (Exception ex) when (IsIoFailure(ex) || ex is JsonException)
            {
                await LoggingService.LogWarningAsync(Audio8Constants.LogSource, $"Состояние Audio8 не прочитано: {ex.Message}");
                return new Audio8State();
            }
            finally
            {
                _gate.Release();
            }
        }

        private bool ShouldWrite(Audio8State state)
        {
            var fingerprint = Fingerprint(state);

            if (fingerprint != _lastFingerprint)
                return true;

            var playing = state.Players.Any(player => !player.Paused && player.CurrentTrack is not null);
            if (!playing)
                return false;

            return LastSavedAtUtc is null
                || DateTimeOffset.UtcNow - LastSavedAtUtc.Value >= Audio8Constants.PositionRefreshInterval;
        }

        private static int Fingerprint(Audio8State state)
        {
            var hash = new HashCode();

            foreach (var guild in state.Guilds)
            {
                hash.Add(guild.GuildId);
                hash.Add(guild.SearchPrefix);

                foreach (var recent in guild.RecentPlaylists)
                    hash.Add(recent.Url);
            }

            foreach (var player in state.Players)
            {
                hash.Add(player.GuildId);
                hash.Add(player.VoiceChannelId);
                hash.Add(player.TextChannelId);
                hash.Add(player.CurrentTrack);
                hash.Add(player.RepeatMode);
                hash.Add(player.Volume);
                hash.Add(player.BassBoostLevel);
                hash.Add(player.FilterPreset);
                hash.Add(player.Paused);
                hash.Add(player.Queue.Count);

                foreach (var track in player.Queue)
                    hash.Add(track);
            }

            return hash.ToHashCode();
        }

        public static Audio8GuildSnapshot Capture(Audio8Player player)
        {
            var queue = player.Queue
                .Select(item => item.Track)
                .Where(track => track is not null)
                .Take(MaxStoredTracks)
                .Select(track => track.ToString())
                .ToList();

            return new Audio8GuildSnapshot
            {
                GuildId = player.GuildId,
                VoiceChannelId = player.VoiceChannelId,
                TextChannelId = player.TextChannelId,
                CurrentTrack = player.CurrentTrack?.ToString(),
                PositionMs = (long)(player.Position?.Position.TotalMilliseconds ?? 0),
                Queue = queue,
                RepeatMode = player.RepeatMode,
                Volume = player.Volume,
                BassBoostLevel = player.BassBoostLevel,
                FilterPreset = player.FilterPreset,
                Paused = player.State is Lavalink4NET.Players.PlayerState.Paused
            };
        }

        public static LavalinkTrack TryParseTrack(string raw) =>
            !string.IsNullOrWhiteSpace(raw) && LavalinkTrack.TryParse(raw, null, out var track) ? track : null;

        private static bool IsIoFailure(Exception ex) =>
            ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;

        private static string ResolvePath()
        {
            var fromMessages = Global.Vars.Cfg.messagesFilePath;

            var directory = !string.IsNullOrWhiteSpace(fromMessages)
                ? Path.GetDirectoryName(Path.GetFullPath(fromMessages))
                : null;

            if (string.IsNullOrWhiteSpace(directory))
                directory = AppContext.BaseDirectory;

            return Path.Combine(directory, FileName);
        }
    }

    internal sealed class Audio8LoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Audio8Logger(Shorten(categoryName));

        public void Dispose() { }

        private static string Shorten(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
                return "lavalink";

            var lastDot = categoryName.LastIndexOf('.');
            return lastDot >= 0 && lastDot < categoryName.Length - 1
                ? categoryName[(lastDot + 1)..]
                : categoryName;
        }
    }

    internal sealed class Audio8Logger : ILogger
    {
        private readonly string _category;

        public Audio8Logger(string category) => _category = category;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var text = formatter(state, exception);

            if (IsNoise(text))
                return;

            var message = $"[{_category}] {text}";

            _ = logLevel switch
            {
                LogLevel.Critical => LoggingService.LogCriticalAsync(Audio8Constants.LogSource, message, exception),
                LogLevel.Error => LoggingService.LogErrorAsync(Audio8Constants.LogSource, message, exception),
                LogLevel.Warning => LoggingService.LogWarningAsync(Audio8Constants.LogSource, message, exception),
                _ => LoggingService.LogInformationAsync(Audio8Constants.LogSource, message)
            };
        }

        private static bool IsNoise(string text) =>
            text.Contains("before the ready payload", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("default Lavalink password", StringComparison.OrdinalIgnoreCase);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }
}
