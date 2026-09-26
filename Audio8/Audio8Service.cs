using Discord;
using Discord.Net;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Filters;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Protocol.Models;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using sblngavnav6.Core;
using sblngavnav6.Services;

namespace sblngavnav6.Audio8
{
    public sealed class Audio8Service : IDisposable, IAsyncDisposable
    {
        public static string TrackKey(LavalinkTrack track)
        {
            if (track is null)
                return "n:";
            if (track.Uri is not null)
                return "u:" + track.Uri;
            if (!string.IsNullOrWhiteSpace(track.Identifier))
                return "i:" + track.Identifier;

            return "t:" + track.Title + "|" + track.Author;
        }

        private readonly IAudioService _audio;
        private readonly DiscordSocketClient _client;
        private readonly PaginatorService _pager;
        private readonly Audio8MessageStates _states;
        private readonly Audio8StatsTracker _stats;
        private readonly Audio8Persistence _persistence;
        private readonly ConcurrentDictionary<ulong, string> _searchPrefixes = new();
        private readonly ConcurrentDictionary<ulong, List<Audio8RecentPlaylist>> _recentPlaylists = new();

        private CancellationTokenSource _cleanupCts;
        private Task _cleanupTask = Task.CompletedTask;
        private bool _disposed;

        internal Audio8Service(
            IAudioService audio,
            DiscordSocketClient client,
            PaginatorService pager,
            Audio8MessageStates states,
            Audio8StatsTracker stats,
            Audio8Persistence persistence)
        {
            _persistence = persistence;
            _audio = audio;
            _client = client;
            _pager = pager;
            _states = states;
            _stats = stats;
        }

        internal Audio8StatsTracker Stats => _stats;

        public string GetSearchPrefix(ulong guildId) =>
            _searchPrefixes.TryGetValue(guildId, out var prefix) ? prefix : Audio8Query.YouTubePrefix;

        public void SetSearchPrefix(ulong guildId, string prefix) => _searchPrefixes[guildId] = prefix;

        public bool IsVoteRunning(ulong guildId) => _states.IsVoteRunning(guildId);

        public void RememberPlaylist(ulong guildId, string name, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            var recent = _recentPlaylists.GetOrAdd(guildId, _ => []);

            lock (recent)
            {
                recent.RemoveAll(item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase));
                recent.Insert(0, new Audio8RecentPlaylist(name, url, DateTimeOffset.UtcNow));

                if (recent.Count > Audio8Constants.RecentPlaylistBuffer)
                    recent.RemoveRange(Audio8Constants.RecentPlaylistBuffer, recent.Count - Audio8Constants.RecentPlaylistBuffer);
            }
        }

        public IReadOnlyList<Audio8RecentPlaylist> GetRecentPlaylists(ulong guildId)
        {
            if (!_recentPlaylists.TryGetValue(guildId, out var recent))
                return [];

            lock (recent)
                return recent.ToArray();
        }

        public void StartCleanup(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cleanupCts != null) return;

