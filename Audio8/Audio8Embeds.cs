using Discord;
using Lavalink4NET.Rest.Entities.Usage;
using Lavalink4NET.Tracks;
using System.Globalization;
using System.Text;
using sblngavnav6.Common;
using static sblngavnav6.Common.CommonUtils.Text;
using static sblngavnav6.Common.CommonUtils.Time;

namespace sblngavnav6.Audio8
{
    internal static class Audio8Embeds
    {
        public static List<Embed> Queue(Audio8Player player, IReadOnlyList<LavalinkTrack> upcoming)
        {
            var current = player.Position?.Position ?? TimeSpan.Zero;
            var total = player.CurrentTrack?.Duration ?? TimeSpan.Zero;
            var remaining = total > current ? total - current : TimeSpan.Zero;
            var queueDuration = TimeSpan.FromMilliseconds(upcoming.Sum(track => track.Duration.TotalMilliseconds));

            var header = new StringBuilder();
            header.AppendLine($"👺 **Трек:** {TrackLink(player.CurrentTrack?.Title, player.CurrentTrack?.Uri?.ToString())}");
            header.AppendLine($"👤 **Автор:** {player.CurrentTrack?.Author}");
            header.AppendLine($"📦 **В очереди:** {upcoming.Count}");
            header.AppendLine($"🕓 **Длина очереди:** {FormatTime(queueDuration)}");
            header.AppendLine($"⏳ **До конца трека осталось:** {FormatTime(remaining)}");
            header.AppendLine($"🕰️ **{ProgressBar(current, total)}**");
            header.AppendLine();

            if (upcoming.Count == 0)
                return [Audio8Embeds.QueuePage(header + "*больше в очереди ничего нет*")];

            var lines = new List<string> { "📜 **Дальше будет:**" };
            for (var i = 0; i < upcoming.Count; i++)
            {
                var track = upcoming[i];
                lines.Add($"{i + 1}. [{Truncate(track.Title ?? "track", 70)}]({track.Uri}) - {FormatTime(track.Duration)}");
            }

            var pages = new List<Embed>();
            for (var offset = 0; offset < lines.Count; offset += Audio8Constants.QueuePageSize)
            {
                var chunk = lines.Skip(offset).Take(Audio8Constants.QueuePageSize);
                pages.Add(Audio8Embeds.QueuePage(header + string.Join("\n", chunk)));
            }

            return pages;
        }

        private static string ProgressBar(TimeSpan current, TimeSpan total)
        {
            if (total.TotalSeconds <= 0)
                return "йоу?";

            var progress = Math.Clamp(current.TotalSeconds / total.TotalSeconds, 0, 1);
            var position = Math.Min((int)(progress * Audio8Constants.ProgressBarSize), Audio8Constants.ProgressBarSize - 1);

            var bar = new StringBuilder("[");
            for (var i = 0; i < Audio8Constants.ProgressBarSize; i++)
                bar.Append(i == position ? "🔴" : "▬");
            bar.Append(']');

            return bar.ToString();
        }

        public static string SourceLabel(LavalinkTrack track)
        {
            var source = track?.SourceName?.Trim().ToLowerInvariant();

            return source switch
            {
                "youtube" => "YouTube",
                "soundcloud" => "SoundCloud",
                "spotify" => "Spotify",
                "flowery" or "flowery-tts" => "TTS",
                "local" => "локальный файл",
                "http" or "https" => "прямая ссылка",
                null or "" => "источник неизвестен",
                _ => track.SourceName
            };
        }

        public static Task<Embed> Info(string sub, string description) =>
            EmbedHandler.Music(sub, description, Color.Blue);

        public static Task<Embed> Accent(string sub, string description) =>
            EmbedHandler.Music(sub, description, Color.DarkMagenta);

        public static Task<Embed> Error(string sub, string error) =>
            EmbedHandler.MusicError(sub, error);

