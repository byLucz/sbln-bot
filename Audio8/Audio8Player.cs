using Discord;
using Lavalink4NET.Filters;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Tracks;

namespace sblngavnav6.Audio8
{
    public sealed record Audio8PlayerOptions : QueuedLavalinkPlayerOptions
    {
        public ulong TextChannelId { get; set; }
    }

    public enum Audio8Announcement
    {
        None,
        Fresh,
        Repeat
    }

    public sealed class Audio8Player : QueuedLavalinkPlayer
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private volatile TaskCompletionSource<bool> _trackStartAwaiter;
        private string _announcedKey;
        private DateTimeOffset _announcedAt;

        public Audio8Player(IPlayerProperties<Audio8Player, Audio8PlayerOptions> properties)
            : base(properties)
        {
            TextChannelId = properties.Options.Value.TextChannelId;
        }

        public ulong TextChannelId { get; set; }

        public bool SilentMode { get; set; }

        public int BassBoostLevel { get; set; } = Audio8Constants.MinBassBoost;

        public string FilterPreset { get; set; } = Audio8Constants.NoFilterPreset;

        public IUserMessage NowPlayingMessage { get; set; }

        public IUserMessage QueueMessage { get; set; }

        public int PlayCount { get; private set; }

        public bool RepeatEnabled => RepeatMode is TrackRepeatMode.Track;

        public bool QueueRepeatEnabled => RepeatMode is TrackRepeatMode.Queue;

        public bool ToggleRepeat()
        {
            var enabled = !RepeatEnabled;
            RepeatMode = enabled ? TrackRepeatMode.Track : TrackRepeatMode.None;
            return enabled;
        }

        public async Task<T> LockedAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { return await action().ConfigureAwait(false); }
            finally { _gate.Release(); }
        }

        public async Task LockedAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await action().ConfigureAwait(false); }
            finally { _gate.Release(); }
        }

        public Task WaitForTrackStartAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var awaiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _trackStartAwaiter = awaiter;

            return WaitAsync();

            async Task WaitAsync()
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                try { await awaiter.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                finally { Interlocked.CompareExchange(ref _trackStartAwaiter, null, awaiter); }
            }
        }

        public Audio8Announcement ClassifyAnnouncement(LavalinkTrack track)
        {
            if (SilentMode || TextChannelId == 0)
                return Audio8Announcement.None;

            var key = Audio8Service.TrackKey(track);
            var now = DateTimeOffset.UtcNow;
            var sameTrack = _announcedKey == key;

            if (sameTrack && now - _announcedAt < Audio8Constants.NowPlayingDedupeWindow)
                return Audio8Announcement.None;

            _announcedAt = now;

            if (sameTrack)
            {
                PlayCount++;
                return Audio8Announcement.Repeat;
            }

            _announcedKey = key;
            PlayCount = 1;
            return Audio8Announcement.Fresh;
        }

        protected override ValueTask NotifyTrackStartedAsync(ITrackQueueItem track, CancellationToken cancellationToken = default)
        {
            Interlocked.Exchange(ref _trackStartAwaiter, null)?.TrySetResult(true);
            return base.NotifyTrackStartedAsync(track, cancellationToken);
        }

        protected override async ValueTask DisposeAsyncCore()
        {
            await base.DisposeAsyncCore().ConfigureAwait(false);
            _gate.Dispose();
        }
    }

    internal static class Audio8Filters
    {
        public const string Nightcore = "найткор";
        public const string Slowed = "слоу";
        public const string Rotation = "8д";
        public const string Karaoke = "караоке";
        public const string Vibrato = "вибрато";

        public static readonly string[] Presets =
        [
            Audio8Constants.NoFilterPreset,
            Nightcore,
            Slowed,
            Rotation,
            Karaoke,
            Vibrato
        ];

        public static string Resolve(string preset) => preset?.Trim().ToLowerInvariant() switch
        {
            "выкл" or "сброс" or "off" or "none" or "-" => Audio8Constants.NoFilterPreset,
            "найткор" or "nightcore" or "нк" => Nightcore,
            "слоу" or "slowed" or "слоуд" or "медляк" => Slowed,
            "8д" or "8d" or "вращение" => Rotation,
            "караоке" or "karaoke" => Karaoke,
            "вибрато" or "vibrato" => Vibrato,
            _ => null
        };

        public static string Describe(string preset) => preset switch
        {
            Nightcore => "найткор - быстрее и выше",
            Slowed => "слоу - медленнее и ниже",
            Rotation => "8д - звук вращается вокруг головы",
            Karaoke => "караоке - вокал приглушён",
            Vibrato => "вибрато - плавающая высота",
            _ => "фильтры выключены"
        };

        public static void Apply(IPlayerFilters filters, string preset)
        {
            filters.Timescale = null;
            filters.Rotation = null;
            filters.Karaoke = null;
            filters.Vibrato = null;

            switch (preset)
            {
                case Nightcore:
                    filters.Timescale = new TimescaleFilterOptions(Speed: 1.15f, Pitch: 1.15f, Rate: 1.0f);
                    break;

                case Slowed:
                    filters.Timescale = new TimescaleFilterOptions(Speed: 0.85f, Pitch: 0.9f, Rate: 1.0f);
                    break;

                case Rotation:
                    filters.Rotation = new RotationFilterOptions(Frequency: 0.2f);
                    break;

                case Karaoke:
                    filters.Karaoke = new KaraokeFilterOptions(Level: 1.0f, MonoLevel: 1.0f, FilterBand: 220f, FilterWidth: 100f);
                    break;

                case Vibrato:
                    filters.Vibrato = new VibratoFilterOptions(Frequency: 4f, Depth: 0.6f);
                    break;
            }
        }
    }
}