            _cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cleanupTask = CleanupLoopAsync(_cleanupCts.Token);
        }

        public async Task StopCleanupAsync()
        {
            if (_disposed || _cleanupCts == null) return;

            await _cleanupCts.CancelAsync();
            await _cleanupTask;
        }

        private async Task CleanupLoopAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(Audio8Constants.StateCleanupInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                    _states.RemoveStale();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }

        public IEnumerable<ulong> GetActiveGuildIds() =>
            _audio.Players.Players.Select(player => player.GuildId).ToArray();

        public Audio8Player GetPlayer(ulong guildId) =>
            _audio.Players.TryGetPlayer<Audio8Player>(guildId, out var player) ? player : null;

        public bool IsConnected(ulong guildId)
        {
            var player = GetPlayer(guildId);
            return player is not null && player.State is not PlayerState.Destroyed;
        }

        public async ValueTask<Audio8Player> JoinAsync(
            IVoiceChannel voiceChannel,
            ulong textChannelId,
            CancellationToken cancellationToken = default)
        {
            var existing = GetPlayer(voiceChannel.GuildId);
            if (existing is not null)
            {
                existing.TextChannelId = textChannelId;
                return existing;
            }

            var options = new Audio8PlayerOptions
            {
                TextChannelId = textChannelId,
                ClearQueueOnStop = false,
                ResetTrackRepeatOnStop = true,
                RespectTrackRepeatOnSkip = false,
                HistoryCapacity = Audio8Constants.HistoryCapacity
            };

            return await _audio.Players
                .JoinAsync<Audio8Player, Audio8PlayerOptions>(
                    voiceChannel.GuildId,
                    voiceChannel.Id,
                    CreatePlayerAsync,
                    Options.Create(options),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        internal static ValueTask<Audio8Player> CreatePlayerAsync(
            IPlayerProperties<Audio8Player, Audio8PlayerOptions> properties,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new Audio8Player(properties));
        }

        public async Task LeaveAsync(ulong guildId, CancellationToken cancellationToken = default)
        {
            var player = GetPlayer(guildId);
            if (player is null) return;

            _states.DropGuild(guildId);

            try
            {
                await player.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                await player.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await LoggingService.LogWarningAsync(Audio8Constants.LogSource, $"Выход из войса g={guildId}: {ex.Message}");
            }
        }

        public async Task<TrackLoadResult> LoadAsync(string identifier, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _audio.Tracks
                    .LoadTracksAsync(identifier, Audio8Query.LoadOptions, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await LoggingService.LogErrorAsync(Audio8Constants.LogSource, $"Поиск не удался: {identifier}", ex);
                return TrackLoadResult.CreateError(new TrackException(ExceptionSeverity.Common, ex.Message, null));
            }
        }

        public async Task<LavalinkTrack> LoadFirstAsync(string identifier, CancellationToken cancellationToken = default)
        {
            var result = await LoadAsync(identifier, cancellationToken).ConfigureAwait(false);
            return result.HasMatches ? result.Tracks.FirstOrDefault() : null;
        }

        public async Task<Audio8PlayResult> EnqueueAsync(
            Audio8Player player,
            TrackLoadResult result,
            Audio8QueryPlan plan,
            CancellationToken cancellationToken = default)
        {
            return await player.LockedAsync(async () =>
            {
                var tracks = result.Tracks;
                var playNow = player.CurrentTrack is null && player.Queue.IsEmpty;

                if (result.IsPlaylist)
                {
                    var start = ResolvePlaylistStart(result, plan, tracks);
                    var available = tracks.Length - start;
                    var take = Math.Min(available, Audio8Constants.MaxPlaylistTracks);

                    if (take <= 0)
                        return Audio8PlayResult.Nothing;

                    var items = tracks
                        .Skip(start)
                        .Take(take)
                        .Select(track => new TrackQueueItem(new TrackReference(track)))
                        .ToArray();

                    if (playNow)
                    {
                        await player.PlayAsync(items[0], enqueue: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                        if (items.Length > 1)
                            await player.Queue.AddRangeAsync(items[1..], cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await player.Queue.AddRangeAsync(items, cancellationToken).ConfigureAwait(false);
                    }

                    var name = string.IsNullOrWhiteSpace(result.Playlist?.Name) ? "плейлист" : result.Playlist.Name;
                    var url = Uri.TryCreate(plan.Identifier, UriKind.Absolute, out _) ? plan.Identifier : null;

                    return Audio8PlayResult.Playlist(name, url, items.Length, available - take);
                }

                var index = Math.Clamp(plan.PlaylistIndex, 0, tracks.Length - 1);
                var track = tracks[index];
                await player.PlayAsync(track, enqueue: !playNow, cancellationToken: cancellationToken).ConfigureAwait(false);

                return playNow ? Audio8PlayResult.Started(track) : Audio8PlayResult.Enqueued(track);
            }, cancellationToken).ConfigureAwait(false);
        }

        private static int ResolvePlaylistStart(TrackLoadResult result, Audio8QueryPlan plan, ImmutableArray<LavalinkTrack> tracks)
        {
            if (plan.PlaylistIndex > 0 && plan.PlaylistIndex < tracks.Length)
                return plan.PlaylistIndex;

            var wanted = result.Playlist?.SelectedTrack?.Identifier ?? plan.SelectedTrackId;
            if (string.IsNullOrWhiteSpace(wanted))
                return 0;

            for (var i = 0; i < tracks.Length; i++)
            {
                if (string.Equals(tracks[i].Identifier, wanted, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return 0;
        }

        public Task<Audio8Skip> SkipAsync(Audio8Player player, int? position, CancellationToken cancellationToken = default)
        {
            return player.LockedAsync(async () =>
            {
                var skipCount = position.GetValueOrDefault(1);
                if (skipCount < 1 || player.Queue.Count < skipCount)
                    return Audio8Skip.Nothing;

                var upcoming = player.Queue.ElementAtOrDefault(skipCount - 1)?.Track;
                if (upcoming is null)
                    return Audio8Skip.Nothing;

                var replaced = player.CurrentTrack;
                await player.SkipAsync(skipCount, cancellationToken).ConfigureAwait(false);

                return new Audio8Skip(true, replaced, upcoming);
            }, cancellationToken);
        }

        public Task<LavalinkTrack> PlayPreviousAsync(Audio8Player player, CancellationToken cancellationToken = default)
        {
            return player.LockedAsync(async () =>
            {
                if (!player.Queue.HasHistory || player.Queue.History.Count == 0)
                    return null;

                var previous = player.Queue.History[^1];
                var current = player.CurrentTrack;

                await player.Queue.History.RemoveAtAsync(player.Queue.History.Count - 1, cancellationToken).ConfigureAwait(false);

                if (current is not null)
                    await player.Queue.InsertAsync(0, new TrackQueueItem(new TrackReference(current)), cancellationToken).ConfigureAwait(false);

                await player.PlayAsync(previous, enqueue: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                return previous.Track;
            }, cancellationToken);
        }

        public Task<string> ApplyFilterAsync(Audio8Player player, string preset, CancellationToken cancellationToken = default)
        {
            return player.LockedAsync(async () =>
            {
                var resolved = Audio8Filters.Resolve(preset);
                if (resolved is null)
                    return null;

                Audio8Filters.Apply(player.Filters, resolved);
                await player.Filters.CommitAsync(cancellationToken).ConfigureAwait(false);

                player.FilterPreset = resolved;
                return resolved;
            }, cancellationToken);
        }

        public Task<bool> HoistAsync(Audio8Player player, string trackKey, CancellationToken cancellationToken = default)
        {
            return player.LockedAsync(async () =>
            {
                var index = player.Queue.IndexOf(item => Audio8Service.TrackKey(item.Track) == trackKey);
                if (index < 0)
                    return false;

                var item = player.Queue[index];
                await player.Queue.RemoveAtAsync(index, cancellationToken).ConfigureAwait(false);
                await player.Queue.InsertAsync(0, item, cancellationToken).ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        public Task<int> ShuffleAsync(Audio8Player player, CancellationToken cancellationToken = default)
        {
            return player.LockedAsync(async () =>
            {
                if (player.Queue.Count < 2)
                    return 0;

                await player.Queue.ShuffleAsync(cancellationToken).ConfigureAwait(false);
                await SpreadSameAuthorsAsync(player, cancellationToken).ConfigureAwait(false);

                return player.Queue.Count;
            }, cancellationToken);
        }

        private static async Task SpreadSameAuthorsAsync(Audio8Player player, CancellationToken cancellationToken)
        {
            var items = player.Queue.ToList();

            for (var i = 1; i < items.Count; i++)
            {
                if (!SameAuthor(items[i].Track, items[i - 1].Track))
                    continue;

                for (var candidate = i + 1; candidate < items.Count; candidate++)
                {
                    var fitsPrevious = !SameAuthor(items[candidate].Track, items[i - 1].Track);
                    var fitsNext = i + 1 >= items.Count || !SameAuthor(items[candidate].Track, items[i + 1].Track);

                    if (fitsPrevious && fitsNext)
                    {
                        (items[i], items[candidate]) = (items[candidate], items[i]);
                        break;
                    }
                }
            }

            await player.Queue.ClearAsync(cancellationToken).ConfigureAwait(false);
            await player.Queue.AddRangeAsync(items, cancellationToken).ConfigureAwait(false);
        }

        private static bool SameAuthor(LavalinkTrack left, LavalinkTrack right) =>
            !string.IsNullOrWhiteSpace(left?.Author) &&
            string.Equals(left.Author, right?.Author, StringComparison.OrdinalIgnoreCase);

        private static readonly float[][] BassBoostGains =
        [
            [],
            [0.10f, 0.08f, 0.05f],
            [0.20f, 0.15f, 0.10f, 0.05f],
            [0.32f, 0.25f, 0.18f, 0.10f, 0.05f]
        ];

        public static string BassBoostName(int level) => level switch
        {
            2 => "лёгкий",
            3 => "средний",
            4 => "жёсткий",
            _ => "выключен"
        };

        public Task SetBassBoostAsync(Audio8Player player, int level, CancellationToken cancellationToken = default)
        {
            level = Math.Clamp(level, Audio8Constants.MinBassBoost, Audio8Constants.MaxBassBoost);

            return player.LockedAsync(async () =>
            {
                if (level == Audio8Constants.MinBassBoost)
                {
                    player.Filters.Equalizer = null;
                }
                else
                {
                    var gains = BassBoostGains[level - 1];
                    var builder = Equalizer.CreateBuilder();

                    for (var band = 0; band < gains.Length; band++)
                        builder[band] = gains[band];

                    player.Filters.Equalizer = new EqualizerFilterOptions(builder.Build());
                }

                await player.Filters.CommitAsync(cancellationToken).ConfigureAwait(false);
                player.BassBoostLevel = level;
            }, cancellationToken);
        }

        public Task StopPlaybackAsync(Audio8Player player, CancellationToken cancellationToken = default)
        {
            return player.LockedAsync(async () =>
            {
                player.RepeatMode = TrackRepeatMode.None;
                await player.Queue.ClearAsync(cancellationToken).ConfigureAwait(false);
                await player.StopAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken);
        }

        public async Task SendQueueAsync(Audio8Player player, ITextChannel channel, CancellationToken cancellationToken = default)
        {
            var pages = await player.LockedAsync(async () =>
            {
                if (player.CurrentTrack is null)
                    return null;

                var upcoming = player.Queue
                    .Select(item => item.Track)
                    .Where(track => track is not null)
                    .ToList();

                return Audio8Embeds.Queue(player, upcoming);
            }, cancellationToken).ConfigureAwait(false);

            if (pages is null)
            {
                await SendAsync(channel, await Audio8Embeds.Error("лист", "очередь пуста")).ConfigureAwait(false);
                return;
            }

            var canPick = player.Queue.Count >= Audio8Constants.MinQueueForPick;

            if (player.QueueMessage is { } previous)
            {
                player.QueueMessage = null;
                try { await ModifyAsync(previous, clearControls: true).ConfigureAwait(false); }
                catch (Exception ex) when (ex is HttpException or TimeoutException) { }
            }

            var message = await _pager.SendAsync(
                channel,
                pages,
                decorate: canPick ? Audio8Controls.QueuePick() : null).ConfigureAwait(false);

            if (canPick)
                player.QueueMessage = message;
        }

        public Task<bool> SkipToQueuedAsync(Audio8Player player, string trackKey, CancellationToken cancellationToken = default)
        {
            return player.LockedAsync(async () =>
            {
                if (player.Queue.FirstOrDefault()?.Track is not { } head || TrackKey(head) != trackKey)
                    return false;

                await player.SkipAsync(1, cancellationToken).ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        public Task<Audio8QueuePick> PickFromQueueAsync(
            Audio8Player player,
            int position,
            bool playNow,
            bool dropBefore,
            CancellationToken cancellationToken = default)
        {
            return player.LockedAsync(async () =>
            {
                var index = position - 1;
                if (index < 0 || index >= player.Queue.Count)
                    return Audio8QueuePick.OutOfRange(player.Queue.Count);

                var item = player.Queue[index];
                var track = item.Track;
                var dropped = 0;

                if (dropBefore && index > 0)
                {
                    await player.Queue.RemoveRangeAsync(0, index, cancellationToken).ConfigureAwait(false);
                    dropped = index;
                }
                else if (index > 0)
                {
                    await player.Queue.RemoveAtAsync(index, cancellationToken).ConfigureAwait(false);
                    await player.Queue.InsertAsync(0, item, cancellationToken).ConfigureAwait(false);
                }

                if (playNow)
                    await player.SkipAsync(1, cancellationToken).ConfigureAwait(false);

                return Audio8QueuePick.Moved(track, playNow, dropped);
            }, cancellationToken);
        }

        public async Task SendEnqueuedAsync(
            IMessageChannel channel,
            ulong guildId,
            ulong requestedByUserId,
            LavalinkTrack track)
        {
            var message = await SendWithControlsAsync(
                channel,
                await Audio8Embeds.Enqueued(track).ConfigureAwait(false),
                Audio8Controls.Hoist(),
                requestedByUserId).ConfigureAwait(false);

            _states.AddHoist(new Audio8HoistState(
                guildId,
                message.Id,
                requestedByUserId,
                TrackKey(track),
                DateTimeOffset.UtcNow));
        }

        public Task<IUserMessage> SendWithControlsAsync(
            IMessageChannel channel,
            Embed embed,
            Action<ComponentBuilder, int> controls,
            ulong? ownerId = null) =>
            _pager.SendAsync(channel, [embed], ownerId, decorate: controls);

        public Task<IUserMessage> SendAsync(IMessageChannel channel, Embed embed) =>
            _pager.SendAsync(channel, [embed]);

        public Task ModifyAsync(
            IUserMessage message,
            Embed embed = null,
            Action<ComponentBuilder, int> controls = null,
            bool clearControls = false) =>
            _pager.ModifyAsync(message, embed, controls, clearControls);

        public MessageComponent BuildControls(Action<ComponentBuilder, int> controls) =>
            _pager.BuildControls(controls);

        internal Audio8StatsContext BuildStatsContext()
        {
            var players = _audio.Players.Players.OfType<Audio8Player>().ToArray();

            return new Audio8StatsContext(
                players.Length,
                players.Count(player => player.State is PlayerState.Playing),
                players.Sum(player => player.Queue.Count),
                _persistence.LastSavedAtUtc,
                _persistence.LastSavedGuilds);
        }

        internal Audio8State CaptureState() => new()
        {
            Guilds = CaptureGuildSettings(),
            Players = CaptureSnapshots().ToList()
        };

        internal void RestoreGuildSettings(IReadOnlyList<Audio8GuildSettings> settings)
        {
            foreach (var entry in settings)
            {
                if (!string.IsNullOrWhiteSpace(entry.SearchPrefix))
                    _searchPrefixes[entry.GuildId] = entry.SearchPrefix;

                if (entry.RecentPlaylists is not { Count: > 0 })
                    continue;

                var recent = _recentPlaylists.GetOrAdd(entry.GuildId, _ => []);

                lock (recent)
                {
                    recent.Clear();
                    recent.AddRange(entry.RecentPlaylists.Take(Audio8Constants.RecentPlaylistBuffer));
                }
            }
        }

        private List<Audio8GuildSettings> CaptureGuildSettings()
        {
            var guildIds = _searchPrefixes.Keys.Concat(_recentPlaylists.Keys).Distinct();
            var settings = new List<Audio8GuildSettings>();

            foreach (var guildId in guildIds)
            {
                var prefix = GetSearchPrefix(guildId);
                var recent = GetRecentPlaylists(guildId);

                if (prefix == Audio8Query.YouTubePrefix && recent.Count == 0)
                    continue;

                settings.Add(new Audio8GuildSettings
                {
                    GuildId = guildId,
                    SearchPrefix = prefix,
                    RecentPlaylists = recent.ToList()
                });
            }

            return settings;
        }

        internal IReadOnlyList<Audio8GuildSnapshot> CaptureSnapshots()
        {
            var snapshots = new List<Audio8GuildSnapshot>();

            foreach (var player in _audio.Players.Players.OfType<Audio8Player>())
            {
                if (player.VoiceChannelId == 0 || player.State is PlayerState.Destroyed)
                    continue;

                if (player.CurrentTrack is null && player.Queue.IsEmpty)
                    continue;

                snapshots.Add(Audio8Persistence.Capture(player));
            }

            return snapshots;
        }

        internal async Task<bool> RestoreAsync(Audio8GuildSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            var voiceChannel = await WaitForVoiceChannelAsync(snapshot, cancellationToken).ConfigureAwait(false);
            if (voiceChannel is null)
                return false;

            var player = await JoinAsync(voiceChannel, snapshot.TextChannelId, cancellationToken).ConfigureAwait(false);
            player.SilentMode = true;

            try
            {
                await player.LockedAsync(async () =>
                {
                    var queued = snapshot.Queue
                        .Select(Audio8Persistence.TryParseTrack)
                        .Where(track => track is not null)
                        .Select(track => new TrackQueueItem(new TrackReference(track)))
                        .ToArray();

                    if (queued.Length > 0)
                        await player.Queue.AddRangeAsync(queued, cancellationToken).ConfigureAwait(false);

                    player.RepeatMode = snapshot.RepeatMode;

                    if (snapshot.Volume > 0)
                        await player.SetVolumeAsync(snapshot.Volume, cancellationToken).ConfigureAwait(false);

                    var current = Audio8Persistence.TryParseTrack(snapshot.CurrentTrack);
                    if (current is null)
                        return;

                    var start = TimeSpan.FromMilliseconds(Math.Max(0, snapshot.PositionMs));
                    var properties = current.IsSeekable && start > TimeSpan.Zero
                        ? new TrackPlayProperties(StartPosition: start)
                        : default;

                    await player.PlayAsync(current, enqueue: false, properties, cancellationToken).ConfigureAwait(false);

                    if (snapshot.Paused)
                        await player.PauseAsync(cancellationToken).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);

                if (snapshot.BassBoostLevel > Audio8Constants.MinBassBoost)
                    await SetBassBoostAsync(player, snapshot.BassBoostLevel, cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(snapshot.FilterPreset) && snapshot.FilterPreset != Audio8Constants.NoFilterPreset)
                    await ApplyFilterAsync(player, snapshot.FilterPreset, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                player.SilentMode = false;
            }

            return true;
        }

        private async Task<IVoiceChannel> WaitForVoiceChannelAsync(Audio8GuildSnapshot snapshot, CancellationToken cancellationToken)
        {
            var deadline = DateTimeOffset.UtcNow + Audio8Constants.GuildWaitTimeout;

            while (true)
            {
                var guild = _client.GetGuild(snapshot.GuildId);

                if (guild is { IsConnected: true })
                {
                    if (guild.GetVoiceChannel(snapshot.VoiceChannelId) is { } channel)
                        return channel;

                    return null;
                }

                if (DateTimeOffset.UtcNow >= deadline)
                    return null;

                await Task.Delay(Audio8Constants.GuildWaitStep, cancellationToken).ConfigureAwait(false);
            }
        }

        public static bool IsInSameVoice(IUser user, Audio8Player player) =>
            user is IVoiceState { VoiceChannel: { } channel } && channel.Id == player.VoiceChannelId;

        public ITextChannel ResolveTextChannel(Audio8Player player) =>
            player.TextChannelId != 0 && _client.GetChannel(player.TextChannelId) is ITextChannel channel ? channel : null;

        public async ValueTask DisposeAsync()
        {
            try { await StopCleanupAsync(); }
            finally { Dispose(); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _cleanupCts?.Cancel(); }
            catch (ObjectDisposedException) { }
            _cleanupCts?.Dispose();
        }
    }

    public readonly record struct Audio8RecentPlaylist(string Name, string Url, DateTimeOffset AddedAt);

    public readonly record struct Audio8Skip(bool Ok, LavalinkTrack Replaced, LavalinkTrack Upcoming)
    {
        public static Audio8Skip Nothing => new(false, null, null);
    }

    public readonly record struct Audio8QueuePick(bool Found, LavalinkTrack Track, bool PlayingNow, int Dropped, int QueueCount)
    {
        public static Audio8QueuePick OutOfRange(int queueCount) => new(false, null, false, 0, queueCount);

        public static Audio8QueuePick Moved(LavalinkTrack track, bool playingNow, int dropped) =>
            new(true, track, playingNow, dropped, 0);
    }

    public readonly record struct Audio8PlayResult(Audio8PlayKind Kind, LavalinkTrack Track, string PlaylistName)
    {
        public string PlaylistUrl { get; init; }

        public int Added { get; init; }

        public int Skipped { get; init; }

        public static Audio8PlayResult Nothing => new(Audio8PlayKind.Nothing, null, null);

        public static Audio8PlayResult Started(LavalinkTrack track) => new(Audio8PlayKind.Started, track, null);

        public static Audio8PlayResult Enqueued(LavalinkTrack track) => new(Audio8PlayKind.Enqueued, track, null);

        public static Audio8PlayResult Playlist(string name, string url, int added, int skipped) =>
            new(Audio8PlayKind.Playlist, null, name) { PlaylistUrl = url, Added = added, Skipped = skipped };
    }

    public enum Audio8PlayKind
    {
        Nothing,
        Started,
        Enqueued,
        Playlist
    }

    internal static class Audio8Constants
    {
        public const int MaxVolume = 500;
        public const int MinVolume = 1;
        public const int MinBassBoost = 1;
        public const int MaxBassBoost = 4;
        public const int MaxPlaylistTracks = 250;
        public const int MinQueueForPick = 2;
        public const int RecentPlaylistBuffer = 5;
        public const int HistoryCapacity = 25;
        public const int QueuePageSize = 10;
        public const int MaxVoteItems = 50;
        public const int MaxSearchPicks = 3;
        public const int ProgressBarSize = 15;

        public static readonly TimeSpan MessageStateLifetime = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan ControlLifetime = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan NowPlayingDedupeWindow = TimeSpan.FromMilliseconds(1200);
        public static readonly TimeSpan LavalinkReadyTimeout = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan GuildWaitTimeout = TimeSpan.FromSeconds(20);
        public static readonly TimeSpan GuildWaitStep = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan PositionRefreshInterval = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(20);
        public static readonly TimeSpan StateCleanupInterval = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan TrackStartTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(2);

        public const string NoFilterPreset = "выкл";

        public const string LogSource = "AUDI8";

        public const string EmojiLoop = "🔁";
        public const string EmojiHoist = "🔼";
        public static readonly string[] EmojiPicks = ["1️⃣", "2️⃣", "3️⃣"];
    }
}
