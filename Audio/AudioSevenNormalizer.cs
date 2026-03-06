using System.Text.RegularExpressions;

namespace sblngavnav5X.Audio
{
    internal static class AudioQueryNormalizer
    {
        private static readonly Regex UrlRegex =
            new(@"(https?://\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex LeadingCommandRegex =
            new(@"^\s*(?:(?:х|и|играй)\s+)+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SpotifyPrefixRegex =
            new(@"^\s*(?:споти|спотик)\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SoundCloudPrefixRegex =
            new(@"^\s*(?:склауд|саундклауд)\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex YandexPrefixRegex =
            new(@"^\s*(?:яндекс|санкции)\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string Normalize(string raw, out int playlistIndex)
        {
            playlistIndex = 0;

            var searchQuery = raw;

            var m = UrlRegex.Match(searchQuery);
            if (m.Success)
                searchQuery = m.Groups[1].Value.Trim().TrimEnd(')', ']', '}', '>', '.', ',', ';');
            else
                searchQuery = LeadingCommandRegex.Replace(searchQuery, "").Trim();

            if (searchQuery.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                searchQuery = searchQuery.Replace("youtu.be/", "youtube.com/watch?v=", StringComparison.OrdinalIgnoreCase);

            if (Uri.TryCreate(searchQuery, UriKind.Absolute, out var uri) &&
                (uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.Contains("music.youtube.com", StringComparison.OrdinalIgnoreCase)))
            {
                var q = ParseQuery(uri.Query);

                if (q.TryGetValue("list", out var listId) && !string.IsNullOrWhiteSpace(listId))
                {
                    if (q.TryGetValue("index", out var idxStr) && int.TryParse(idxStr, out var parsed) && parsed > 0)
                        playlistIndex = parsed - 1;

                    searchQuery = $"https://www.youtube.com/playlist?list={listId}";
                }
            }

            if (!Uri.TryCreate(searchQuery, UriKind.Absolute, out _) && SpotifyPrefixRegex.IsMatch(searchQuery))
            {
                var q = SpotifyPrefixRegex.Replace(searchQuery, "").Trim();
                searchQuery = "spsearch:" + q;
            }

            if (!Uri.TryCreate(searchQuery, UriKind.Absolute, out _) && SoundCloudPrefixRegex.IsMatch(searchQuery))
            {
                var q = SoundCloudPrefixRegex.Replace(searchQuery, "").Trim();
                searchQuery = "scsearch:" + q;
            }

            if (!Uri.TryCreate(searchQuery, UriKind.Absolute, out _) && YandexPrefixRegex.IsMatch(searchQuery))
            {
                var q = YandexPrefixRegex.Replace(searchQuery, "").Trim();
                searchQuery = "ymsearch:" + q;
            }

            if (!Uri.TryCreate(searchQuery, UriKind.Absolute, out _) && !HasKnownSearchPrefix(searchQuery))
                searchQuery = "ytsearch:" + searchQuery;

            return searchQuery;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(query)) return dict;

            foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                var key = Uri.UnescapeDataString(kv[0]);
                var val = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
                dict[key] = val;
            }

            return dict;
        }

        private static bool HasKnownSearchPrefix(string q)
        {
            return q.StartsWith("ytsearch:", StringComparison.OrdinalIgnoreCase) ||
                   q.StartsWith("scsearch:", StringComparison.OrdinalIgnoreCase) ||
                   q.StartsWith("ymsearch:", StringComparison.OrdinalIgnoreCase) ||
                   q.StartsWith("spsearch:", StringComparison.OrdinalIgnoreCase);
        }
    }
}