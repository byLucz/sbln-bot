namespace sblngavnav5X.Common
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
        }

        public static double Round(double value, int places)
        {
            long factor = (long)Math.Pow(10, places);
            long tmp = (long)Math.Round(value * factor);
            return (double)tmp / factor;
        }

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
