using Discord;
using Discord.Net;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Tracks;
using sblngavnav6.Data;
using sblngavnav6.Services;

namespace sblngavnav6.Audio8
{
    internal sealed record Audio8PlaybackSnapshot(
        LavalinkTrack Track,
        TimeSpan Position,
        TrackRepeatMode RepeatMode,
        IReadOnlyList<ITrackQueueItem> Queue);

    internal sealed class Audio8VoteService
    {
        private static readonly TimeSpan SkippableThreshold = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan ResultLinger = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan RestoreSeekDelay = TimeSpan.FromMilliseconds(800);
        private static readonly TimeSpan MinRestorePosition = TimeSpan.FromSeconds(3);

        private readonly Audio8Service _service;
        private readonly Audio8MessageStates _states;
        private readonly Audio8TtsComposer _composer;

        public Audio8VoteService(
            Audio8Service service,
            Audio8MessageStates states,
            Audio8TtsComposer composer)
        {
            _service = service;
            _states = states;
            _composer = composer;
        }

        public async Task RunAsync(
            Audio8Player player,
            IUserMessage statusMessage,
            IReadOnlyList<string> items,
            string winner,
            HttpClient http,
            CancellationToken cancellationToken = default)
        {
            if (!_states.TryStartVoteSession(player.GuildId, statusMessage.Id))
            {
                await _service.ModifyAsync(statusMessage,
                    Audio8Embeds.Vote("⚠️ На сервере уже идёт голосование, дождись его окончания", Color.Orange))
                    .ConfigureAwait(false);
                return;
            }

            Audio8VoteAudio audio;
            try
            {
                audio = await _composer.BuildAsync(items, winner, http, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _states.EndVoteSession(player.GuildId);
                await LoggingService.LogErrorAsync(Audio8Constants.LogSource, "Сборка аудио голосования сорвалась", ex);
                await _service.ModifyAsync(statusMessage,
                    Audio8Embeds.Vote("❌ Ошибка при подготовке аудио", Color.Red)).ConfigureAwait(false);
                return;
            }

            Audio8PlaybackSnapshot snapshot = null;

            try
            {
                var voteTrack = await LoadLocalAsync(audio.Path, cancellationToken).ConfigureAwait(false);
                if (voteTrack is null)
                {
                    await LoggingService.LogErrorAsync(
                        Audio8Constants.LogSource,
                        $"Lavalink не видит {audio.Path}. Смонтируй каталог аудио в Lavalink и включи local source, " +
                        "а если путь внутри Lavalink другой - задай Lava:AudioPath (или SBLN_AUDIO_DIR_LAVA).");

                    await _service.ModifyAsync(statusMessage, Audio8Embeds.Vote(
                        "❌ Lavalink не видит аудиофайл голосования - озвучка недоступна, голосование отменено",
                        Color.Red)).ConfigureAwait(false);
                    return;
                }

                snapshot = await CaptureAndSilenceAsync(player, cancellationToken).ConfigureAwait(false);

                var started = player.WaitForTrackStartAsync(Audio8Constants.TrackStartTimeout, cancellationToken);
                await player.PlayAsync(voteTrack, enqueue: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                await started.ConfigureAwait(false);

                await _service.ModifyAsync(statusMessage, Audio8Embeds.Vote(
                    "🎙️ Варианты дрипа...\n\n" + FormatItems(items), Color.DarkBlue)).ConfigureAwait(false);

                await AnnounceAsync(player, statusMessage, audio, cancellationToken).ConfigureAwait(false);
                await CountdownAsync(statusMessage, items, audio.CountdownDuration, cancellationToken).ConfigureAwait(false);

                await _service.ModifyAsync(statusMessage, Audio8Embeds.VoteFinished(winner)).ConfigureAwait(false);
                await Task.Delay(ResultLinger, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _states.RemoveVoteSkip(statusMessage.Id);
                player.SilentMode = false;

                if (snapshot is not null)
                {
                    try { await RestoreAsync(player, snapshot).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        await LoggingService.LogWarningAsync(
                            Audio8Constants.LogSource,
                            $"Не удалось восстановить воспроизведение: {ex.Message}");
                    }
                }

                _states.EndVoteSession(player.GuildId);

                try { File.Delete(audio.Path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }

        private static Task<Audio8PlaybackSnapshot> CaptureAndSilenceAsync(Audio8Player player, CancellationToken cancellationToken)
        {
            return player.LockedAsync(async () =>
            {
                var snapshot = new Audio8PlaybackSnapshot(
                    player.CurrentTrack,
                    player.Position?.Position ?? TimeSpan.Zero,
                    player.RepeatMode,
                    player.Queue.ToList());

                player.SilentMode = true;
                player.RepeatMode = TrackRepeatMode.None;
                await player.Queue.ClearAsync(cancellationToken).ConfigureAwait(false);

                return snapshot;
            }, cancellationToken);
        }

        private async Task AnnounceAsync(
            Audio8Player player,
            IUserMessage statusMessage,
            Audio8VoteAudio audio,
            CancellationToken cancellationToken)
        {
            var skip = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var skippable = audio.AnnounceDuration > SkippableThreshold;

            if (skippable)
            {
                _states.AddVoteSkip(statusMessage.Id, new Audio8VoteSkipState(player.GuildId, audio.AnnounceDuration, skip));
                await _service.ModifyAsync(statusMessage, controls: Audio8Controls.VoteSkip()).ConfigureAwait(false);
            }

            await Task.WhenAny(Task.Delay(audio.AnnounceDuration, cancellationToken), skip.Task).ConfigureAwait(false);

            if (!skippable)
                return;

            _states.RemoveVoteSkip(statusMessage.Id);

            try { await _service.ModifyAsync(statusMessage, clearControls: true).ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpException or TimeoutException) { }
        }

        private async Task CountdownAsync(
            IUserMessage statusMessage,
            IReadOnlyList<string> items,
            TimeSpan duration,
            CancellationToken cancellationToken)
        {
            var seconds = Math.Max(1, (int)duration.TotalSeconds);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(seconds);

            for (var remaining = seconds; remaining >= 1; remaining--)
            {
                await _service.ModifyAsync(statusMessage, Audio8Embeds.Vote(
                    $"⏳ ВАЙБИМ: **{remaining}**с.\n\n" + FormatItems(items), Color.DarkBlue)).ConfigureAwait(false);

                var nextTick = deadline.AddSeconds(-(remaining - 1));
                var wait = nextTick - DateTimeOffset.UtcNow;

                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task RestoreAsync(Audio8Player player, Audio8PlaybackSnapshot snapshot)
        {
            var restored = await player.LockedAsync(async () =>
            {
                await player.Queue.ClearAsync().ConfigureAwait(false);

                if (snapshot.Queue.Count > 0)
                    await player.Queue.AddRangeAsync(snapshot.Queue).ConfigureAwait(false);

                player.RepeatMode = snapshot.RepeatMode;

                if (snapshot.Track is null)
                    return false;

                await player.PlayAsync(snapshot.Track, enqueue: false).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);

            if (!restored || snapshot.Position <= MinRestorePosition)
                return;

            await Task.Delay(RestoreSeekDelay).ConfigureAwait(false);

            try { await player.SeekAsync(snapshot.Position).ConfigureAwait(false); }
            catch (Exception ex)
            {
                await LoggingService.LogWarningAsync(Audio8Constants.LogSource, $"Возврат позиции не удался: {ex.Message}");
            }
        }

        private static string FormatItems(IReadOnlyList<string> items) =>
            string.Join("\n", items.Select((item, index) => $"`{index + 1}.` {item}"));

        private async Task<LavalinkTrack> LoadLocalAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var localPath = Path.GetFullPath(filePath);

            foreach (var candidate in BuildCandidates(localPath))
            {
                var track = await _service.LoadFirstAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (track is not null)
                    return track;

                await LoggingService.LogWarningAsync(Audio8Constants.LogSource, $"Lavalink не отдал трек по {candidate}");
            }

            return null;
        }

        private static IEnumerable<string> BuildCandidates(string localPath)
        {
            var remotePath = MapToLavalinkPath(localPath);

            if (remotePath is not null)
            {
                yield return remotePath;
                yield return ToFileUri(remotePath);
            }

            yield return localPath;
            yield return ToFileUri(localPath);
        }

        private static string ToFileUri(string path) =>
            Uri.TryCreate(path, UriKind.Absolute, out var uri) ? uri.AbsoluteUri : "file://" + path.Replace('\\', '/');

        private static string MapToLavalinkPath(string localPath)
        {
            var lavalinkRoot = Global.Vars.Cfg.lavaAudioPath;
            if (string.IsNullOrWhiteSpace(lavalinkRoot))
                return null;

            var localRoot = Environment.GetEnvironmentVariable("SBLN_AUDIO_DIR");
            if (string.IsNullOrWhiteSpace(localRoot))
                return null;

            localRoot = Path.GetFullPath(localRoot);
            if (!localPath.StartsWith(localRoot, StringComparison.OrdinalIgnoreCase))
                return null;

            var relative = localPath[localRoot.Length..].TrimStart('/', '\\').Replace('\\', '/');
            return $"{lavalinkRoot.TrimEnd('/', '\\')}/{relative}";
        }
    }
}
