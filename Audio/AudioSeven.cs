using Discord.Commands;
using Discord;
using System.Text;
using Victoria.Rest.Search;
using Victoria;
using sblngavnav5X.Core;
using sblngavnav5X.Services;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Victoria.Rest.Filters;

namespace sblngavnav5X.Audio
{
    public sealed class AudioSeven(LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode, AudioSevenService audioService) : ModuleBase<SocketCommandContext>
    {
        public async Task JoinAsync()
        {
            var voiceState = Context.User as IVoiceState;
            if (!await UserInVoice())
            {
                return;
            }
            await lavaNode.JoinAsync(voiceState?.VoiceChannel);
            audioService.TextChannels.TryAdd(voiceState.VoiceChannel.GuildId, Context.Channel as ITextChannel);
            audioService.VoiceChannels.TryAdd(voiceState.VoiceChannel.GuildId, voiceState?.VoiceChannel);
        }

        [Command("выйди")]
        [Alias("л")]
        public async Task LeaveAsync()
        {
            if (!await UserInVoice() || !await BotInVoice())
            {
                return;
            }

            await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
            if (await lavaNode.TryGetPlayerAsync(Context.Guild.Id) is not null)
            {
                var player = await lavaNode.GetPlayerAsync(Context.Guild.Id);
                await lavaNode.LeaveAsync(GetVoiceChannel());
            }
        }

        [Command("играй")]
        [Alias("и")]
        public async Task PlayAsync([Remainder] string searchQuery)
        {
            if (!await UserInVoice())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                await ReplyAsync("нормально название пиши ебланчик");
                return;
            }

            var guildId = Context.Guild.Id;
            var player = await lavaNode.TryGetPlayerAsync(guildId);

            if (player == null || !player.State.IsConnected)
            {
                await JoinAsync();
                player = await lavaNode.GetPlayerAsync(guildId);
            }

