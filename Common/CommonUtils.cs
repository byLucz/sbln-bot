using Discord;
using Discord.Net;

namespace sblngavnav6.Common
{
    public static class CommonUtils
    {
        public static class Chat
        {
            public const string DefaultAvatar = "https://cdn-icons-png.flaticon.com/512/3670/3670157.png";

            private static readonly TimeSpan BulkDeleteLimit = TimeSpan.FromDays(14) - TimeSpan.FromMinutes(5);

            public static string Avatar(IUser user, ushort size = 128) =>
                user?.GetAvatarUrl(ImageFormat.Auto, size) ?? user?.GetDefaultAvatarUrl() ?? DefaultAvatar;

            public static string GuildAvatar(IGuildUser user, string fallback = DefaultAvatar) =>
                user?.GetGuildAvatarUrl() ?? user?.GetAvatarUrl() ?? user?.GetDefaultAvatarUrl() ?? fallback;

            public static async Task ReactAsync(IUserMessage message, string emote)
            {
                if (message is null || !Emote.TryParse(emote, out var parsed))
                    return;

                try { await message.AddReactionAsync(parsed).ConfigureAwait(false); }
                catch (Exception ex) when (ex is HttpException or TimeoutException or NotSupportedException) { }
            }

            public static async Task<IUserMessage> AnimateAsync(
                IMessageChannel channel,
                IReadOnlyList<Embed> frames,
                TimeSpan delay,
                IUserMessage message = null)
            {
                ArgumentNullException.ThrowIfNull(channel);

                if (frames is not { Count: > 0 })
                    return message;

                for (var index = 0; index < frames.Count; index++)
                {
                    var frame = frames[index];

                    try
                    {
                        if (message is null)
                            message = await channel.SendMessageAsync(embed: frame).ConfigureAwait(false);
                        else
                            await message.ModifyAsync(properties => properties.Embed = frame).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is HttpException or TimeoutException)
                    {
                        return message;
                    }

                    if (index < frames.Count - 1)
                        await Task.Delay(delay).ConfigureAwait(false);
                }

                return message;
            }

            public static async Task<int> PurgeAsync(IMessageChannel channel, int count, ulong skipId = 0)
            {
                ArgumentNullException.ThrowIfNull(channel);

                if (count <= 0)
                    return 0;

                var messages = (await channel.GetMessagesAsync(count).FlattenAsync().ConfigureAwait(false))
                    .Where(message => message.Id != skipId)
                    .ToList();

                if (messages.Count == 0)
                    return 0;

                var threshold = DateTimeOffset.UtcNow - BulkDeleteLimit;
                var bulk = messages.Where(message => message.Timestamp > threshold).ToList();
                var single = messages.Except(bulk).ToList();
                var removed = 0;

                if (channel is ITextChannel text && bulk.Count > 1)
                {
                    try
                    {
                        await text.DeleteMessagesAsync(bulk).ConfigureAwait(false);
                        removed += bulk.Count;
                    }
                    catch (Exception ex) when (ex is HttpException or TimeoutException)
                    {
                        single.AddRange(bulk);
                    }
                }
                else
                {
                    single.AddRange(bulk);
                }

                foreach (var message in single)
                {
                    try
                    {
                        await channel.DeleteMessageAsync(message).ConfigureAwait(false);
                        removed++;
                    }
                    catch (Exception ex) when (ex is HttpException or TimeoutException) { }
                }

                return removed;
            }
        }

        public static class Text
        {
            public static string Truncate(string s, int max)
            {
                if (string.IsNullOrEmpty(s)) return s ?? "";
                s = s.Replace("\r", "");
                return s.Length <= max ? s : s.Substring(0, Math.Max(0, max - 1)) + "…";
            }

            public static string CollapseSpaces(string s)
            {
                if (string.IsNullOrEmpty(s)) return s ?? "";
                while (s.Contains("  ", StringComparison.Ordinal))
                    s = s.Replace("  ", " ");
                return s.Trim();
            }

            public static string TrackLink(string? title, string? url)
            {
                var label = HasVisible(title) ? title.Trim() : "Без названия..";
                label = label.Replace("[", "(").Replace("]", ")");
                return HasVisible(url) ? $"[{label}]({url.Trim()})" : label;
            }

            public static string Percent(double value, int digits) =>
                (value * 100).ToString($"0.{new string('0', digits)}", System.Globalization.CultureInfo.InvariantCulture) + "%";

            public static string FirstLine(string? s, int max = 300)
                => string.IsNullOrWhiteSpace(s) ? "" : Truncate(s.Split('\n', 2)[0].Trim(), max);

