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
    }
}