        public static Task<Embed> NowPlaying(LavalinkTrack track, Audio8Player player)
        {
            var extra = new List<string>();

            if (player.RepeatEnabled && player.PlayCount > 1)
                extra.Add($"🔁 **Повторов:** {player.PlayCount}");

            if (player.BassBoostLevel > Audio8Constants.MinBassBoost)
                extra.Add($"🔊 **Басс:** ур. {player.BassBoostLevel}");

            if (player.FilterPreset != Audio8Constants.NoFilterPreset)
                extra.Add($"🎛️ **Фильтр:** {player.FilterPreset}");

            var description =
                $"**👺 Трек:** {TrackLink(track.Title, track.Uri?.ToString())}\n" +
                $"**👤 Автор:** {track.Author}\n" +
                $"**⏳ Длительность:** {FormatTime(track.Duration)}";

            if (extra.Count > 0)
                description += "\n" + string.Join("\n", extra);

            return EmbedHandler.MusicCustom(null, description, $"💿 {SourceLabel(track)}", Color.Purple);
        }

        public static Task<Embed> Skipped(Audio8Skip skip) =>
            EmbedHandler.Music(
                "скип",
                $"👀 Пропустили: {TrackLink(skip.Replaced?.Title, skip.Replaced?.Uri?.ToString())}\n" +
                $"🦻 Поставили: {TrackLink(skip.Upcoming?.Title, skip.Upcoming?.Uri?.ToString())}",
                Color.Green);

        public static Task<Embed> Previous(LavalinkTrack track) =>
            EmbedHandler.Music("назад", $"⏮️ Вернул: {TrackLink(track.Title, track.Uri?.ToString())}", Color.Blue);

        public static Task<Embed> FilterList(string current)
        {
            var lines = Audio8Filters.Presets.Select(preset =>
            {
                var mark = preset == current ? "▸" : "  ";
                return $"{mark} **{preset}** - {Audio8Filters.Describe(preset)}";
            });

            return EmbedHandler.Music("фильтр", string.Join("\n", lines), Color.DarkMagenta);
        }

        public static Task<Embed> Filter(string preset) =>
            EmbedHandler.Music(
                "фильтр",
                preset == Audio8Constants.NoFilterPreset
                    ? "⛔ **Фильтры выключены**"
                    : $"🎛️ **{preset}** - {Audio8Filters.Describe(preset)}",
                Color.DarkMagenta);

        public static Task<Embed> TrackFailed(LavalinkTrack track, string reason) =>
            EmbedHandler.MusicError(
                "трек",
                $"не смог доиграть {TrackLink(track?.Title, track?.Uri?.ToString())}\n{reason}");

        public static Task<Embed> Enqueued(LavalinkTrack track) =>
            EmbedHandler.Music(
                null,
                $"{TrackLink(track.Title, track.Uri?.ToString())} **добавлен в очередь** 🤙",
                Color.Orange);

        public static Task<Embed> PlaylistEnqueued(Audio8PlayResult result)
        {
            var lines = new List<string>
            {
                $"**{result.PlaylistName}** - добавлено треков: **{result.Added}** 🤙"
            };

            if (result.Skipped > 0)
                lines.Add($"✂️ Не влезло: **{result.Skipped}** (лимит {Audio8Constants.MaxPlaylistTracks})");

            return EmbedHandler.Music("плейлист", string.Join("\n", lines), Color.DarkOrange);
        }

        public static Task<Embed> Hoisted(LavalinkTrack track) =>
            EmbedHandler.Music(
                "играй+",
                $"💎 Трек {TrackLink(track.Title, track.Uri?.ToString())} добавлен в начало листа",
                Color.Green);

        public static Task<Embed> QueuePicked(Audio8QueuePick pick)
        {
            var lines = new List<string>
            {
                pick.PlayingNow
                    ? $"▶️ Играет сейчас: {TrackLink(pick.Track?.Title, pick.Track?.Uri?.ToString())}"
                    : $"💎 В начале листа: {TrackLink(pick.Track?.Title, pick.Track?.Uri?.ToString())}"
            };

            if (pick.Dropped > 0)
                lines.Add($"🗑️ Убрано треков до него: **{pick.Dropped}**");

            return EmbedHandler.Music("лист", string.Join("\n", lines), Color.Green);
        }

        public static Task<Embed> PickChosen(LavalinkTrack track) =>
            EmbedHandler.Music(
                "лист+",
                $"💎 Выбран трек: {TrackLink(track.Title, track.Uri?.ToString())}",
                Color.Green);