            if (searchQuery.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                searchQuery = searchQuery.Replace("youtu.be/", "youtube.com/watch?v=");

            int index = 0;

            if (searchQuery.IndexOf("youtube.com/watch?v=", StringComparison.OrdinalIgnoreCase) >= 0
             && searchQuery.IndexOf("&list=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var uri = new Uri(searchQuery);
                var query = uri.Query
                                .TrimStart('?')
                                .Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var param in query)
                {
                    var parts = param.Split('=', 2);
                    if (parts.Length == 2 &&
                        parts[0].Equals("index", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(parts[1], out var parsed) && parsed > 0)
                    {
                        index = parsed - 1;
                        break;
                    }
                }

                searchQuery = Regex.Replace(
                    searchQuery,
                    @"watch\?v=.*?&list=",
                    "playlist?list=",
                    RegexOptions.IgnoreCase
                );
            }
            else if (searchQuery.Contains("склауд", StringComparison.OrdinalIgnoreCase))
            {
                searchQuery = "scsearch:" +
                              Regex.Replace(searchQuery, "склауд", "", RegexOptions.IgnoreCase)
                                   .Trim();
            }
            else if (!searchQuery.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
            {
                searchQuery = "ytsearch:" + searchQuery;
            }

            try
            {
                var searchResponse = await lavaNode.LoadTrackAsync(searchQuery);

                if (searchResponse.Tracks.Count == 0)
                {
                    var embedErr = await EmbedHandler.CreateErrorEmbed(
                        "sbln muzik🎸🎧, играй",
                        "нихуя не нашлось по запросу..."
                    );
                    await ReplyAsync(embed: embedErr);
                    return;
                }

                var maxIdx = searchResponse.Tracks.Count - 1;
                if (index < 0) index = 0;
                if (index > maxIdx) index = maxIdx;

                var queue = player.GetQueue();
                if (!queue.Any() && player.Track == null)
                    await PlayNow(searchResponse, player, index);
                else
                    await QueueNow(searchResponse, player, index);
            }
            catch (Exception e)
            {
                var embedErr = await EmbedHandler.CreateErrorEmbed(
                    "ненене👿",
                    $"хуйня какая то\n*{e.Message}*"
                );
                await ReplyAsync(embed: embedErr);
            }
        }


        [Command("скип")]
        [Alias("ск")]
        public async Task SkipAsync([Optional] int? index)
        {
            if (!await UserInVoice() || !await BotInVoice())
            {
                return;
            }

            var player = await lavaNode.GetPlayerAsync(Context.Guild.Id);

            if (index.HasValue)
            {
                int idx = index.Value - 1;

                if (idx < 0 || idx >= player.GetQueue().Count)
                {
                    var err = await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, скип", $"🚫 В очереди нет песни с номером {index}");
                    await ReplyAsync(embed: err);
                    return;
                }

                var nextTrack = player.GetQueue().ElementAt(idx);
                player.GetQueue().RemoveAt(idx);

                await LoggingService.LogInformationAsync("sbln muzik🎸🎧", $"Пропустили говно: [{player.Track.Title}]({player.Track.Url}), теперь играет: {nextTrack.Title}");

                var embed = await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧, скип", $"👀 Пропустили говно: [{player.Track.Title}]({player.Track.Url})\n🦻 Вместо этого запихали: {nextTrack.Title}", Color.Green);
                await ReplyAsync(embed: embed);

                await player.PlayAsync(lavaNode, nextTrack, false);
                return;
            }

            if (player.GetQueue().TryDequeue(out var track))
            {
                await LoggingService.LogInformationAsync("sbln muzik🎸🎧", $"Пропустили говно: [{player.Track.Title}]({player.Track.Url})");

                var embed = await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧, скип", $"👀 Пропустили говно: [{player.Track.Title}]({player.Track.Url})\n🦻 Вместо этого запихали: [{track.Title}]({track.Url})", Color.Green);
                await ReplyAsync(embed: embed);

                await player.PlayAsync(lavaNode, track, false);
            }
            else
            {
                var err = await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, скип", $"🚫 В очереди больше ничего нет");
                await ReplyAsync(embed: err);
            }
        }

        [Command("плейлист")]
        [Alias("лист")]
        private async Task QueueAsync()
        {
            if (!await UserInVoice() || !await BotInVoice())
            {
                return;
            }

            var player = await lavaNode.GetPlayerAsync(Context.Guild.Id);

            if (player.Track == null)
            {
                var emptyEmbed = await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, плейлист", "🚫 Очередь пуста");
                await Context.Channel.SendMessageAsync(embed: emptyEmbed);
                return;
            }

            TimeSpan current = player.Track.Position;
            TimeSpan total = player.Track.Duration;
            TimeSpan remaining = total - current;

            if (player.GetQueue().Count < 1)
            {
                var embed = await EmbedHandler.CreateMusicEmbed(
                    $"sbln muzik🎸🎧, лист\n",
                    $"👺 **Ща Играет:** [{player.Track.Title}]({player.Track.Url})\n" +
                    $"👤 **Автор:** {player.Track.Author}\n" +
                    $"⏳ **До конца осталось:** {FormatTime(remaining)}\n" +
                    $"{BuildProgressBar(current, total)}\n\n" +
                    "*больше в очереди ничего нет*",
                    Color.Blue); 

                await Context.Channel.SendMessageAsync(embed: embed);
                return;
            }

            var builder = new EmbedBuilder();
            builder.WithTitle($"sbln muzik🎸🎧, лист - ({player.GetQueue().Count})");
            builder.WithColor(3447003);

            var descriptionBuilder = new StringBuilder();

            if (player.Track != null)
            {
                descriptionBuilder.AppendLine($"👺 **Ща Играет:** [{player.Track.Title}]({player.Track.Url})");
                descriptionBuilder.AppendLine($"👤 **Автор:** {player.Track.Author}");
                descriptionBuilder.AppendLine($"⏳ **До конца осталось:** {FormatTime(remaining)}");
                descriptionBuilder.AppendLine(BuildProgressBar(current, total));
                descriptionBuilder.AppendLine();
            }

            if (player.GetQueue().Any())
            {
                descriptionBuilder.AppendLine("📜 **Дальше будет:**");

                int trackNum = 1;
                foreach (LavaTrack track in player.GetQueue())
                {
                    descriptionBuilder.AppendLine($"{trackNum}. [{track.Title}]({track.Url}) - {FormatTime(track.Duration)}");
                    trackNum++;
                }
            }
            builder.WithDescription(descriptionBuilder.ToString());
            builder.WithFooter("L4 + V7 open beta");
            builder.WithCurrentTimestamp();

            await ReplyAsync(embed: builder.Build());
        }

        [Command("пауза")]
        [Alias("пз")]
        public async Task PauseAsync()
        {
            var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
            if (player.IsPaused && player.Track != null)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, пауза", "так ничего не играет"));
                return;
            }

            try
            {
                await player.PauseAsync(lavaNode);
                await ReplyAsync(embed: await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧, пауза", $"поставил на паузу - [{player.Track.Title}]({player.Track.Url}) ⏸️", Color.Blue));
            }
            catch (Exception e)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, пауза", e.ToString()));
            }
        }

        [Command("продолжи")]
        [Alias("прод")]
        public async Task ResumeAsync()
        {
            var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
            if (!player.IsPaused && player.Track != null)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, продолжи", "так ничего не играет"));
                return;
            }

            try
            {
                await player.ResumeAsync(lavaNode, player.Track);
                await ReplyAsync(embed: await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧, продолжи", $"продолжаю - [{player.Track.Title}]({player.Track.Url}) ▶️", Color.Blue));
            }
            catch (Exception e)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, продолжи", e.ToString()));
            }
        }

