using Discord;
using Discord.Commands;
using sblngavnav5X.Core;
using System.Runtime.InteropServices;
using Victoria;
using Victoria.Rest.Search;

namespace sblngavnav5X.Audio
{
    public sealed class AudioSeven(
        LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode,
        AudioSevenService audioService) : ModuleBase<SocketCommandContext>
    {
        public async Task JoinAsync()
        {
            if (!await EnsureUserInVoiceAsync(requireSameAsBot: false))
                return;

            var voiceState = (IVoiceState)Context.User;
            await lavaNode.JoinAsync(voiceState.VoiceChannel);

            if (Context.Channel is not ITextChannel tc)
                return;

            audioService.SetGuildChannels(
                voiceState.VoiceChannel.GuildId,
                voiceState.VoiceChannel.Id,
                tc.Id
            );
        }

        [Command("выйди")]
        [Alias("л")]
        public async Task LeaveAsync()
        {
            if (!await BotInVoice())
                return;

            await audioService.ForceLeaveAsync(Context.Guild.Id);
        }

        [Command("играй")]
        [Alias("и")]
        public async Task PlayAsync([Remainder] string searchQuery)
        {
            if (!await EnsureUserInVoiceAsync(requireSameAsBot: false))
                return;

            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                await ReplyAsync("напиши нормально запрос бро");
                return;
            }

            var guildId = Context.Guild.Id;

            var player = await lavaNode.TryGetPlayerAsync(guildId);
            if (player == null || !player.State.IsConnected)
            {
                await JoinAsync();
                player = await lavaNode.GetPlayerAsync(guildId);
            }

            var normalized = AudioQueryNormalizer.Normalize(searchQuery, out var index);

            try
            {
                SearchResponse? searchResponse = null;

                try
                {
                    searchResponse = await lavaNode.LoadTrackAsync(normalized);
                }
                catch{}

                var trackCount = searchResponse?.Tracks?.Count ?? 0;

                if (trackCount == 0)
                {
                    if (Context.Channel is ITextChannel tc)
                    {
                        var handled = await audioService.TrySendSmartSearchPicksAsync(
                            guildId: guildId,
                            channel: tc,
                            requestedByUserId: Context.User.Id,
                            normalizedQuery: normalized);

                        if (handled)
                            return;
                    }

                    var embedErr = await EmbedHandler.CreateErrorEmbed(
                        "sbln muzik🎸🎧, играй",
                        "ничего не нашлось по запросу...");
                    await ReplyAsync(embed: embedErr);
                    return;
                }

                await audioService.RunInGuildLockAsync(guildId, async () =>
                {
                    var queue = player.GetQueue();

                    var maxIdx = searchResponse.Tracks.Count - 1;
                    if (index < 0) index = 0;
                    if (index > maxIdx) index = maxIdx;

                    if (!queue.Any() && player.Track == null)
                        await PlayNow(searchResponse, player, index);
                    else
                        await QueueNow(searchResponse, player, index);
                });
            }
            catch
            {
                var embedErr = await EmbedHandler.CreateErrorEmbed(
                    "ненене👿",
                    "ошибка сервиса поиска, ты че написал...");
                await ReplyAsync(embed: embedErr);
            }
        }