            public static string FirstMeaningfulLine(string? s, string fallback)
            {
                if (string.IsNullOrWhiteSpace(s))
                    return fallback;

                foreach (var line in s.Split('\n'))
                {
                    var trimmed = line.Trim();

                    if (trimmed.Length == 0 || trimmed.StartsWith("at ", StringComparison.Ordinal))
                        continue;

                    return trimmed;
                }

                return fallback;
            }

            public static string CodeTable(IEnumerable<(string Name, string Value)> rows, string empty)
                => CodeTable(rows.Select(row => (IReadOnlyList<string>)[row.Name, row.Value]), empty);

            public static string CodeTable(IEnumerable<IReadOnlyList<string>> rows, string empty)
            {
                var items = rows.Select(row => row.ToArray()).ToList();

                if (items.Count == 0)
                    return empty;

                var widths = new int[items.Max(row => row.Length)];

                foreach (var row in items)
                    for (var column = 0; column < row.Length; column++)
                        widths[column] = Math.Max(widths[column], row[column]?.Length ?? 0);

                var lines = items.Select(row => string.Join("   ", row.Select((cell, column) =>
                    column == row.Length - 1 ? cell ?? "" : (cell ?? "").PadRight(widths[column]))).TrimEnd());

                return "```\n" + string.Join("\n", lines) + "\n```";
            }

            private static bool HasVisible(string? s)
            {
                if (string.IsNullOrEmpty(s)) return false;
                foreach (var ch in s)
                {
                    if (char.IsWhiteSpace(ch) || char.IsControl(ch)) continue;
                    if (char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.Format) continue;
                    switch (ch)
                    {
                        case 'ㅤ': case '⠀': case 'ᅟ': case 'ᅠ':
                        case 'ﾠ': case '　': case '᠎': case '⁠': case '﻿':
                            continue;
                    }
                    return true;
                }
                return false;
            }
        }

        public static class Time
        {
            public static long ToUnix(DateTime utc)
                => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

            public static string FormatTime(TimeSpan time)
            {
                if (time.TotalDays >= 1)
                    return $"{(int)time.TotalDays}d {time:hh\\:mm\\:ss}";
                return time.ToString(@"hh\:mm\:ss");
            }

            public static string FormatAge(TimeSpan age) => age switch
            {
                { TotalSeconds: < 60 } => $"{age.TotalSeconds:0}с",
                { TotalMinutes: < 60 } => $"{age.TotalMinutes:0}м",
                { TotalHours: < 24 } => $"{age.TotalHours:0}ч",
                _ => $"{age.Days}д {age.Hours}ч"
            };

            public static bool TryParseTimecode(string input, out TimeSpan result)
            {
                result = default;

                if (string.IsNullOrWhiteSpace(input))
                    return false;

                var parts = input.Trim().Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length is < 2 or > 3)
                    return false;

                int hours = 0, minutes, seconds;

                if (parts.Length == 3 && !int.TryParse(parts[0], out hours))
                    return false;

                var offset = parts.Length == 3 ? 1 : 0;

                if (!int.TryParse(parts[offset], out minutes) || !int.TryParse(parts[offset + 1], out seconds))
                    return false;

                if (hours < 0 || minutes is < 0 or > 59 || seconds is < 0 or > 59)
                    return false;

                result = new TimeSpan(hours, minutes, seconds);
                return true;
            }
        }

        public static double Round(double value, int places)
        {
            long factor = (long)Math.Pow(10, places);
            long tmp = (long)Math.Round(value * factor);
            return (double)tmp / factor;
        }

        public static bool IsIoFailure(Exception ex) =>
            ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;

        public static int RandomNumber(int min, int max) => (int)Random.Shared.NextInt64(min, (long)max + 1);

        public static T RandomList<T>(this IList<T> items) => items[Random.Shared.Next(items.Count)];

        public static string GetScoreEmoji(double score)
        {
            if (score >= 91) return "<:KK90:1352292878252249191> <:KKplus:1352292867170631731>";
            if (score == 90) return "<:KK90:1352292878252249191>";
            if (score >= 75) return "<:KK90:1352292878252249191> <:KKminus:1352292880022114324>";
            if (score >= 61) return "<:KK60:1352292871260344400> <:KKplus:1352292867170631731>";
            if (score == 60) return "<:KK60:1352292871260344400>";
            if (score >= 45) return "<:KK60:1352292871260344400> <:KKminus:1352292880022114324>";
            if (score >= 31) return "<:KK30:1352292869179965544> <:KKplus:1352292867170631731>";
            if (score == 30) return "<:KK30:1352292869179965544>";
            return "<:KK30:1352292869179965544> <:KKminus:1352292880022114324>";
        }
    }
}
