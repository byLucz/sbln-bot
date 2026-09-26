using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Events;
using Lavalink4NET.Events.Players;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Usage;
using Lavalink4NET.Tracks;
using System.Collections.Concurrent;
using sblngavnav6.Services;

namespace sblngavnav6.Audio8
{
    internal sealed record Audio8HoistState(
        ulong GuildId,
        ulong MessageId,
        ulong RequestedByUserId,
        string TrackKey,
        DateTimeOffset CreatedAtUtc);

    internal sealed record Audio8PickState(
        ulong GuildId,
        ulong MessageId,
        ulong RequestedByUserId,
        IReadOnlyList<LavalinkTrack> Picks,
        DateTimeOffset CreatedAtUtc);

    internal sealed record Audio8VoteSkipState(
        ulong GuildId,
        TimeSpan AnnounceDuration,
        TaskCompletionSource<bool> Skip);

    internal sealed record Audio8SkipState(
        ulong GuildId,
        ulong MessageId,
        ulong RequestedByUserId,
        string TrackKey,
        DateTimeOffset CreatedAtUtc);

    internal sealed class Audio8MessageStates
    {
        private readonly ConcurrentDictionary<ulong, Audio8HoistState> _hoists = new();
        private readonly ConcurrentDictionary<ulong, Audio8PickState> _picks = new();
        private readonly ConcurrentDictionary<ulong, Audio8VoteSkipState> _voteSkips = new();
        private readonly ConcurrentDictionary<ulong, Audio8SkipState> _skips = new();
        private readonly ConcurrentDictionary<ulong, ulong> _voteSessions = new();

        public void AddHoist(Audio8HoistState state) => _hoists[state.MessageId] = state;

        public bool TryGetHoist(ulong messageId, out Audio8HoistState state) => _hoists.TryGetValue(messageId, out state);

        public void RemoveHoist(ulong messageId) => _hoists.TryRemove(messageId, out _);

        public void AddPick(Audio8PickState state) => _picks[state.MessageId] = state;

        public bool TryGetPick(ulong messageId, out Audio8PickState state) => _picks.TryGetValue(messageId, out state);

        public void RemovePick(ulong messageId) => _picks.TryRemove(messageId, out _);

        public void AddVoteSkip(ulong messageId, Audio8VoteSkipState state) => _voteSkips[messageId] = state;

        public bool TryGetVoteSkip(ulong messageId, out Audio8VoteSkipState state) => _voteSkips.TryGetValue(messageId, out state);

        public void RemoveVoteSkip(ulong messageId) => _voteSkips.TryRemove(messageId, out _);

        public bool TryStartVoteSession(ulong guildId, ulong messageId) => _voteSessions.TryAdd(guildId, messageId);

        public void EndVoteSession(ulong guildId) => _voteSessions.TryRemove(guildId, out _);

        public bool IsVoteRunning(ulong guildId) => _voteSessions.ContainsKey(guildId);

        public void AddSkip(Audio8SkipState state) => _skips[state.MessageId] = state;

        public bool TryGetSkip(ulong messageId, out Audio8SkipState state) => _skips.TryGetValue(messageId, out state);

        public void RemoveSkip(ulong messageId) => _skips.TryRemove(messageId, out _);

        public void DropGuild(ulong guildId)
        {
            foreach (var pair in _hoists.ToArray())
                if (pair.Value.GuildId == guildId)
                    _hoists.TryRemove(pair);

            foreach (var pair in _picks.ToArray())
                if (pair.Value.GuildId == guildId)
                    _picks.TryRemove(pair);

            foreach (var pair in _voteSkips.ToArray())
                if (pair.Value.GuildId == guildId)
                    _voteSkips.TryRemove(pair);

            foreach (var pair in _skips.ToArray())
                if (pair.Value.GuildId == guildId)
                    _skips.TryRemove(pair);

            _voteSessions.TryRemove(guildId, out _);
        }

        public void RemoveStale()
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var pair in _hoists.ToArray())
                if (now - pair.Value.CreatedAtUtc > Audio8Constants.MessageStateLifetime)
                    _hoists.TryRemove(pair);

            foreach (var pair in _picks.ToArray())
                if (now - pair.Value.CreatedAtUtc > Audio8Constants.MessageStateLifetime)
                    _picks.TryRemove(pair);

