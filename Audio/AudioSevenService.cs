using Discord;
using Discord.WebSocket;
using sblngavnav5X.Core;
using sblngavnav5X.Services;
using System.Collections.Concurrent;
using System.Text.Json;
using Victoria;
using Victoria.Enums;
using Victoria.WebSocket.EventArgs;

namespace sblngavnav5X.Audio
{
    public sealed class AudioSevenService : IDisposable
    {
        private readonly LavaNode<LavaPlayer<LavaTrack>, LavaTrack> _lavaNode;
        private readonly DiscordSocketClient _client;

        private readonly ConcurrentDictionary<ulong, ulong> _voiceChannelIds = new();
        private readonly ConcurrentDictionary<ulong, ulong> _textChannelIds = new();
        private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _guildLocks = new();
        private readonly ConcurrentDictionary<ulong, bool> _repeatEnabled = new();
        private readonly ConcurrentDictionary<ulong, (string key, DateTimeOffset at)> _ddCheck = new();


        private readonly object _statsLock = new();
        private StatsEventArg? _lastStats;
        private DateTimeOffset _lastStatsAtUtc;

        private string FormatTime(TimeSpan time) => time.ToString(@"hh\:mm\:ss");

        public AudioSevenService(
            LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode,
            DiscordSocketClient client)
        {
            _lavaNode = lavaNode;
            _client = client;

            _lavaNode.OnWebSocketClosed += OnWebSocketClosedAsync;
            _lavaNode.OnStats += OnStatsAsync;
            _lavaNode.OnTrackEnd += OnTrackEndAsync;
            _lavaNode.OnTrackStart += OnTrackStartAsync;
        }

        public void SetGuildChannels(ulong guildId, ulong voiceChannelId, ulong textChannelId)
        {
            _voiceChannelIds[guildId] = voiceChannelId;
            _textChannelIds[guildId] = textChannelId;
        }

        public void ClearGuildChannels(ulong guildId)
        {
            _voiceChannelIds.TryRemove(guildId, out _);
            _textChannelIds.TryRemove(guildId, out _);
        }

        public void SetRepeat(ulong guildId, bool enabled) => _repeatEnabled[guildId] = enabled;

        private Task OnTrackStartAsync(TrackStartEventArg arg)
        {
            return LoggingService.LogInformationAsync(
                "VI-KA", 
                $"UPD Начат трек: {arg.Track.Title}");
        }

