namespace sblngavnav6.Common
{
    public static class CommonUtils
    {
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
                _ => $"{age.TotalHours:0}ч"
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

        public static int RandomNumber(int min, int max) => Random.Shared.Next(min, max);

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
