using Discord;
using Discord.WebSocket;
using sblngavnav5X.Core;
using sblngavnav5X.Services;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Victoria;
using Victoria.Enums;
using Victoria.Rest.Search;
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
        private readonly ConcurrentDictionary<ulong, ulong> _lastNowPlayingId = new();
        private readonly ConcurrentDictionary<ulong, (string trackKey, DateTimeOffset at)> _lastNowPlayingTrack = new();
        private readonly ConcurrentDictionary<ulong, DateTime> _lastClickTime = new();
        private readonly ConcurrentDictionary<ulong, PaginatorState> _paginatorsByMessageId = new();
        private readonly ConcurrentDictionary<ulong, QueueInsertState> _queueInsertByMessageId = new();
        private readonly ConcurrentDictionary<ulong, SearchPickState> _searchPicksByMessageId = new();

        private readonly object _statsLock = new();
        private StatsEventArg? _lastStats;
        private DateTimeOffset _lastStatsAtUtc;

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

            _client.ReactionAdded += OnReactionAddedAsync;
            _client.UserVoiceStateUpdated += OnUserVoiceStateUpdatedAsync;
        }

        public void SetGuildChannels(ulong guildId, ulong voiceChannelId, ulong textChannelId)
        {
            _voiceChannelIds[guildId] = voiceChannelId;
            _textChannelIds[guildId] = textChannelId;
        }

        public bool TryGetTrackedVoiceChannelId(ulong guildId, out ulong voiceChannelId) =>
            _voiceChannelIds.TryGetValue(guildId, out voiceChannelId);

        public void ClearGuildChannels(ulong guildId)
        {
            _voiceChannelIds.TryRemove(guildId, out _);
            _textChannelIds.TryRemove(guildId, out _);
        }

        public void SetRepeat(ulong guildId, bool enabled) => _repeatEnabled[guildId] = enabled;

        public bool ToggleRepeat(ulong guildId)
        {
            var enabled = _repeatEnabled.TryGetValue(guildId, out var cur) && cur;
            var next = !enabled;
            _repeatEnabled[guildId] = next;
            return next;
        }

        public bool IsRepeatEnabled(ulong guildId) =>
            _repeatEnabled.TryGetValue(guildId, out var enabled) && enabled;

        public async Task RunInGuildLockAsync(ulong guildId, Func<Task> action)
        {
            var sem = _guildLocks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync();
            try { await action(); }
            finally { sem.Release(); }
        }

        public async Task AttachQueueInsertControlAsync(ulong guildId, IUserMessage message, ulong requestedByUserId, LavaTrack track)
        {
            try { await message.AddReactionAsync(new Emoji("🔼")); } catch { }

            _queueInsertByMessageId[message.Id] = new QueueInsertState(
                GuildId: guildId,
                MessageId: message.Id,
                ChannelId: message.Channel.Id,
                RequestedByUserId: requestedByUserId,
                TrackKey: BuildTrackKey(track),
                CreatedAtUtc: DateTimeOffset.UtcNow
            );
        }

        private Task OnTrackStartAsync(TrackStartEventArg arg)
        {
            FireAndForget(async () =>
            {
                await SendNowPlayingAsync(arg.GuildId, arg.Track);
            }, $"NowPlaying g={arg.GuildId}");

            return Task.CompletedTask;
        }

        private async Task OnTrackEndAsync(TrackEndEventArg args)
        {
            await RunInGuildLockAsync(args.GuildId, async () =>
            {
                try
                {
                    var player = await _lavaNode.TryGetPlayerAsync(args.GuildId);
                    if (player is null || !player.State.IsConnected)
                        return;

                    if (!HasNonBotUsersInVoice(args.GuildId))
                    {
                        await LeaveAndCleanupAsync(args.GuildId);
                        return;
                    }

                    if (args.Reason == TrackEndReason.Load_Failed)
                    {
                        if (player.GetQueue().TryDequeue(out var nextAfterFail) && nextAfterFail != null)
                            await player.PlayAsync(_lavaNode, nextAfterFail, false);
                        return;
                    }

                    if (args.Reason != TrackEndReason.Finished)
                        return;

                    if (IsRepeatEnabled(args.GuildId))
                    {
                        await player.PlayAsync(_lavaNode, args.Track, false);
                        return;
                    }

                    if (!player.GetQueue().TryDequeue(out var next) || next is null)
                        return;

                    await player.PlayAsync(_lavaNode, next, false);
                }
                catch (Exception ex)
                {
                    await LoggingService.LogInformationAsync("VI-KA", $"ERR OnTrackEndAsync g={args.GuildId}: {ex}");
                }
            });
        }

        private async Task OnUserVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after)
        {
            try
            {
                if (before.VoiceChannel?.Guild is null && after.VoiceChannel?.Guild is null)
                    return;

                var guild = before.VoiceChannel?.Guild ?? after.VoiceChannel?.Guild;
                if (guild is null) return;

                var guildId = guild.Id;

                if (!_voiceChannelIds.TryGetValue(guildId, out var trackedVcId))
                    return;

                var vc = guild.GetVoiceChannel(trackedVcId);
                if (vc is null) return;

                if (!vc.ConnectedUsers.Any(u => !u.IsBot))
                    await ForceLeaveAsync(guildId);
            }
            catch (Exception ex)
            {
                await LoggingService.LogInformationAsync("VI-KA", $"WRN VoiceStateUpdated: {ex}");
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

        public async Task ForceLeaveAsync(ulong guildId)
        {
            await RunInGuildLockAsync(guildId, async () =>
            {
                await LeaveAndCleanupAsync(guildId);
            });
        }

        private async Task LeaveAndCleanupAsync(ulong guildId)
        {
            try
            {
                foreach (var kv in _paginatorsByMessageId.ToArray())
                {
                    if (kv.Value.GuildId == guildId)
                        _paginatorsByMessageId.TryRemove(kv.Key, out _);
                }

                foreach (var kv in _queueInsertByMessageId.ToArray())
                {
                    if (kv.Value.GuildId == guildId)
                        _queueInsertByMessageId.TryRemove(kv.Key, out _);
                }

                _repeatEnabled[guildId] = false;

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
                await LoggingService.LogInformationAsync("VI-KA", $"WRN LF g={guildId}: {ex.Message}");
            }
            finally
            {
                ClearGuildChannels(guildId);
                _lastNowPlayingId.TryRemove(guildId, out _);
                _lastNowPlayingTrack.TryRemove(guildId, out _);
            }
        }

        private async Task SendNowPlayingAsync(ulong guildId, LavaTrack track)
        {
            if (!_textChannelIds.TryGetValue(guildId, out var tcId))
                return;

            if (_client.GetChannel(tcId) is not ITextChannel textChannel)
                return;

            var key = BuildTrackKey(track);
            if (_lastNowPlayingTrack.TryGetValue(guildId, out var prev) &&
                prev.trackKey == key &&
                (DateTimeOffset.UtcNow - prev.at) < TimeSpan.FromSeconds(1.2))
            {
                return;
            }

            _lastNowPlayingTrack[guildId] = (key, DateTimeOffset.UtcNow);

            var loopLine = IsRepeatEnabled(guildId) ? "\n🔁 **Луп включен**" : "";

            var embed = await EmbedHandler.CreateCustomMusicEmbed(
                "sbln muzik🎸🎧",
                $"**👺 Трек:** [{track.Title}]({track.Url})\n" +
                $"**👤 Автор:** {track.Author}\n" +
                $"**⏳ Длительность:** {FormatTime(track.Duration)}\n" +
                $"{loopLine}", "▶/🔁 - скип/луп трека",
                Color.Purple);

            var msg = await textChannel.SendMessageAsync(embed: embed);

            _lastNowPlayingId[guildId] = msg.Id;

            try
            {
                await msg.AddReactionAsync(new Emoji("▶"));
                await msg.AddReactionAsync(new Emoji("🔁"));
            }
            catch { }
        }

        public async Task SendQueuePagedAsync(ulong guildId, ITextChannel channel, ulong requestedByUserId)
        {
            await RunInGuildLockAsync(guildId, async () =>
            {
                var player = await _lavaNode.TryGetPlayerAsync(guildId);
                if (player is null || !player.State.IsConnected || player.Track is null)
                {
                    var empty = await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, лист", "очередь пуста");
                    await channel.SendMessageAsync(embed: empty);
                    return;
                }

                var queueList = player.GetQueue().ToList();
                var queueCount = queueList.Count;
                var queueDuration = TimeSpan.FromMilliseconds(queueList.Sum(t => t.Duration.TotalMilliseconds));

                var pages = BuildQueuePages(player, queueList, queueCount, queueDuration);

                var msg = await channel.SendMessageAsync(embed: BuildPagedEmbed(
                    title: "sbln muzik🎸🎧, лист",
                    pageLines: pages[0],
                    pageIndex: 0,
                    pageCount: pages.Count,
                    footer: pages.Count > 1 ? "⬅️/➡️ переключение страниц" : ""));

                if (pages.Count <= 1) return;

                var state = new PaginatorState(
                    GuildId: guildId,
                    ChannelId: channel.Id,
                    MessageId: msg.Id,
                    RequestedByUserId: requestedByUserId,
                    Pages: pages,
                    PageIndex: 0,
                    CreatedAtUtc: DateTimeOffset.UtcNow);

                _paginatorsByMessageId[msg.Id] = state;

                await msg.AddReactionAsync(new Emoji("⬅️"));
                await msg.AddReactionAsync(new Emoji("➡️"));
            });
        }

        private async Task OnReactionAddedAsync(
            Cacheable<IUserMessage, ulong> message,
            Cacheable<IMessageChannel, ulong> channel,
            SocketReaction reaction)
        {
            if (reaction.UserId == _client.CurrentUser.Id)
                return;

            var now = DateTime.UtcNow;
            if (_lastClickTime.TryGetValue(reaction.UserId, out var prevTime))
            {
                if ((now - prevTime).TotalMilliseconds < 700)
                    return;
            }
            _lastClickTime[reaction.UserId] = now;

            var ch = await channel.GetOrDownloadAsync();
            if (ch is not SocketGuildChannel guildChannel)
                return;

            var guildId = guildChannel.Guild.Id;

            if (_queueInsertByMessageId.TryGetValue(reaction.MessageId, out var ins))
            {
                await HandleQueueInsertReactionAsync(guildId, guildChannel, message, reaction, ins);
                return;
            }

            if (_searchPicksByMessageId.TryGetValue(reaction.MessageId, out var pickState))
            {
                await HandleSearchPickReactionAsync(guildChannel, message, reaction, pickState);
                return;
            }

            if (_lastNowPlayingId.TryGetValue(guildId, out var lastNpId) && reaction.MessageId == lastNpId)
            {
                await HandleNowPlayingReactionAsync(guildChannel, message, reaction);
                return;
            }

            if (_paginatorsByMessageId.TryGetValue(reaction.MessageId, out var state))
            {
                await HandlePaginatorReactionAsync(guildChannel, message, reaction, state);
                return;
            }
        }

        private async Task HandleQueueInsertReactionAsync(
            ulong guildId,
            SocketGuildChannel guildChannel,
            Cacheable<IUserMessage, ulong> message,
            SocketReaction reaction,
            QueueInsertState state)
        {
            if (reaction.Emote.Name != "🔼")
                return;

            if ((DateTimeOffset.UtcNow - state.CreatedAtUtc) > TimeSpan.FromMinutes(2))
            {
                _queueInsertByMessageId.TryRemove(state.MessageId, out _);
                return;
            }

            if (reaction.UserId != state.RequestedByUserId)
                return;

            var msg = await message.GetOrDownloadAsync();
            var user = guildChannel.Guild.GetUser(reaction.UserId);
            if (user is not null)
            {
                try { await msg.RemoveReactionAsync(reaction.Emote, user); } catch { }
            }

            await RunInGuildLockAsync(guildId, async () =>
            {
                var player = await _lavaNode.TryGetPlayerAsync(guildId);
                if (player is null || !player.State.IsConnected)
                    return;

                var queue = player.GetQueue();
                var list = queue.ToList();

                var idx = list.FindIndex(t => BuildTrackKey(t) == state.TrackKey);
                if (idx < 0)
                    return;

                var picked = list[idx];
                list.RemoveAt(idx);

                queue.Clear();
                queue.Enqueue(picked);
                foreach (var t in list)
                    queue.Enqueue(t);

                try
                {
                    var ok = await EmbedHandler.CreateMusicEmbed(
                        "sbln muzik🎸🎧, скип+",
                        $"💎 Трек [{picked.Title}]({picked.Url}) добавлен в начало листа",
                        Color.Green);
                    await msg.ModifyAsync(m => m.Embed = ok);

                    try { await msg.RemoveAllReactionsAsync(); } catch {}
                }
                catch { }
            });

            _queueInsertByMessageId.TryRemove(state.MessageId, out _);
        }

        private async Task HandleNowPlayingReactionAsync(SocketGuildChannel guildChannel, Cacheable<IUserMessage, ulong> message, SocketReaction reaction)
        {
            var guildId = guildChannel.Guild.Id;

            var msg = await message.GetOrDownloadAsync();
            var user = guildChannel.Guild.GetUser(reaction.UserId);

            if (user is not null)
            {
                try { await msg.RemoveReactionAsync(reaction.Emote, user); } catch { }
            }

            if (reaction.Emote.Name == "▶")
            {
                await RunInGuildLockAsync(guildId, async () =>
                {
                    var player = await _lavaNode.TryGetPlayerAsync(guildId);
                    if (player is null || !player.State.IsConnected)
                        return;

                    if (player.GetQueue().TryDequeue(out var next) && next != null)
                        await player.PlayAsync(_lavaNode, next, false);
                });

                return;
            }

            if (reaction.Emote.Name == "🔁")
            {
                ToggleRepeat(guildId);

                var player = await _lavaNode.TryGetPlayerAsync(guildId);
                if (player?.Track is null) return;

                var loopLine = IsRepeatEnabled(guildId) ? "\n🔁 **Луп включен**" : "";

                var embed = await EmbedHandler.CreateMusicEmbed(
                    "sbln muzik🎸🎧",
                    $"**👺 Трек:** [{player.Track.Title}]({player.Track.Url})\n" +
                    $"**👤 Автор:** {player.Track.Author}\n" +
                    $"**⏳ Длительность:** {FormatTime(player.Track.Duration)}\n" +
                    $"{loopLine}",
                    Color.Purple);

                await msg.ModifyAsync(m => m.Embed = embed);
                return;
            }
        }

        private async Task HandleSearchPickReactionAsync(
            SocketGuildChannel guildChannel,
            Cacheable<IUserMessage, ulong> message,
            SocketReaction reaction,
            SearchPickState state)
        {
            if ((DateTimeOffset.UtcNow - state.CreatedAtUtc) > TimeSpan.FromMinutes(2))
            {
                _searchPicksByMessageId.TryRemove(state.MessageId, out _);
                return;
            }

            if (reaction.UserId != state.RequestedByUserId)
                return;

            int idx =
                reaction.Emote.Name == "1️⃣" ? 0 :
                reaction.Emote.Name == "2️⃣" ? 1 :
                reaction.Emote.Name == "3️⃣" ? 2 : -1;

            if (idx < 0 || idx >= state.Picks.Count)
                return;

            var msg = await message.GetOrDownloadAsync();
            var user = guildChannel.Guild.GetUser(reaction.UserId);

            if (user is not null)
            {
                try { await msg.RemoveReactionAsync(reaction.Emote, user); } catch { }
            }

            var picked = state.Picks[idx];

            await RunInGuildLockAsync(state.GuildId, async () =>
            {
                var player = await _lavaNode.TryGetPlayerAsync(state.GuildId);
                if (player is null || !player.State.IsConnected)
                    return;

                var q = player.GetQueue();

                if (player.Track is null && !q.Any())
                {
                    await player.PlayAsync(_lavaNode, picked, false);
                }
                else
                {
                    q.Enqueue(picked);
                }
            });

            try
            {
                var ok = await EmbedHandler.CreateMusicEmbed(
                    "sbln muzik🎸🎧, играй+",
                    $"💎 Выбран трек: [{picked.Title}]({picked.Url})",
                    Color.Green);

                await msg.ModifyAsync(m => m.Embed = ok);
                try { await msg.RemoveAllReactionsAsync(); } catch { }
            }
            catch { }

            _searchPicksByMessageId.TryRemove(state.MessageId, out _);
        }

        private async Task HandlePaginatorReactionAsync(
            SocketGuildChannel guildChannel,
            Cacheable<IUserMessage, ulong> message,
            SocketReaction reaction,
            PaginatorState state)
        {
            if ((DateTimeOffset.UtcNow - state.CreatedAtUtc) > TimeSpan.FromMinutes(3))
            {
                _paginatorsByMessageId.TryRemove(state.MessageId, out _);
                return;
            }

            if (reaction.UserId != state.RequestedByUserId)
                return;

            var msg = await message.GetOrDownloadAsync();
            var user = guildChannel.Guild.GetUser(reaction.UserId);

            if (user is not null)
            {
                try { await msg.RemoveReactionAsync(reaction.Emote, user); } catch { }
            }

            var pageIndex = state.PageIndex;

            if (reaction.Emote.Name == "⬅️")
                pageIndex = Math.Max(0, pageIndex - 1);
            else if (reaction.Emote.Name == "➡️")
                pageIndex = Math.Min(state.Pages.Count - 1, pageIndex + 1);
            else
                return;

            if (pageIndex == state.PageIndex)
                return;

            state = state with { PageIndex = pageIndex };
            _paginatorsByMessageId[state.MessageId] = state;

            var embed = BuildPagedEmbed(
                title: "sbln muzik🎸🎧, лист",
                pageLines: state.Pages[pageIndex],
                pageIndex: pageIndex,
                pageCount: state.Pages.Count,
                footer: "⬅️/➡️ переключение страниц");

            await msg.ModifyAsync(m => m.Embed = embed);
        }

        public async Task<bool> TrySendSmartSearchPicksAsync(
            ulong guildId,
            ITextChannel channel,
            ulong requestedByUserId,
            string normalizedQuery)
        {
            if (!normalizedQuery.StartsWith("ytsearch:", StringComparison.OrdinalIgnoreCase))
                return false;

            var baseText = normalizedQuery["ytsearch:".Length..].Trim();
            if (string.IsNullOrWhiteSpace(baseText))
                return false;

            var variants = BuildSmartVariants(baseText);

            var picks = new List<LavaTrack>(capacity: 3);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in variants)
            {
                if (picks.Count >= 3) break;

                var q = "ytsearch:" + v;

                SearchResponse resp;
                try
                {
                    resp = await _lavaNode.LoadTrackAsync(q);
                }
                catch
                {
                    continue;
                }

                var t = await SafeFirstTrackAsync("ytsearch:" + v);
                if (t is null)
                    continue;

                var key = BuildTrackKey(t);
                if (seen.Add(key))
                    picks.Add(t);
            }

            if (picks.Count == 0)
                return false;

            var lines = new List<string>();
            for (int i = 0; i < picks.Count; i++)
            {
                var num = i == 0 ? "1️⃣" : i == 1 ? "2️⃣" : "3️⃣";
                var t = picks[i];
                lines.Add($"{num} [{Truncate(t.Title ?? "track", 80)}]({t.Url}) - {Truncate(t.Author ?? "unknown", 40)} - {FormatTime(t.Duration)}");
            }

            var desc =
                "ты помоему перепутал, может это?\n\n" +
                string.Join("\n", lines);

            var embed = await EmbedHandler.CreateCustomMusicEmbed(
                "sbln muzik🎸🎧, играй+",
                desc,
                "1️⃣/2️⃣/3️⃣ - выбрать",
                Color.Purple);

            var msg = await channel.SendMessageAsync(embed: embed);

            try
            {
                if (picks.Count >= 1) await msg.AddReactionAsync(new Emoji("1️⃣"));
                if (picks.Count >= 2) await msg.AddReactionAsync(new Emoji("2️⃣"));
                if (picks.Count >= 3) await msg.AddReactionAsync(new Emoji("3️⃣"));
            }
            catch {}

            _searchPicksByMessageId[msg.Id] = new SearchPickState(
                GuildId: guildId,
                ChannelId: channel.Id,
                MessageId: msg.Id,
                RequestedByUserId: requestedByUserId,
                Picks: picks,
                CreatedAtUtc: DateTimeOffset.UtcNow);

            return true;
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
            return LoggingService.LogInformationAsync("VI-KA", $"WS CLOSED: {JsonSerializer.Serialize(arg)}");
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

            return new EmbedBuilder()
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
                .AddField("🕓 Аптайм", $"{uptime:hh\\:mm\\:ss}", true)
                .AddField("📦 Фреймы",
                    $"Sent: `{s.Frames.Sent}`\n" +
                    $"Nulled: `{s.Frames.Nulled}`\n" +
                    $"Deficit: `{s.Frames.Deficit}`",
                    true)
                .WithFooter("sbln muzik🎸🎧 & sbln статистикс🔭")
                .WithCurrentTimestamp()
                .Build();
        }

        public void Dispose()
        {
            _lavaNode.OnWebSocketClosed -= OnWebSocketClosedAsync;
            _lavaNode.OnStats -= OnStatsAsync;
            _lavaNode.OnTrackEnd -= OnTrackEndAsync;
            _lavaNode.OnTrackStart -= OnTrackStartAsync;

            _client.ReactionAdded -= OnReactionAddedAsync;
            _client.UserVoiceStateUpdated -= OnUserVoiceStateUpdatedAsync;

            foreach (var kv in _guildLocks)
            {
                try { kv.Value.Dispose(); } catch { }
            }
        }

        private static string BuildTrackKey(LavaTrack t)
        {
            if (!string.IsNullOrWhiteSpace(t.Url))
                return "u:" + t.Url;
            if (!string.IsNullOrWhiteSpace(t.Id))
                return "i:" + t.Id;
            return "t:" + (t.Title ?? "") + "|" + (t.Author ?? "");
        }

        private static string FormatTime(TimeSpan time)
        {
            if (time.TotalDays >= 1)
                return $"{(int)time.TotalDays}d {time:hh\\:mm\\:ss}";
            return time.ToString(@"hh\:mm\:ss");
        }

        private List<string> BuildQueuePages(LavaPlayer<LavaTrack> player, List<LavaTrack> queueList, int queueCount, TimeSpan queueDuration)
        {
            const int pageSize = 10;

            var current = player.Track?.Position ?? TimeSpan.Zero;
            var total = player.Track?.Duration ?? TimeSpan.Zero;
            var remaining = total > current ? (total - current) : TimeSpan.Zero;

            var header = new StringBuilder();
            header.AppendLine($"👺 **Трек:** [{player.Track!.Title}]({player.Track.Url})");
            header.AppendLine($"👤 **Автор:** {player.Track.Author}");
            header.AppendLine($"📦 **В очереди:** {queueCount}");
            header.AppendLine($"🕓 **Длина очереди:** {FormatTime(queueDuration)}");
            header.AppendLine($"⏳ **До конца трека осталось:** {FormatTime(remaining)}");
            header.AppendLine($"🕰️ **{BuildProgressBar(current, total)}**");
            header.AppendLine();

            if (queueList.Count == 0)
                return new List<string> { header + "*больше в очереди ничего нет*" };

            var lines = new List<string> { "📜 **Дальше будет:**" };
            for (int i = 0; i < queueList.Count; i++)
            {
                var t = queueList[i];
                var title = Truncate(t.Title ?? "track", 70);
                lines.Add($"{i + 1}. [{title}]({t.Url}) - {FormatTime(t.Duration)}");
            }

            var pages = new List<string>();
            for (int i = 0; i < lines.Count; i += pageSize)
            {
                var chunk = lines.Skip(i).Take(pageSize);
                pages.Add(header + string.Join("\n", chunk));
            }

            return pages;
        }

        private static string BuildProgressBar(TimeSpan current, TimeSpan total, int size = 15)
        {
            if (total.TotalSeconds <= 0)
                return "йоу?";

            double progress = current.TotalSeconds / total.TotalSeconds;
            progress = Math.Clamp(progress, 0, 1);

            int position = (int)(progress * size);
            if (position >= size) position = size - 1;

            var bar = new StringBuilder("[");
            for (int i = 0; i < size; i++)
                bar.Append(i == position ? "🔴" : "▬");
            bar.Append("]");

            return bar.ToString();
        }

        private static Embed BuildPagedEmbed(string title, string pageLines, int pageIndex, int pageCount, string footer)
        {
            var fullTitle = $"{title} ({pageIndex + 1}/{pageCount})";

            var b = new EmbedBuilder()
                .WithTitle(fullTitle)
                .WithColor(Color.Purple)
                .WithDescription(pageLines.Length > 3900 ? pageLines.Substring(0, 3900) + "\n…" : pageLines)
                .WithCurrentTimestamp();

            if (!string.IsNullOrWhiteSpace(footer))
                b.WithFooter(footer);
            else
                b.WithFooter("powered by AudioSeven");

            return b.Build();
        }

        private static void FireAndForget(Func<Task> taskFactory, string tag)
        {
            _ = Task.Run(async () =>
            {
                try { await taskFactory(); }
                catch (Exception ex)
                {
                    await LoggingService.LogInformationAsync("VI-KA", $"WRN FireAndForget {tag}: {ex.Message}");
                }
            });
        }

        private async Task<LavaTrack?> SafeFirstTrackAsync(string query)
        {
            try
            {
                var resp = await _lavaNode.LoadTrackAsync(query);
                return resp?.Tracks?.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static List<string> BuildSmartVariants(string input)
        {
            var s = CollapseSpaces(input.Trim());
            var vars = new List<string>();
            AddIfNew(vars, SwapKeyboardRuEn(s));
            AddIfNew(vars, SwapKeyboardEnRu(s));
            AddIfNew(vars, TranslitRuToLat(s));

            foreach (var p in PrefixCuts(s, minLen: 4, maxLen: 10))
                AddIfNew(vars, p);

            return vars
                .Select(CollapseSpaces)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(15)
                .ToList();
        }

        private static IEnumerable<string> PrefixCuts(string s, int minLen, int maxLen)
        {
            s = s.Trim();
            var len = s.Length;
            var start = Math.Min(maxLen, len);
            for (int k = start; k >= minLen; k--)
                yield return s.Substring(0, k);
        }

        private static void AddIfNew(List<string> list, string value)
        {
            value = CollapseSpaces(value);
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
                list.Add(value);
        }

        private static string CollapseSpaces(string s)
        {
            while (s.Contains("  ", StringComparison.Ordinal))
                s = s.Replace("  ", " ");
            return s.Trim();
        }

        private static readonly Dictionary<char, char> RuToEn = new()
        {
            ['й'] = 'q',
            ['ц'] = 'w',
            ['у'] = 'e',
            ['к'] = 'r',
            ['е'] = 't',
            ['н'] = 'y',
            ['г'] = 'u',
            ['ш'] = 'i',
            ['щ'] = 'o',
            ['з'] = 'p',
            ['х'] = '[',
            ['ъ'] = ']',
            ['ф'] = 'a',
            ['ы'] = 's',
            ['в'] = 'd',
            ['а'] = 'f',
            ['п'] = 'g',
            ['р'] = 'h',
            ['о'] = 'j',
            ['л'] = 'k',
            ['д'] = 'l',
            ['ж'] = ';',
            ['э'] = '\'',
            ['я'] = 'z',
            ['ч'] = 'x',
            ['с'] = 'c',
            ['м'] = 'v',
            ['и'] = 'b',
            ['т'] = 'n',
            ['ь'] = 'm',
            ['б'] = ',',
            ['ю'] = '.'
        };

        private static readonly Dictionary<char, char> EnToRu = RuToEn.ToDictionary(kv => kv.Value, kv => kv.Key);

        private static string SwapKeyboardRuEn(string s) => MapChars(s, RuToEn);
        private static string SwapKeyboardEnRu(string s) => MapChars(s, EnToRu);

        private static string MapChars(string s, Dictionary<char, char> map)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                var low = char.ToLowerInvariant(ch);
                if (map.TryGetValue(low, out var repl))
                {
                    sb.Append(char.IsUpper(ch) ? char.ToUpperInvariant(repl) : repl);
                }
                else
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }

        private static string TranslitRuToLat(string s)
        {
            var sb = new StringBuilder(s.Length * 2);
            foreach (var ch in s)
            {
                var c = char.ToLowerInvariant(ch);
                sb.Append(c switch
                {
                    'а' => "a",
                    'б' => "b",
                    'в' => "v",
                    'г' => "g",
                    'д' => "d",
                    'е' => "e",
                    'ж' => "zh",
                    'з' => "z",
                    'и' => "i",
                    'й' => "y",
                    'к' => "k",
                    'л' => "l",
                    'м' => "m",
                    'н' => "n",
                    'о' => "o",
                    'п' => "p",
                    'р' => "r",
                    'с' => "s",
                    'т' => "t",
                    'у' => "u",
                    'ф' => "f",
                    'х' => "h",
                    'ц' => "ts",
                    'ч' => "ch",
                    'ш' => "sh",
                    'щ' => "sch",
                    'ы' => "y",
                    'э' => "e",
                    'ю' => "yu",
                    'я' => "ya",
                    'ь' => "",
                    'ъ' => "",
                    _ => ch.ToString()
                });
            }
            return sb.ToString();
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length <= max) return s;
            return s.Substring(0, Math.Max(0, max - 1)) + "…";
        }

        private sealed record PaginatorState(
            ulong GuildId,
            ulong ChannelId,
            ulong MessageId,
            ulong RequestedByUserId,
            List<string> Pages,
            int PageIndex,
            DateTimeOffset CreatedAtUtc);

        private sealed record QueueInsertState(
            ulong GuildId,
            ulong MessageId,
            ulong ChannelId,
            ulong RequestedByUserId,
            string TrackKey,
            DateTimeOffset CreatedAtUtc);

        private sealed record SearchPickState(
            ulong GuildId,
            ulong ChannelId,
            ulong MessageId,
            ulong RequestedByUserId,
            List<LavaTrack> Picks,
            DateTimeOffset CreatedAtUtc);
    }
}