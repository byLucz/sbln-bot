using Discord;
using Discord.Commands;
using Lavalink4NET.Players;
using System.Runtime.InteropServices;
using sblngavnav6.Common;
using sblngavnav6.Core;
using sblngavnav6.Services;
using static sblngavnav6.Common.CommonUtils.Text;
using static sblngavnav6.Common.CommonUtils.Time;

namespace sblngavnav6.Audio8
{
    [RequireGuild]
    public sealed class Audio8Commands : ModuleBase<SocketCommandContext>
    {
        private readonly Audio8Service _service;
        private readonly Audio8SearchService _search;
        private readonly Audio8VoteService _vote;
        private readonly IHttpClientFactory _httpClientFactory;

        internal Audio8Commands(
            Audio8Service service,
            Audio8SearchService search,
            Audio8VoteService vote,
            IHttpClientFactory httpClientFactory)
        {
            _service = service;
            _search = search;
            _vote = vote;
            _httpClientFactory = httpClientFactory;
        }

        [Command("играй")]
        [Alias("и")]
        public async Task PlayAsync([Remainder] string searchQuery)
        {
            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                await ReplyAsync("напиши нормально запрос бро");
                return;
            }

            var player = await EnsurePlayerAsync(requireSameChannel: false);
            if (player is null)
                return;

            var query = Audio8Query.Normalize(searchQuery, _service.GetSearchPrefix(Context.Guild.Id));
            var result = await _service.LoadAsync(query.Identifier);

            if (!result.HasMatches && !string.IsNullOrEmpty(query.FallbackIdentifier))
            {
                var fallback = await _service.LoadAsync(query.FallbackIdentifier);
                if (fallback.HasMatches)
                {
                    result = fallback;
                    query = query with { PlaylistIndex = 0, SelectedTrackId = null };
                }
            }

            if (!result.HasMatches)
            {
                if (Context.Channel is ITextChannel textChannel &&
                    await _search.TryRecoverAsync(Context.Guild.Id, textChannel, Context.User.Id, query.Identifier, result))
                {
                    return;
                }

                await ReplyAsync(embed: await Audio8Embeds.Error("играй", "ничего не нашлось по запросу..."));
                return;
            }

            var outcome = await _service.EnqueueAsync(player, result, query);

            switch (outcome.Kind)
            {
                case Audio8PlayKind.Playlist:
                    _service.RememberPlaylist(Context.Guild.Id, outcome.PlaylistName, outcome.PlaylistUrl);
                    await ReplyAsync(embed: await Audio8Embeds.PlaylistEnqueued(outcome));
                    break;

                case Audio8PlayKind.Enqueued:
                    await _service.SendEnqueuedAsync(Context.Channel, Context.Guild.Id, Context.User.Id, outcome.Track);
                    break;

                case Audio8PlayKind.Nothing:
                    await ReplyAsync(embed: await Audio8Embeds.Error("играй", "нечего ставить в очередь"));
                    break;
            }
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

            await PlayAsync($"ftts://{text.Trim()}");
        }

        [Command("выйди")]
        [Alias("л")]
        public async Task LeaveAsync()
        {
            if (!await RequireConnectedAsync())
                return;

            await _service.LeaveAsync(Context.Guild.Id);
        }