            foreach (var pair in _skips.ToArray())
                if (now - pair.Value.CreatedAtUtc > Audio8Constants.MessageStateLifetime)
                    _skips.TryRemove(pair);
        }
    }

    internal static class Audio8Controls
    {
        public const string NowPlayingId = "a8np";
        public const string HoistId = "a8hoist";
        public const string PickId = "a8pick";
        public const string VoteSkipId = "a8vskip";
        public const string SkipToTrackId = "a8skipnow";
        public const string QueuePickId = "a8qpick";
        public const string QueuePickModalId = "a8qpickmodal";
        public const string RecentPlaylistId = "a8recent";

        public static Action<ComponentBuilder, int> NowPlaying(bool repeatEnabled) => (builder, _) =>
        {
            builder.WithButton("Скип", $"{NowPlayingId}:skip", ButtonStyle.Secondary, new Emoji("⏭️"));
            builder.WithButton("Лист", $"{NowPlayingId}:queue", ButtonStyle.Secondary, new Emoji("📜"));
            builder.WithButton(
                repeatEnabled ? "Луп вкл" : "Луп выкл",
                $"{NowPlayingId}:loop",
                repeatEnabled ? ButtonStyle.Success : ButtonStyle.Secondary,
                new Emoji(Audio8Constants.EmojiLoop));
        };

        public static Action<ComponentBuilder, int> Hoist() => (builder, _) =>
            builder.WithButton("В начало листа", HoistId, ButtonStyle.Secondary, new Emoji(Audio8Constants.EmojiHoist));

        public static Action<ComponentBuilder, int> Picks(int count) => (builder, _) =>
        {
            for (var index = 0; index < count; index++)
                builder.WithButton($"{index + 1}", $"{PickId}:{index}", ButtonStyle.Secondary);
        };

        public static Action<ComponentBuilder, int> VoteSkip() => (builder, _) =>
            builder.WithButton("Пропустить озвучку", VoteSkipId, ButtonStyle.Secondary, new Emoji("⏭️"));

        public static Action<ComponentBuilder, int> SkipToTrack() => (builder, _) =>
            builder.WithButton("Скипнуть текущий", SkipToTrackId, ButtonStyle.Secondary, new Emoji("⏭️"));

        public static Action<ComponentBuilder, int> RecentPlaylists(int count) => (builder, _) =>
        {
            for (var index = 0; index < count; index++)
                builder.WithButton($"{index + 1}", $"{RecentPlaylistId}:{index}", ButtonStyle.Secondary);
        };

        public static Action<ComponentBuilder, int> QueuePick() => (builder, _) =>
            builder.WithButton("Выбрать трек", QueuePickId, ButtonStyle.Secondary, new Emoji("🎯"), row: 1);
    }

    internal sealed class Audio8Interactions : IDisposable
    {
        private readonly IAudioService _audio;
        private readonly Audio8Service _service;
        private bool _disposed;

        public Audio8Interactions(IAudioService audio, Audio8Service service)
        {
            _audio = audio;
            _service = service;
            _audio.TrackStarted += OnTrackStartedAsync;
            _audio.TrackException += OnTrackExceptionAsync;
            _audio.TrackStuck += OnTrackStuckAsync;
        }

        private async Task OnTrackStartedAsync(object sender, TrackStartedEventArgs args)
        {
            if (args.Player is not Audio8Player player)
                return;

            var kind = player.ClassifyAnnouncement(args.Track);
            if (kind is Audio8Announcement.None)
                return;

            var channel = _service.ResolveTextChannel(player);
            if (channel is null)
                return;

            try
            {
                var embed = await Audio8Embeds.NowPlaying(args.Track, player).ConfigureAwait(false);
                var controls = Audio8Controls.NowPlaying(player.RepeatEnabled);

                if (kind is Audio8Announcement.Repeat && player.NowPlayingMessage is { } existing)
                {
                    await _service.ModifyAsync(existing, embed, controls).ConfigureAwait(false);
                    return;
                }

                await RetireNowPlayingAsync(player).ConfigureAwait(false);
                player.NowPlayingMessage = await _service.SendWithControlsAsync(channel, embed, controls).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await LoggingService.LogWarningAsync(
                    Audio8Constants.LogSource,
                    $"Не удалось показать NowPlaying g={player.GuildId}: {ex.Message}");
            }
        }

        private Task OnTrackExceptionAsync(object sender, TrackExceptionEventArgs args) =>
            ReportTrackProblemAsync(
                args.Player,
                args.Track,
                string.IsNullOrWhiteSpace(args.Exception.Message) ? "источник отказал" : args.Exception.Message,
                $"TrackException: {args.Exception.Severity} {args.Exception.Message}");

        private Task OnTrackStuckAsync(object sender, TrackStuckEventArgs args) =>
            ReportTrackProblemAsync(
                args.Player,
                args.Track,
                $"трек завис больше чем на {args.Threshold.TotalSeconds:0}с",
                $"TrackStuck: {args.Threshold}");

        private async Task ReportTrackProblemAsync(
            ILavalinkPlayer source,
            LavalinkTrack track,
            string userReason,
            string logReason)
        {
            await LoggingService.LogWarningAsync(
                Audio8Constants.LogSource,
                $"{logReason} g={source?.GuildId} track={track?.Title}");

            if (source is not Audio8Player player || player.SilentMode)
                return;

            var channel = _service.ResolveTextChannel(player);
            if (channel is null)
                return;

            try { await _service.SendAsync(channel, await Audio8Embeds.TrackFailed(track, userReason)).ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpException or TimeoutException) { }
        }

        private async Task RetireNowPlayingAsync(Audio8Player player)
        {
            if (player.NowPlayingMessage is not { } previous)
                return;

            player.NowPlayingMessage = null;

            try { await _service.ModifyAsync(previous, clearControls: true).ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpException or TimeoutException) { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _audio.TrackStarted -= OnTrackStartedAsync;
            _audio.TrackException -= OnTrackExceptionAsync;
            _audio.TrackStuck -= OnTrackStuckAsync;
        }
    }

    public sealed class Audio8Components : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly Audio8Service _service;
        private readonly Audio8MessageStates _states;

        internal Audio8Components(Audio8Service service, Audio8MessageStates states)
        {
            _service = service;
            _states = states;
        }

        [ComponentInteraction($"{Audio8Controls.NowPlayingId}:*")]
        public async Task NowPlayingAsync(string action)
        {
            if (Context.Interaction is not SocketMessageComponent component)
                return;

            var player = _service.GetPlayer(Context.Guild.Id);
            if (player is null)
            {
                await RespondAsync("плеер уже не в войсе", ephemeral: true);
                return;
            }

            if (player.NowPlayingMessage?.Id != component.Message.Id)
            {
                await RespondAsync("это старое сообщение, кнопки в нём уже не работают", ephemeral: true);
                return;
            }

            if (!Audio8Service.IsInSameVoice(Context.User, player))
            {
                await RespondAsync("надо быть в том же войсе где я", ephemeral: true);
                return;
            }

            if (action == "skip")
            {
                await component.DeferAsync();
                await _service.SkipAsync(player, position: null);
                return;
            }

            if (action == "queue")
            {
                await component.DeferAsync();

                if (Context.Channel is ITextChannel channel)
                    await _service.SendQueueAsync(player, channel);

                return;
            }

            if (action != "loop")
            {
                await component.DeferAsync();
                return;
            }

            var enabled = player.ToggleRepeat();

            if (player.CurrentTrack is not { } track)
            {
                await component.DeferAsync();
                return;
            }

            var embed = await Audio8Embeds.NowPlaying(track, player);
            var controls = _service.BuildControls(Audio8Controls.NowPlaying(enabled));

            await component.UpdateAsync(message =>
            {
                message.Embed = embed;
                message.Components = controls;
            });
        }

        [ComponentInteraction(Audio8Controls.HoistId)]
        public async Task HoistAsync()
        {
            if (Context.Interaction is not SocketMessageComponent component)
                return;

            if (!_states.TryGetHoist(component.Message.Id, out var state))
            {
                await RespondAsync("кнопка устарела", ephemeral: true);
                return;
            }

            if (DateTimeOffset.UtcNow - state.CreatedAtUtc > Audio8Constants.ControlLifetime)
            {
                _states.RemoveHoist(state.MessageId);
                await RespondAsync("кнопка устарела", ephemeral: true);
                return;
            }

            if (Context.User.Id != state.RequestedByUserId)
            {
                await RespondAsync("это не твой трек", ephemeral: true);
                return;
            }

            var player = _service.GetPlayer(state.GuildId);
            if (player is null)
            {
                _states.RemoveHoist(state.MessageId);
                await RespondAsync("плеер уже не в войсе", ephemeral: true);
                return;
            }

            if (!await _service.HoistAsync(player, state.TrackKey))
            {
                _states.RemoveHoist(state.MessageId);
                await RespondAsync("трека уже нет в очереди", ephemeral: true);
                return;
            }

            _states.RemoveHoist(state.MessageId);

            var track = player.Queue.FirstOrDefault()?.Track;
            if (track is null)
            {
                await component.DeferAsync();
                return;
            }

            var embed = await Audio8Embeds.Hoisted(track);
            var controls = _service.BuildControls(Audio8Controls.SkipToTrack());

            await component.UpdateAsync(message =>
            {
                message.Embed = embed;
                message.Components = controls;
            });

            _states.AddSkip(new Audio8SkipState(
                state.GuildId,
                component.Message.Id,
                state.RequestedByUserId,
                Audio8Service.TrackKey(track),
                DateTimeOffset.UtcNow));
        }

        [ComponentInteraction($"{Audio8Controls.PickId}:*")]
        public async Task PickAsync(string indexRaw)
        {
            if (Context.Interaction is not SocketMessageComponent component)
                return;

            if (!_states.TryGetPick(component.Message.Id, out var state))
            {
                await RespondAsync("кнопка устарела", ephemeral: true);
                return;
            }

            if (DateTimeOffset.UtcNow - state.CreatedAtUtc > Audio8Constants.ControlLifetime)
            {
                _states.RemovePick(state.MessageId);
                await RespondAsync("кнопка устарела", ephemeral: true);
                return;
            }

            if (Context.User.Id != state.RequestedByUserId)
            {
                await RespondAsync("это не твой поиск", ephemeral: true);
                return;
            }

            if (!int.TryParse(indexRaw, out var index) || index < 0 || index >= state.Picks.Count)
            {
                await component.DeferAsync();
                return;
            }

            var player = _service.GetPlayer(state.GuildId);
            if (player is null)
            {
                _states.RemovePick(state.MessageId);
                await RespondAsync("плеер уже не в войсе", ephemeral: true);
                return;
            }

            var picked = state.Picks[index];

            var queued = await player.LockedAsync(async () =>
            {
                if (player.CurrentTrack is null && player.Queue.IsEmpty)
                {
                    await player.PlayAsync(picked, enqueue: false).ConfigureAwait(false);
                    return false;
                }

                await player.Queue.InsertAsync(0, new TrackQueueItem(new TrackReference(picked))).ConfigureAwait(false);
                return true;
            });

            _states.RemovePick(state.MessageId);

            var embed = await Audio8Embeds.PickChosen(picked);
            var controls = _service.BuildControls(queued ? Audio8Controls.SkipToTrack() : null);

            await component.UpdateAsync(message =>
            {
                message.Embed = embed;
                message.Components = controls;
            });

            if (queued)
            {
                _states.AddSkip(new Audio8SkipState(
                    state.GuildId,
                    component.Message.Id,
                    state.RequestedByUserId,
                    Audio8Service.TrackKey(picked),
                    DateTimeOffset.UtcNow));
            }
        }

        [ComponentInteraction(Audio8Controls.VoteSkipId)]
        public async Task VoteSkipAsync()
        {
            if (Context.Interaction is not SocketMessageComponent component)
                return;

            if (!_states.TryGetVoteSkip(component.Message.Id, out var state))
            {
                await RespondAsync("голосование уже идёт дальше", ephemeral: true);
                return;
            }

            await component.DeferAsync();

            var player = _service.GetPlayer(state.GuildId);
            if (player?.CurrentTrack is not null)
            {
                try { await player.SeekAsync(state.AnnounceDuration); }
                catch (Exception ex) when (ex is HttpException or TimeoutException or InvalidOperationException)
                {
                    await LoggingService.LogWarningAsync(Audio8Constants.LogSource, $"Пропуск озвучки не удался: {ex.Message}");
                }
            }

            state.Skip.TrySetResult(true);
        }

        [ComponentInteraction(Audio8Controls.SkipToTrackId)]
        public async Task SkipToTrackAsync()
        {
            if (Context.Interaction is not SocketMessageComponent component)
                return;

            if (!_states.TryGetSkip(component.Message.Id, out var state) ||
                DateTimeOffset.UtcNow - state.CreatedAtUtc > Audio8Constants.ControlLifetime)
            {
                _states.RemoveSkip(component.Message.Id);
                await RespondAsync("кнопка устарела", ephemeral: true);
                return;
            }

            if (Context.User.Id != state.RequestedByUserId)
            {
                await RespondAsync("это не твой трек", ephemeral: true);
                return;
            }

            var player = _service.GetPlayer(state.GuildId);
            if (player is null)
            {
                _states.RemoveSkip(state.MessageId);
                await RespondAsync("плеер уже не в войсе", ephemeral: true);
                return;
            }

            if (!await _service.SkipToQueuedAsync(player, state.TrackKey))
            {
                _states.RemoveSkip(state.MessageId);
                await RespondAsync("трек уже не первый в очереди", ephemeral: true);
                return;
            }

            _states.RemoveSkip(state.MessageId);

            var cleared = _service.BuildControls(null);
            await component.UpdateAsync(message => message.Components = cleared);
        }

        [ComponentInteraction($"{Audio8Controls.RecentPlaylistId}:*")]
        public async Task RecentPlaylistAsync(string indexRaw)
        {
            if (Context.Interaction is not SocketMessageComponent component)
                return;

            if (_service.IsVoteRunning(Context.Guild.Id))
            {
                await RespondAsync("идёт голосование, дождись конца", ephemeral: true);
                return;
            }

            var recent = _service.GetRecentPlaylists(Context.Guild.Id);

            if (!int.TryParse(indexRaw, out var index) || index < 0 || index >= recent.Count)
            {
                await RespondAsync("этого плейлиста больше нет в списке", ephemeral: true);
                return;
            }

            if (Context.User is not IVoiceState { VoiceChannel: { } voiceChannel })
            {
                await RespondAsync("надо быть в войсе", ephemeral: true);
                return;
            }

            var player = _service.GetPlayer(Context.Guild.Id);

            if (player is not null && !Audio8Service.IsInSameVoice(Context.User, player))
            {
                await RespondAsync("ты не в том войсе где я", ephemeral: true);
                return;
            }

            await component.DeferAsync();

            var chosen = recent[index];

            try
            {
                player ??= await _service.JoinAsync(voiceChannel, component.Channel.Id);

                var result = await _service.LoadAsync(chosen.Url);
                if (!result.HasMatches)
                {
                    await component.FollowupAsync(
                        embed: await Audio8Embeds.Error("недавние плейлисты", $"не смог загрузить {chosen.Name}"),
                        ephemeral: true);
                    return;
                }

                var plan = new Audio8QueryPlan(chosen.Url, 0, null, null);
                var outcome = await _service.EnqueueAsync(player, result, plan);

                if (outcome.Kind is Audio8PlayKind.Playlist)
                {
                    _service.RememberPlaylist(Context.Guild.Id, outcome.PlaylistName, outcome.PlaylistUrl);
                    await component.FollowupAsync(embed: await Audio8Embeds.PlaylistEnqueued(outcome));
                    return;
                }

                if (outcome.Track is not null)
                    await component.FollowupAsync(embed: await Audio8Embeds.Enqueued(outcome.Track));
            }
            catch (Exception ex)
            {
                await LoggingService.LogErrorAsync(Audio8Constants.LogSource, $"Недавний плейлист не запустился g={Context.Guild.Id}", ex);
                await component.FollowupAsync(
                    embed: await Audio8Embeds.Error("недавние плейлисты", "не смог поставить плейлист, детали в логах"),
                    ephemeral: true);
            }
        }

        [ComponentInteraction(Audio8Controls.QueuePickId)]
        public async Task QueuePickAsync()
        {
            var player = _service.GetPlayer(Context.Guild.Id);
            if (player is null)
            {
                await RespondAsync("плеер уже не в войсе", ephemeral: true);
                return;
            }

            if (!Audio8Service.IsInSameVoice(Context.User, player))
            {
                await RespondAsync("надо быть в том же войсе где я", ephemeral: true);
                return;
            }

            if (player.Queue.IsEmpty)
            {
                await RespondAsync("в очереди нечего выбирать", ephemeral: true);
                return;
            }

            await Context.Interaction.RespondWithModalAsync<Audio8QueuePickModal>(Audio8Controls.QueuePickModalId);
        }

        [ModalInteraction(Audio8Controls.QueuePickModalId)]
        public async Task QueuePickSubmitAsync(Audio8QueuePickModal modal)
        {
            var player = _service.GetPlayer(Context.Guild.Id);
            if (player is null)
            {
                await RespondAsync("плеер уже не в войсе", ephemeral: true);
                return;
            }

            if (!int.TryParse(modal.Position?.Trim(), out var position))
            {
                await RespondAsync("номер трека - это число", ephemeral: true);
                return;
            }

            var playNow = ParseFlag(modal.PlayNow, fallback: true);
            var dropBefore = ParseFlag(modal.DropBefore, fallback: false);

            var result = await _service.PickFromQueueAsync(player, position, playNow, dropBefore);

            if (!result.Found)
            {
                await RespondAsync($"в очереди нет трека с номером {position} (всего {result.QueueCount})", ephemeral: true);
                return;
            }

            await RespondAsync(embed: await Audio8Embeds.QueuePicked(result), ephemeral: true);
        }

        private static bool ParseFlag(string value, bool fallback)
        {
            var normalized = value?.Trim().ToLowerInvariant();

            return normalized switch
            {
                null or "" => fallback,
                "да" or "+" or "yes" or "y" or "true" or "1" => true,
                "нет" or "-" or "no" or "n" or "false" or "0" => false,
                _ => fallback
            };
        }
    }

    public class Audio8QueuePickModal : IModal
    {
        public string Title => "Выбрать трек из очереди";

        [InputLabel("Номер трека в очереди")]
        [ModalTextInput("position", placeholder: "например 32", maxLength: 5)]
        public string Position { get; set; }

        [InputLabel("Включить сразу? да/нет")]
        [ModalTextInput("now", placeholder: "да", maxLength: 4)]
        [RequiredInput(false)]
        public string PlayNow { get; set; }

        [InputLabel("Убрать треки до него? да/нет")]
        [ModalTextInput("drop", placeholder: "нет", maxLength: 4)]
        [RequiredInput(false)]
        public string DropBefore { get; set; }
    }

    internal readonly record struct Audio8StatsContext(
        int Players,
        int Playing,
        int QueuedTracks,
        DateTimeOffset? SnapshotAt,
        int SnapshotGuilds);

    internal sealed class Audio8StatsTracker : IDisposable
    {
        private readonly IAudioService _audioService;
        private readonly Lock _gate = new();

        private LavalinkServerStatistics _latest;
        private DateTimeOffset _latestAtUtc;
        private bool _disposed;

        public Audio8StatsTracker(IAudioService audioService)
        {
            _audioService = audioService;
            _audioService.StatisticsUpdated += OnStatisticsUpdatedAsync;
            _audioService.WebSocketClosed += OnWebSocketClosedAsync;
        }

        public Embed Build(Audio8StatsContext context)
        {
            LavalinkServerStatistics snapshot;
            DateTimeOffset at;

            lock (_gate)
            {
                snapshot = _latest;
                at = _latestAtUtc;
            }

            return snapshot is null ? null : Audio8Embeds.Stats(snapshot, DateTimeOffset.UtcNow - at, context);
        }

        private Task OnStatisticsUpdatedAsync(object sender, StatisticsUpdatedEventArgs args)
        {
            lock (_gate)
            {
                _latest = args.Statistics;
                _latestAtUtc = DateTimeOffset.UtcNow;
            }

            return Task.CompletedTask;
        }

        private Task OnWebSocketClosedAsync(object sender, WebSocketClosedEventArgs args) =>
            LoggingService.LogWarningAsync(
                Audio8Constants.LogSource,
                $"WS закрыт: code={(int)args.CloseCode} reason={args.Reason} byRemote={args.ByRemote} guild={args.Player?.GuildId}");

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _audioService.StatisticsUpdated -= OnStatisticsUpdatedAsync;
            _audioService.WebSocketClosed -= OnWebSocketClosedAsync;
        }
    }
}