        [Command("скип")]
        [Alias("ск")]
        public async Task SkipAsync([Optional] int? index)
        {
            if (!await EnsureUserInVoiceAsync(requireSameAsBot: true) || !await BotInVoice())
                return;

            var guildId = Context.Guild.Id;
            var player = await lavaNode.GetPlayerAsync(guildId);

            await audioService.RunInGuildLockAsync(guildId, async () =>
            {
                var queue = player.GetQueue();

                if (index.HasValue)
                {
                    int n = index.Value;

                    if (n < 1 || queue.Count < n)
                    {
                        var err = await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, скип", $"в очереди нет трека с номером {index}");
                        await ReplyAsync(embed: err);
                        return;
                    }

                    for (int i = 1; i < n; i++)
                        queue.TryDequeue(out _);

                    queue.TryDequeue(out var nextTrack);
                    if (nextTrack is null)
                    {
                        var err = await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, скип", $"в очереди нет трека с номером {index}");
                        await ReplyAsync(embed: err);
                        return;
                    }

                    var embed = await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧, скип",
                        $"👀 Пропустили говно: [{player.Track?.Title}]({player.Track?.Url})\n🦻 Вместо это теперь: {nextTrack.Title}",
                        Color.Green);

                    await ReplyAsync(embed: embed);
                    await player.PlayAsync(lavaNode, nextTrack, false);
                    return;
                }

                if (queue.TryDequeue(out var track) && track != null)
                {
                    var embed = await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧, скип",
                        $"👀 Пропустили говно: [{player.Track?.Title}]({player.Track?.Url})\n🦻 Вместо это теперь: [{track.Title}]({track.Url})",
                        Color.Green);

                    await ReplyAsync(embed: embed);
                    await player.PlayAsync(lavaNode, track, false);
                }
                else
                {
                    var err = await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, скип", "в очереди больше ничего нет =(");
                    await ReplyAsync(embed: err);
                }
            });
        }

        [Command("озвучь")]
        [Alias("ттс")]
        public async Task SpeakAsync([Remainder] string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                await ReplyAsync("напиши текст для озвучки");
                return;
            }

            var floweryQuery = $"ftts://{text.Trim()}";
            await PlayAsync(floweryQuery);
        }

        [Command("плейлист")]
        [Alias("лист")]
        public async Task QueueAsync()
        {
            if (!await EnsureUserInVoiceAsync(requireSameAsBot: true) || !await BotInVoice())
                return;

            await audioService.SendQueuePagedAsync(Context.Guild.Id, (ITextChannel)Context.Channel, Context.User.Id);
        }

        [Command("пауза")]
        [Alias("пз")]
        public async Task PauseAsync()
        {
            if (!await EnsureUserInVoiceAsync(requireSameAsBot: true) || !await BotInVoice())
                return;

            var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
            if (player?.Track is null)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, пауза", "так ничего не играет"));
                return;
            }

            if (player.IsPaused)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, пауза", "але, я уже на паузе"));
                return;
            }

            await player.PauseAsync(lavaNode);
            await ReplyAsync(embed: await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧, пауза",
                $"поставил на паузу --- [{player.Track.Title}]({player.Track.Url}) ⏸️",
                Color.Blue));
        }

        [Command("продолжи")]
        [Alias("прод")]
        public async Task ResumeAsync()
        {
            if (!await EnsureUserInVoiceAsync(requireSameAsBot: true) || !await BotInVoice())
                return;

            var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
            if (player?.Track is null)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, продолжи", "так ничего не играет"));
                return;
            }

            if (!player.IsPaused)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, продолжи", "я не на паузе!!"));
                return;
            }

            await player.ResumeAsync(lavaNode, player.Track);
            await ReplyAsync(embed: await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧, продолжи",
                $"продолжаю --- [{player.Track.Title}]({player.Track.Url}) ▶️",
                Color.Blue));
        }

        [Command("останови")]
        [Alias("стоп")]
        public async Task StopAsync()
        {
            if (!await EnsureUserInVoiceAsync(requireSameAsBot: true) || !await BotInVoice())
                return;

            var guildId = Context.Guild.Id;
            var player = await lavaNode.GetPlayerAsync(guildId);

            await audioService.RunInGuildLockAsync(guildId, async () =>
            {
                audioService.SetRepeat(guildId, false);
                player.GetQueue().Clear();

                try { await player.SeekAsync(lavaNode, player.Track?.Duration ?? TimeSpan.Zero); } catch { }

                await ReplyAsync(embed: await EmbedHandler.CreateMusicEmbed(
                    "sbln muzik🎸🎧, стоп",
                    "стопнулся и очистил плейлист ⛔",
                    Color.Blue));
            });
        }

        [Command("громкость")]
        [Alias("гр")]
        public async Task VolumeAsync(int volume)
        {
            if (!await EnsureUserInVoiceAsync(requireSameAsBot: true) || !await BotInVoice())
                return;

            if (volume > 500 || volume < 1)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, громкость", "только значения 1-500"));
                return;
            }

            var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
            if (player is null)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, громкость", "нет плеера.."));
                return;
            }

            await player.SetVolumeAsync(lavaNode, volume);
            await ReplyAsync(embed: await EmbedHandler.CreateMusicEmbed(
                "sbln muzik🎸🎧, громкость",
                $"**Громкость --- {volume} 📶**",
                Color.DarkMagenta));
        }

        [Command("залупа")]
        [Alias("луп")]
        public async Task LoopAsync()
        {
            if (!await BotInVoice())
                return;

            var enabled = audioService.ToggleRepeat(Context.Guild.Id);
            var text = enabled ? "🔁 **Луп вкл**" : "⛔ **Луп выкл**";
            await ReplyAsync(embed: await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧, луп", text, Color.DarkMagenta));
        }

        [Command("перейти")]
        [Alias("пр")]
        public async Task SeekAsync([Remainder] string timecode)
        {
            if (!await EnsureUserInVoiceAsync(requireSameAsBot: true) || !await BotInVoice())
                return;

            var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
            if (player?.Track is null)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, перейти", "так ничего не играет"));
                return;
            }

            if (!TryParseTimecode(timecode, out var ts))
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, перейти", "формат `мм:сс` или `чч:мм:сс`"));
                return;
            }

            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            if (ts > player.Track.Duration) ts = player.Track.Duration;

            await audioService.RunInGuildLockAsync(Context.Guild.Id, async () =>
            {
                await player.SeekAsync(lavaNode, ts);
            });

            await ReplyAsync(embed: await EmbedHandler.CreateMusicEmbed(
                "sbln muzik🎸🎧, перейти",
                $"⏩ Перемотал на **{ts:hh\\:mm\\:ss}**",
                Color.Blue));
        }

        [Command("лавастат")]
        public async Task LavaStat()
        {
            var embed = audioService.GetStatsEmbed();
            if (embed is null)
            {
                await ReplyAsync("Статы пока нет...");
                return;
            }

            await ReplyAsync(embed: embed);
        }

        private async Task PlayNow(SearchResponse searchResponse, LavaPlayer<LavaTrack> player, int index)
        {
            var tracks = searchResponse.Tracks as IReadOnlyList<LavaTrack> ?? searchResponse.Tracks.ToList();

            if (searchResponse.Type == SearchType.Playlist)
            {
                for (var i = index; i < tracks.Count; i++)
                {
                    var t = tracks[i];

                    if (i == index)
                        await player.PlayAsync(lavaNode, t, false);
                    else
                        player.GetQueue().Enqueue(t);
                }

                var playlistQEmbed = await EmbedHandler.CreateMusicEmbed(
                    "sbln muzik🎸🎧",
                    $"{searchResponse.Playlist.Name} --- добавлено в очередь 🤙",
                    Color.Orange);

                await Context.Channel.SendMessageAsync(embed: playlistQEmbed);
                return;
            }

            await player.PlayAsync(lavaNode, tracks[index], false);
        }

        private async Task QueueNow(SearchResponse searchResponse, LavaPlayer<LavaTrack> player, int index)
        {
            var tracks = searchResponse.Tracks as IReadOnlyList<LavaTrack> ?? searchResponse.Tracks.ToList();
            var queue = player.GetQueue();

            if (searchResponse.Type == SearchType.Playlist)
            {
                for (var i = index; i < tracks.Count; i++)
                    queue.Enqueue(tracks[i]);

                var playlistQEmbed = await EmbedHandler.CreateMusicEmbed(
                    "sbln muzik🎸🎧",
                    $"{searchResponse.Playlist.Name} --- добавлено в очередь 🤙",
                    Color.Orange);

                await Context.Channel.SendMessageAsync(embed: playlistQEmbed);
                return;
            }
             
            var track = tracks[index];
            queue.Enqueue(track);

            var qEmbed = await EmbedHandler.CreateCustomMusicEmbed(
                "sbln muzik🎸🎧",
                $"[{track.Title}]({track.Url}) **добавлено в очередь** 🤙", "🔼 - в начало листа",
                Color.Orange);

            var msg = await Context.Channel.SendMessageAsync(embed: qEmbed);
            await audioService.AttachQueueInsertControlAsync(Context.Guild.Id, msg, Context.User.Id, track);
        }

        private async Task<bool> EnsureUserInVoiceAsync(bool requireSameAsBot)
        {
            if (Context.User is not IVoiceState voiceState || voiceState.VoiceChannel == null)
            {
                await ReplyAsync("надо быть в войсе 😡");
                return false;
            }

            if (!requireSameAsBot)
                return true;

            if (!audioService.TryGetTrackedVoiceChannelId(Context.Guild.Id, out var botVcId))
                return true;

            if (voiceState.VoiceChannel.Id != botVcId)
            {
                await ReplyAsync("ты не в том войсе где я 😡");
                return false;
            }

            return true;
        }

        private async Task<bool> BotInVoice()
        {
            var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
            if (player is not null && player.State.IsConnected)
                return true;

            await ReplyAsync("так я не в войсе");
            return false;
        }

        private static bool TryParseTimecode(string input, out TimeSpan result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(input)) return false;

            var parts = input.Trim().Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts.Length > 3) return false;

            int h = 0, m = 0, s = 0;

            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[0], out m)) return false;
                if (!int.TryParse(parts[1], out s)) return false;
            }
            else
            {
                if (!int.TryParse(parts[0], out h)) return false;
                if (!int.TryParse(parts[1], out m)) return false;
                if (!int.TryParse(parts[2], out s)) return false;
            }

            if (h < 0) return false;
            if (m < 0 || m > 59) return false;
            if (s < 0 || s > 59) return false;

            result = new TimeSpan(h, m, s);
            return true;
        }
    }
}