        [Command("источник")]
        [Alias("сорс", "деф")]
        public async Task DefaultSourceAsync([Remainder] string source = null)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                var current = Audio8Query.DisplayName(_service.GetSearchPrefix(Context.Guild.Id));
                await ReplyAsync(embed: await Audio8Embeds.Accent("источник",
                    $"дефолтный источник для `x и`: **{current}**\nсменить: `x источник ютуб / спотик / склауд`"));
                return;
            }

            var prefix = Audio8Query.TryParsePrefix(source);
            if (prefix is null)
            {
                await ReplyAsync(embed: await Audio8Embeds.Error("источник", "не знаю такой. доступно: **ютуб** / **спотик** / **склауд**"));
                return;
            }

            _service.SetSearchPrefix(Context.Guild.Id, prefix);
            await ReplyAsync(embed: await Audio8Embeds.Accent("источник",
                $"🎚️ Дефолтный источник для `x и` теперь: **{Audio8Query.DisplayName(prefix)}**"));
        }

        [Command("скип")]
        [Alias("ск")]
        public async Task SkipAsync([Optional] int? index)
        {
            var player = await RequirePlayerAsync();
            if (player is null)
                return;

            var skip = await _service.SkipAsync(player, index);
            if (!skip.Ok)
            {
                var error = index.HasValue
                    ? $"в очереди нет трека с номером {index}"
                    : "в очереди больше ничего нет =(";

                await ReplyAsync(embed: await Audio8Embeds.Error("скип", error));
                return;
            }

            await ReplyAsync(embed: await Audio8Embeds.Skipped(skip));
        }

        [Command("недавние")]
        [Alias("нед", "последние")]
        public async Task RecentPlaylistsAsync()
        {
            var recent = _service.GetRecentPlaylists(Context.Guild.Id);

            if (recent.Count == 0)
            {
                await ReplyAsync(embed: await Audio8Embeds.Error("недавние плейлисты", "плейлистов ещё не было"));
                return;
            }

            await _service.SendWithControlsAsync(
                Context.Channel,
                await Audio8Embeds.RecentPlaylists(recent),
                Audio8Controls.RecentPlaylists(recent.Count),
                scope: Audio8Scopes.Recent(Context.Guild.Id));
        }

        [Command("плейлист")]
        [Alias("лист")]
        public async Task QueueAsync()
        {
            var player = await RequirePlayerAsync();
            if (player is null)
                return;

            await _service.SendQueueAsync(player, (ITextChannel)Context.Channel);
        }

        [Command("перемешай")]
        [Alias("шафл", "перемешка")]
        public async Task ShuffleAsync()
        {
            var player = await RequirePlayerAsync();
            if (player is null)
                return;

            var count = await _service.ShuffleAsync(player);
            if (count == 0)
            {
                await ReplyAsync(embed: await Audio8Embeds.Error("шафл", "в очереди нечего мешать (нужно ≥2 трека)"));
                return;
            }

            await ReplyAsync(embed: await Audio8Embeds.Accent("шафл", $"🔀 **Очередь перемешана** ({count} треков)"));
        }

        [Command("пауза")]
        [Alias("пз")]
        public async Task PauseAsync()
        {
            var player = await RequirePlayerAsync();
            if (player is null)
                return;

            if (player.CurrentTrack is null)
            {
                await ReplyAsync(embed: await Audio8Embeds.Error("пауза", "так ничего не играет"));
                return;
            }

            if (player.State is PlayerState.Paused)
            {
                await ReplyAsync(embed: await Audio8Embeds.Error("пауза", "але, я уже на паузе"));
                return;
            }

            await player.PauseAsync();
            await ReplyAsync(embed: await Audio8Embeds.Info("пауза",
                $"Поставил на паузу: {TrackLink(player.CurrentTrack.Title, player.CurrentTrack.Uri?.ToString())} ⏸️"));
        }

        [Command("продолжи")]
        [Alias("прод")]
        public async Task ResumeAsync()
        {
            var player = await RequirePlayerAsync();
            if (player is null)
                return;

            if (player.CurrentTrack is null)
            {
                await ReplyAsync(embed: await Audio8Embeds.Error("продолжи", "так ничего не играет"));
                return;
            }

            if (player.State is not PlayerState.Paused)
            {
                await ReplyAsync(embed: await Audio8Embeds.Error("продолжи", "я не на паузе!!"));
                return;
            }

            await player.ResumeAsync();
            await ReplyAsync(embed: await Audio8Embeds.Info("продолжи",
                $"Продолжаю: {TrackLink(player.CurrentTrack.Title, player.CurrentTrack.Uri?.ToString())} ▶️"));
        }

        [Command("останови")]
        [Alias("стоп")]
        public async Task StopAsync()
        {
            var player = await RequirePlayerAsync();
            if (player is null)
                return;

            await _service.StopPlaybackAsync(player);
            await ReplyAsync(embed: await Audio8Embeds.Info("стоп", "стопнулся и очистил плейлист ⛔"));
        }

        [Command("громкость")]
        [Alias("гр")]
        public async Task VolumeAsync([Optional] int? level)
        {
            var player = await RequirePlayerAsync();
            if (player is null)
                return;

            if (!level.HasValue)
            {
                await ReplyAsync(embed: await Audio8Embeds.Accent("громкость",
                    $"**Сейчас - {(int)Math.Round(player.Volume * 100)} 📶**\n" +
                    $"сменить: `x гр {Audio8Constants.MinVolume}-{Audio8Constants.MaxVolume}`"));
                return;
            }

            var volume = level.Value;

            if (volume < Audio8Constants.MinVolume || volume > Audio8Constants.MaxVolume)
            {
                await ReplyAsync(embed: await Audio8Embeds.Error("громкость",
                    $"только значения {Audio8Constants.MinVolume}-{Audio8Constants.MaxVolume}"));
                return;
            }

            await player.SetVolumeAsync(volume / 100f);
            await ReplyAsync(embed: await Audio8Embeds.Accent("громкость", $"**Громкость - {volume} 📶**"));
        }

        [Command("басс")]
        [Alias("бс")]
        public async Task BassBoostAsync([Optional] int? level)
        {
            var player = await RequirePlayerAsync();
            if (player is null)
                return;

            if (!level.HasValue)
            {
                await ReplyAsync(embed: await Audio8Embeds.Accent("басс",
                    $"🔊 Ступень **{player.BassBoostLevel}** из {Audio8Constants.MaxBassBoost} - **{Audio8Service.BassBoostName(player.BassBoostLevel)}**\n" +
                    $"сменить: `x бс 1..{Audio8Constants.MaxBassBoost}`, где **1** - выключено"));
                return;
            }

            if (level.Value < Audio8Constants.MinBassBoost || level.Value > Audio8Constants.MaxBassBoost)
            {
                await ReplyAsync(embed: await Audio8Embeds.Error("басс",
                    $"только ступени {Audio8Constants.MinBassBoost}-{Audio8Constants.MaxBassBoost}, где **1** - выключено"));
                return;
            }

            await _service.SetBassBoostAsync(player, level.Value);

            var description = level.Value == Audio8Constants.MinBassBoost
                ? "⛔ **Басс-буст выключен**"
                : $"🔊 **Басс-буст: ур. {level.Value}** ({Audio8Service.BassBoostName(level.Value)})";

            await ReplyAsync(embed: await Audio8Embeds.Accent("басс", description));
        }

        [Command("назад")]
        [Alias("пред", "предыдущий")]
        public async Task PreviousAsync()
        {
            var player = await RequirePlayerAsync();
            if (player is null)
                return;

            var previous = await _service.PlayPreviousAsync(player);
            if (previous is null)
            {
                await ReplyAsync(embed: await Audio8Embeds.Error("назад", "истории ещё нет"));
                return;
            }

            await ReplyAsync(embed: await Audio8Embeds.Previous(previous));
        }

        [Command("фильтр")]
        [Alias("фильтры", "эффект")]
        public async Task FilterAsync([Remainder] string preset = null)
        {
            var player = await RequirePlayerAsync();
            if (player is null)
                return;

            if (string.IsNullOrWhiteSpace(preset))
            {
                await ReplyAsync(embed: await Audio8Embeds.FilterList(player.FilterPreset));
                return;
            }

            var applied = await _service.ApplyFilterAsync(player, preset);
            if (applied is null)
            {
                await ReplyAsync(embed: await Audio8Embeds.Error("фильтр",
                    $"не знаю такой. доступно: {string.Join(", ", Audio8Filters.Presets)}"));
                return;
            }

            await ReplyAsync(embed: await Audio8Embeds.Filter(applied));
        }

        [Command("залупа")]
        [Alias("луп")]
        public async Task LoopAsync()
        {
            var player = await RequirePlayerAsync();
            if (player is null)
                return;

            var enabled = player.ToggleRepeat();

            await ReplyAsync(embed: await Audio8Embeds.Accent("луп", enabled ? "🔁 **Луп вкл**" : "⛔ **Луп выкл**"));
        }

        [Command("перейти")]
        [Alias("пр")]
        public async Task SeekAsync([Remainder] string timecode)
        {
            var player = await RequirePlayerAsync();
            if (player is null)
                return;

            if (player.CurrentTrack is null)
            {
                await ReplyAsync(embed: await Audio8Embeds.Error("перейти", "так ничего не играет"));
                return;
            }

            if (!TryParseTimecode(timecode, out var position))
            {
                await ReplyAsync(embed: await Audio8Embeds.Error("перейти", "формат `мм:сс` или `чч:мм:сс`"));
                return;
            }

            position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, player.CurrentTrack.Duration.Ticks));

            await player.SeekAsync(position);
            await ReplyAsync(embed: await Audio8Embeds.Info("перейти", $"⏩ Перемотал на **{position:hh\\:mm\\:ss}**"));
        }

        [Command("голосование")]
        [Alias("голос")]
        public async Task VoteAsync([Remainder] string options)
        {
            var items = (options ?? string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (items.Count < 2)
            {
                await ReplyAsync("для голосования нужно минимум 2 варианта через `|`");
                return;
            }

            if (items.Count > Audio8Constants.MaxVoteItems)
            {
                await ReplyAsync($"слишком много вариантов ({items.Count}), беру первые {Audio8Constants.MaxVoteItems}");
                items = items.Take(Audio8Constants.MaxVoteItems).ToList();
            }

            var player = await EnsurePlayerAsync(requireSameChannel: false, allowDuringVote: true);
            if (player is null)
                return;

            var winner = items.RandomList();
            var statusMessage = await _service.SendAsync(
                Context.Channel,
                Audio8Embeds.Vote("⏳ Кукапим секвенции...", Color.DarkBlue));

            using var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("sblnokv6");

            await _vote.RunAsync(player, statusMessage, items, winner, http);
        }

        [Command("лавастат")]
        public async Task StatsAsync()
        {
            var embed = _service.Stats.Build(_service.BuildStatsContext());
            if (embed is null)
            {
                await ReplyAsync("Статы пока нет...");
                return;
            }

            await ReplyAsync(embed: embed);
        }

        private async Task<Audio8Player> EnsurePlayerAsync(bool requireSameChannel, bool allowDuringVote = false)
        {
            if (!allowDuringVote && _service.IsVoteRunning(Context.Guild.Id))
            {
                await ReplyAsync(embed: await Audio8Embeds.Error("музыка", "идёт голосование, дождись конца или выгони меня командой `x л`"));
                return null;
            }

            if (Context.User is not IVoiceState { VoiceChannel: { } voiceChannel })
            {
                await ReplyAsync("надо быть в войсе 😡");
                return null;
            }

            var existing = _service.GetPlayer(Context.Guild.Id);

            if (existing is not null && requireSameChannel && !Audio8Service.IsInSameVoice(Context.User, existing))
            {
                await ReplyAsync("ты не в том войсе где я 😡");
                return null;
            }

            var textChannelId = Context.Channel is ITextChannel textChannel ? textChannel.Id : 0UL;

            try
            {
                return await _service.JoinAsync(voiceChannel, textChannelId);
            }
            catch (TimeoutException ex)
            {
                await LoggingService.LogWarningAsync(
                    Audio8Constants.LogSource,
                    $"Плеер не создался за отведённое время g={Context.Guild.Id}: {ex.Message}");

                await ReplyAsync(embed: await Audio8Embeds.Error("играй", "не смог зайти в войс, попробуй ещё раз"));
                return null;
            }
            catch (Exception ex)
            {
                await LoggingService.LogErrorAsync(Audio8Constants.LogSource, $"Вход в войс сорвался g={Context.Guild.Id}", ex);
                await ReplyAsync(embed: await Audio8Embeds.Error("играй", "не смог зайти в войс, детали в логах"));
                return null;
            }
        }

        private async Task<Audio8Player> RequirePlayerAsync()
        {
            if (_service.IsVoteRunning(Context.Guild.Id))
            {
                await ReplyAsync(embed: await Audio8Embeds.Error("голос", "идёт голосование, дождись конца!"));
                return null;
            }

            if (Context.User is not IVoiceState { VoiceChannel: not null })
            {
                await ReplyAsync("надо быть в войсе 😡");
                return null;
            }

            var player = _service.GetPlayer(Context.Guild.Id);
            if (player is null)
            {
                await ReplyAsync("так я не в войсе");
                return null;
            }

            if (!Audio8Service.IsInSameVoice(Context.User, player))
            {
                await ReplyAsync("ты не в том войсе где я 😡");
                return null;
            }

            return player;
        }

        private async Task<bool> RequireConnectedAsync()
        {
            if (_service.IsConnected(Context.Guild.Id))
                return true;

            await ReplyAsync("так я не в войсе");
            return false;
        }

    }
}