        public static Task<Embed> Picks(IReadOnlyList<LavalinkTrack> picks, string header)
        {
            var lines = picks.Select((track, index) =>
                $"{Audio8Constants.EmojiPicks[index]} " +
                $"{TrackLink(Truncate(track.Title ?? "track", 80), track.Uri?.ToString())} - " +
                $"{Truncate(track.Author ?? "unknown", 40)} - {FormatTime(track.Duration)} - " +
                $"**{SourceLabel(track)}**");

            return EmbedHandler.Music("играй+", header + "\n\n" + string.Join("\n", lines), Color.Purple);
        }

        public static Embed Vote(string description, Color color) =>
            EmbedHandler.Build(new EmbedSpec
            {
                Title = "ГОЛОСОВАНИЕ",
                Description = description,
                Color = color,
                Footer = EmbedHandler.VoteFooter
            });

        public static Embed VoteFinished(string winner) =>
            EmbedHandler.Build(new EmbedSpec
            {
                Title = "ГОЛОСОВАНИЕ ЗАВЕРШЕНО",
                Description = $"**Я выбираю:** `{winner}`",
                Color = Color.Gold,
                Footer = EmbedHandler.VoteFooter
            });

        public static Embed Stats(LavalinkServerStatistics statistics, TimeSpan age, Audio8StatsContext context)
        {
            var fields = new List<EmbedFieldSpec>
            {
                new("🎧 Плееры",
                    $"наших: `{context.Players}` / играет: `{context.Playing}`\n" +
                    $"в очередях: `{context.QueuedTracks}` трек(ов)",
                    true),
                new("🖥️ Lavalink",
                    $"аптайм: `{statistics.Uptime:d\\.hh\\:mm\\:ss}`\n" +
                    $"нагрузка: `{statistics.ProcessorUsage.LavalinkLoad:P1}` из `{statistics.ProcessorUsage.SystemLoad:P1}`\n" +
                    $"память: `{statistics.MemoryUsage.UsedMemory / 1024 / 1024}` / `{statistics.MemoryUsage.AllocatedMemory / 1024 / 1024}`MB",
                    true)
            };

            if (statistics.FrameStatistics is { } frames)
            {
                var lost = frames.NulledFrames + Math.Max(0, frames.DeficitFrames);
                var total = frames.SentFrames + lost;
                var lossRate = total > 0 ? (double)lost / total : 0;

                fields.Add(new EmbedFieldSpec("📶 Качество звука",
                    $"потери: `{lossRate:P2}` ({lost} из {total})\n" +
                    $"{(lossRate > 0.02 ? "⚠️ заикания вероятны" : "✅ в норме")}",
                    true));
            }

            fields.Add(new EmbedFieldSpec("💾 Снимок состояния",
                context.SnapshotAt is null
                    ? "`ещё не сохранялся`"
                    : $"сохранён `{FormatAge(DateTimeOffset.UtcNow - context.SnapshotAt.Value)}` назад\n" +
                      $"гильдий в снимке: `{context.SnapshotGuilds}`",
                true));

            return EmbedHandler.Build(new EmbedSpec
            {
                Title = "🎛️ LavaStats",
                Description = $"данные Lavalink обновлены {FormatAge(age)} назад",
                Color = Color.Purple,
                Fields = fields,
                Footer = $"{EmbedHandler.MusicFooter} & sbln статистикс🔭"
            });
        }

        private static string Percent(double value, int digits) =>
            (value * 100).ToString($"0.{new string('0', digits)}", CultureInfo.InvariantCulture) + "%";

        private static string FormatAge(TimeSpan age) => age switch
        {
            { TotalSeconds: < 60 } => $"{age.TotalSeconds:0}с",
            { TotalMinutes: < 60 } => $"{age.TotalMinutes:0}м",
            _ => $"{age.TotalHours:0}ч"
        };

        public static Embed QueuePage(string body) =>
            EmbedHandler.Build(new EmbedSpec
            {
                Title = $"{EmbedHandler.MusicFooter}, лист",
                Description = body,
                Color = Color.Purple,
                Footer = $"powered by {EmbedHandler.AudioEngine}"
            });
    }
}