        private async Task OnTrackEndAsync(TrackEndEventArg args)
        {
            var sem = _guildLocks.GetOrAdd(args.GuildId, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync();

            try
            {
                await LoggingService.LogInformationAsync(
                    "VI-KA",
                    $"UPD Завершён трек: {args.Track.Title} | Причина: {args.Reason}");

                var player = await _lavaNode.TryGetPlayerAsync(args.GuildId);
                if (player is null || !player.State.IsConnected)
                    return;

                if (args.Reason == TrackEndReason.Load_Failed)
                {
                    if (player.GetQueue().TryDequeue(out var nextAfterFail) && nextAfterFail != null)
                    {
                        await player.PlayAsync(_lavaNode, nextAfterFail, false);
                        await SendNowPlayingAsync(args.GuildId, nextAfterFail);
                    }
                    return;
                }

                if (args.Reason != TrackEndReason.Finished)
                    return;

                if (!HasNonBotUsersInVoice(args.GuildId))
                {
                    await LeaveAndCleanupAsync(args.GuildId);
                    return;
                }

                if (_repeatEnabled.TryGetValue(args.GuildId, out var rep) && rep)
                {
                    await player.PlayAsync(_lavaNode, args.Track, false);
                    await SendNowPlayingAsync(args.GuildId, args.Track);
                    return;
                }

                if (!player.GetQueue().TryDequeue(out var queueable) || queueable is null)
                    return;

                await player.PlayAsync(_lavaNode, queueable, false);
                await SendNowPlayingAsync(args.GuildId, queueable);
            }
            catch (Exception ex)
            {
                await LoggingService.LogInformationAsync(
                    "VI-KA",
                    $"ERR OnTrackEndAsync g={args.GuildId}: {ex}");
            }
            finally
            {
                sem.Release();
            }
        }

        private bool HasNonBotUsersInVoice(ulong guildId)
        {
            if (!_voiceChannelIds.TryGetValue(guildId, out var vcId))
                return true;

            var guild = _client.GetGuild(guildId);
            var vc = guild?.GetVoiceChannel(vcId);

            if (vc is null)
                return true;

            return vc.ConnectedUsers.Any(u => !u.IsBot);
        }

        private async Task LeaveAndCleanupAsync(ulong guildId)
        {
            try
            {
                if (_voiceChannelIds.TryGetValue(guildId, out var vcId))
                {
                    var guild = _client.GetGuild(guildId);
                    var vc = guild?.GetVoiceChannel(vcId);
                    if (vc is not null)
                        await _lavaNode.LeaveAsync(vc);
                }
            }
            catch (Exception ex)
            {
                await LoggingService.LogInformationAsync(
                    "VI-KA",
                    $"WRN LF g={guildId}: {ex.Message}");
            }
            finally
            {
                ClearGuildChannels(guildId);
            }
        }

        private async Task SendNowPlayingAsync(ulong guildId, LavaTrack track)
        {
            var key = !string.IsNullOrWhiteSpace(track.Id) ? track.Id : (track.Url ?? track.Title ?? "track");
            var now = DateTimeOffset.UtcNow;

            if (_ddCheck.TryGetValue(guildId, out var last))
            {
                if (last.key == key && (now - last.at) < TimeSpan.FromSeconds(2))
                    return;
            }

            _ddCheck[guildId] = (key, now);

            if (!_textChannelIds.TryGetValue(guildId, out var tcId))
                return;

            if (_client.GetChannel(tcId) is not ITextChannel textChannel)
                return;

            await textChannel.SendMessageAsync(embed: await EmbedHandler.CreateMusicEmbed(
                "sbln muzik🎸🎧",
                $"👺 **Ща Играет:** [{track.Title}]({track.Url})\n" +
                $"**👤 Автор:** {track.Author}\n" +
                $"**⏳ Длительность:** {FormatTime(track.Duration)}\n",
                Color.Purple));
        }


        private Task OnStatsAsync(StatsEventArg arg)
        {
            lock (_statsLock)
            {
                _lastStats = arg;
                _lastStatsAtUtc = DateTimeOffset.UtcNow;
            }
            return Task.CompletedTask;
        }

        private Task OnWebSocketClosedAsync(WebSocketClosedEventArg arg)
        {
            Task task = LoggingService.LogInformationAsync(
                "VI-KA",
                $"WS CLOSED: {JsonSerializer.Serialize(arg)}");
            return task;
        }

        public void Dispose()
        {
            _lavaNode.OnWebSocketClosed -= OnWebSocketClosedAsync;
            _lavaNode.OnStats -= OnStatsAsync;
            _lavaNode.OnTrackEnd -= OnTrackEndAsync;
            _lavaNode.OnTrackStart -= OnTrackStartAsync;
        }

        public bool ToggleRepeat(ulong guildId)
        {
            var enabled = _repeatEnabled.TryGetValue(guildId, out var cur) && cur;
            var next = !enabled;
            _repeatEnabled[guildId] = next;
            return next;
        }

        public string? GetLastStatsJson(int maxChars = 1800)
        {
            StatsEventArg? statsCopy;
            DateTimeOffset at;

            lock (_statsLock)
            {
                statsCopy = _lastStats;
                at = _lastStatsAtUtc;
            }

            if (statsCopy is null) return null;

            var json = JsonSerializer.Serialize(statsCopy, new JsonSerializerOptions { WriteIndented = true });

            if (json.Length > maxChars)
                json = json.Substring(0, maxChars) + "\n...YO...";

            var age = DateTimeOffset.UtcNow - at;
            return $"Zpoint: {age.TotalSeconds:0}s\n{json}";
        }

        public Embed? GetStatsEmbed()
        {
            StatsEventArg? statsCopy;
            DateTimeOffset at;

            lock (_statsLock)
            {
                statsCopy = _lastStats;
                at = _lastStatsAtUtc;
            }

            if (statsCopy is null)
                return null;

            var s = statsCopy.Value;
            var age = DateTimeOffset.UtcNow - at;

            var uptime = TimeSpan.FromMilliseconds(s.Uptime);

            var embed = new EmbedBuilder()
                .WithTitle("🎛️ LavaStats")
                .WithColor(Color.Purple)
                .WithDescription($"Последнее обновление: {age.TotalSeconds:0}s назад")
                .AddField("🧠 CPU",
                    $"Cores: `{s.Cpu.Cores}`\n" +
                    $"System Load: `{s.Cpu.SystemLoad:P1}`\n" +
                    $"Lavalink Load: `{s.Cpu.LavalinkLoad:P1}`",
                    true)
                .AddField("💾 Память",
                    $"Used: `{s.Memory.Used / 1024 / 1024} MB`\n" +
                    $"Free: `{s.Memory.Free / 1024 / 1024} MB`\n" +
                    $"Allocated: `{s.Memory.Allocated / 1024 / 1024} MB`\n" +
                    $"Reservable: `{s.Memory.Reservable / 1024 / 1024} MB`",
                    true)
                .AddField("🎧 Плееры",
                    $"Total: `{s.Players}`\n" +
                    $"Playing: `{s.PlayingPlayers}`",
                    true)
                .AddField("🕓 Аптайм",
                    $"{uptime:hh\\:mm\\:ss}",
                    true)
                .AddField("📦 Фреймы",
                    $"Sent: `{s.Frames.Sent}`\n" +
                    $"Nulled: `{s.Frames.Nulled}`\n" +
                    $"Deficit: `{s.Frames.Deficit}`",
                    true)
                .WithFooter("sbln muzik🎸🎧 & sbln статистикс🔭")
                .WithCurrentTimestamp()
                .Build();

            return embed;
        }
    }
}