        [Command("останови")]
        [Alias("стоп")]
        public async Task StopAsync()
        {
            if (!await BotInVoice())
            {
                return;
            }

            var player = await lavaNode.GetPlayerAsync(Context.Guild.Id);

            try
            {
                player.GetQueue().Clear();

                await player.SeekAsync(lavaNode, player.Track.Duration);

                await LoggingService.LogInformationAsync("sbln muzik🎸🎧", $"стопнулся");
                await ReplyAsync(embed: await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧, стоп", "стопнулся и очистил плейлист ⛔", Color.Blue));
            }
            catch (Exception e)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, стоп", e.ToString()));
            }
        }

        [Command("громкость")]
        [Alias("гр")]
        public async Task VolumeAsync(int volume)
        {
            if (!await BotInVoice())
            {
                return;
            }

            if (volume >= 500 || volume < 1)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, громкость", "только значения от 1-500"));
            }
            try
            {
                var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
                await player.SetVolumeAsync(lavaNode, volume);
                await ReplyAsync(embed: await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧, громкость", $"**Громкость выставлена на уровень {volume} 📶**", Color.DarkMagenta));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, громкость", ex.Message));
            }
        }

        [Command("басс")]
        [Alias("бс")]
        public async Task BassBoostCommand(string level)
        {
            if (!await BotInVoice())
            {
                return;
            }

            if (!Int32.TryParse(level, out int outLevel))
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, басы", "только значения от 1-4"));
            }

            EqualizerBand[][] bands = new EqualizerBand[][]
            {
                    new EqualizerBand[]
                    {
                        new EqualizerBand(0, 0d),
                        new EqualizerBand(1, 0d),
                        new EqualizerBand(2, 0d),
                        new EqualizerBand(3, 0d),
                        new EqualizerBand(4, 0d),
                        new EqualizerBand(5, 0d),
                    },
                    new EqualizerBand[]
                    {
                        new EqualizerBand(0, -0.05d),
                        new EqualizerBand(1, 0.06d),
                        new EqualizerBand(2, 0.16d),
                        new EqualizerBand(3, 0.3d),
                        new EqualizerBand(4, -0.12d),
                        new EqualizerBand(5, 0.11d),
                    },
                    new EqualizerBand[]
                    {
                        new EqualizerBand(0, -0.1d),
                        new EqualizerBand(1, 0.14d),
                        new EqualizerBand(2, 0.32d),
                        new EqualizerBand(3, 0.6d),
                        new EqualizerBand(4, -0.25d),
                        new EqualizerBand(5, 0.22d),
                    },
                    new EqualizerBand[]
                    {
                        new EqualizerBand(0, -0.25d),
                        new EqualizerBand(1, 1d),
                        new EqualizerBand(2, 1d),
                        new EqualizerBand(3, 1d),
                        new EqualizerBand(4, -0.25d),
                        new EqualizerBand(5, 0.5d),
                    },
            };

            if (outLevel < 1 || outLevel > bands.Length)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("sbln muzik🎸🎧, басы", "только значения от 1-4"));
            }

            var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
            await player.EqualizeAsync(lavaNode, bands[outLevel - 1]);
            if (outLevel == 1)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧, басы", $"**БАСС БУСТ ВЫКЛЮЧЕН!**", Color.DarkMagenta));
            }
            else
            await ReplyAsync(embed: await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧, басы", $"**БАСС БУСТ АКТИВИРОВАН НА УРОВЕНЬ {outLevel}!**", Color.DarkMagenta));
        }


        private async Task PlayNow(SearchResponse searchResponse, LavaPlayer<LavaTrack> player, int index)
            {
            var track = searchResponse.Tracks.ElementAt(index);

            if (searchResponse.Type == SearchType.Playlist)
            {
                for (var i = index; i < searchResponse.Tracks.Count; i++)
                {
                    if (i == 0 || i == index)
                    {
                        await player.PlayAsync(lavaNode, track);
                        await LoggingService.LogInformationAsync("sbln muzik🎸🎧", $"👺 Ща Играет - [{track.Title}]({track.Url})");
                        var playlistEmbed = await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧", $"👺 **Ща Играет: **[{track.Title}]({track.Url})\n**👤 Автор: **{track.Author}\n**⏳ Длительность: **{FormatTime(track.Duration)}\n", Color.Purple);
                        await Context.Channel.SendMessageAsync(embed: playlistEmbed);
                    }
                    else
                    {
                        player.GetQueue().Enqueue(searchResponse.Tracks.ElementAt(i));
                    }
                }

                var playlistQEmbed = await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧", $"{searchResponse.Playlist.Name} **кучка треков добавлена в плейлист**🤙", Color.Orange);
                await LoggingService.LogInformationAsync("sbln muzik🎸🎧", $"{searchResponse.Playlist.Name} кучка треков добавлена в плейлист🤙");
                await Context.Channel.SendMessageAsync(embed: playlistQEmbed);
            }
            else
            {
                await player.PlayAsync(lavaNode, track);
                var QEmbed = await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧", $"👺 **Ща Играет: **[{track.Title}]({track.Url})\n**👤 Автор: **{track.Author}\n**⏳ Длительность: **{FormatTime(track.Duration)}\n", Color.Purple);
                await LoggingService.LogInformationAsync("sbln muzik🎸🎧", $"👺 Ща Играет - [{track.Title}]({track.Url})\n");
                await Context.Channel.SendMessageAsync(embed: QEmbed);
            }
        }

        private async Task QueueNow(SearchResponse searchResponse, LavaPlayer<LavaTrack> player, int index)
        {
            if (searchResponse.Type == SearchType.Playlist)
            {
                for (var i = index; i < searchResponse.Tracks.Count; i++)
                {
                    player.GetQueue().Enqueue(searchResponse.Tracks.ElementAt(i));
                }

                await LoggingService.LogInformationAsync("sbln muzik🎸🎧", $"{searchResponse.Playlist.Name} кучка треков добавлена в плейлист🤙");
                var playlistQEmbed = await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧", $"{searchResponse.Playlist.Name} **кучка треков добавлена в плейлист**🤙", Color.Orange);
                await Context.Channel.SendMessageAsync(embed: playlistQEmbed);
            }
            else
            {
                var track = searchResponse.Tracks.ElementAt(0);
                player.GetQueue().Enqueue(track);
                await LoggingService.LogInformationAsync("sbln muzik🎸🎧", $"[{track.Title}]({track.Url}) **песня добавлена в плейлист**🤙");
                var QEmbed = await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧", $"[{track.Title}]({track.Url}) **песня добавлена в плейлист**🤙", Color.Orange);;
                await Context.Channel.SendMessageAsync(embed: QEmbed);
            }
        }

        private async Task<bool> UserInVoice()
        {
            if (Context.User is IVoiceState voiceState)
            {
                if (voiceState.VoiceChannel != null)
                    return true;
            }

            await ReplyAsync("надо быть в войсе, дурачок 😡");
            return false;
        }

        private async Task<bool> BotInVoice()
        {
            var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);

            if (player is not null && player.State.IsConnected)
                return true;

            await ReplyAsync("так я не в войсе");
            return false;
        }

        private IVoiceChannel GetVoiceChannel()
        {
            var voiceState = Context.User as IVoiceState;
            return voiceState?.VoiceChannel;
        }

        private string FormatTime(TimeSpan time) => time.ToString(@"hh\:mm\:ss");

        private string BuildProgressBar(TimeSpan current, TimeSpan total, int size = 15)
        {
            double progress = current.TotalSeconds / total.TotalSeconds;
            int position = (int)(progress * size);

            var bar = new StringBuilder("▶ [");
            for (int i = 0; i < size; i++)
            {
                if (i == position)
                    bar.Append("🔘");
                else
                    bar.Append("▬");
            }
            bar.Append("]");

            return bar.ToString();
        }

    }
}
