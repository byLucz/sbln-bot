using Lavalink4NET.Rest.Entities.Tracks;
using System.Text;
using System.Text.RegularExpressions;
using sblngavnav6.Common;

namespace sblngavnav6.Audio8
{
    public readonly record struct Audio8QueryPlan(
        string Identifier,
        int PlaylistIndex,
        string FallbackIdentifier,
        string SelectedTrackId);

    internal static partial class Audio8Query
    {
        public const string YouTubePrefix = "ytsearch:";
        public const string SoundCloudPrefix = "scsearch:";
        public const string SpotifyPrefix = "spsearch:";
        public const string TtsPrefix = "ftts:";

        public static readonly (string Prefix, string Name)[] Fallbacks =
        [
            (YouTubePrefix, "YouTube"),
            (SoundCloudPrefix, "SoundCloud"),
            (SpotifyPrefix, "Spotify")
        ];

        public static string DisplayName(string prefix) => prefix switch
        {
            SpotifyPrefix => "Spotify",
            SoundCloudPrefix => "SoundCloud",
            _ => "YouTube"
        };

        public static string TryParsePrefix(string input) => input?.Trim().ToLowerInvariant() switch
        {
            "ютуб" or "ют" or "yt" or "youtube" => YouTubePrefix,
            "спотик" or "спотифай" or "споти" or "sp" or "spotify" => SpotifyPrefix,
            "склауд" or "саундклауд" or "ск" or "sc" or "soundcloud" => SoundCloudPrefix,
            _ => null
        };

        public static bool HasKnownPrefix(string query) =>
            query.StartsWith(YouTubePrefix, StringComparison.OrdinalIgnoreCase) ||
            query.StartsWith(SoundCloudPrefix, StringComparison.OrdinalIgnoreCase) ||
            query.StartsWith(SpotifyPrefix, StringComparison.OrdinalIgnoreCase) ||
            query.StartsWith(TtsPrefix, StringComparison.OrdinalIgnoreCase);

        public static string StripPrefix(string query)
        {
            foreach (var (prefix, _) in Fallbacks)
                if (query.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return query[prefix.Length..].Trim();

            return Uri.TryCreate(query, UriKind.Absolute, out _) ? string.Empty : query.Trim();
        }

        public static (string Name, bool IsUrl) Detect(string query)
        {
            if (query.StartsWith(YouTubePrefix, StringComparison.OrdinalIgnoreCase)) return ("YouTube", false);
            if (query.StartsWith(SoundCloudPrefix, StringComparison.OrdinalIgnoreCase)) return ("SoundCloud", false);
            if (query.StartsWith(SpotifyPrefix, StringComparison.OrdinalIgnoreCase)) return ("Spotify", false);
            if (query.StartsWith(TtsPrefix, StringComparison.OrdinalIgnoreCase)) return ("TTS", true);

            if (Uri.TryCreate(query, UriKind.Absolute, out var uri))
            {
                var host = uri.Host.ToLowerInvariant();
                if (host.Contains("youtu")) return ("YouTube", true);
                if (host.Contains("soundcloud")) return ("SoundCloud", true);
                if (host.Contains("spotify")) return ("Spotify", true);
                return ("ссылка", true);
            }

            return ("поиск", false);
        }

        public static TrackLoadOptions LoadOptions { get; } =
            new(SearchMode: TrackSearchMode.None, SearchBehavior: StrictSearchBehavior.Passthrough);

        private const int MaxVariants = 15;
        private const int MinPrefixLength = 4;
        private const int MaxPrefixLength = 10;

        private static readonly Dictionary<char, char> RuToEn = new()
        {
            ['й'] = 'q', ['ц'] = 'w', ['у'] = 'e', ['к'] = 'r', ['е'] = 't', ['н'] = 'y',
            ['г'] = 'u', ['ш'] = 'i', ['щ'] = 'o', ['з'] = 'p', ['х'] = '[', ['ъ'] = ']',
            ['ф'] = 'a', ['ы'] = 's', ['в'] = 'd', ['а'] = 'f', ['п'] = 'g', ['р'] = 'h',
            ['о'] = 'j', ['л'] = 'k', ['д'] = 'l', ['ж'] = ';', ['э'] = '\'', ['я'] = 'z',
            ['ч'] = 'x', ['с'] = 'c', ['м'] = 'v', ['и'] = 'b', ['т'] = 'n', ['ь'] = 'm',
            ['б'] = ',', ['ю'] = '.'
        };

        private static readonly Dictionary<char, char> EnToRu =
            RuToEn.ToDictionary(pair => pair.Value, pair => pair.Key);

        public static List<string> Variants(string input)
        {
            var source = CommonUtils.Text.CollapseSpaces(input?.Trim() ?? string.Empty);
            var variants = new List<string>();

            Add(variants, MapChars(source, RuToEn));
            Add(variants, MapChars(source, EnToRu));
            Add(variants, Transliterate(source));

            foreach (var prefix in PrefixCuts(source))
                Add(variants, prefix);

            return variants
                .Select(CommonUtils.Text.CollapseSpaces)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxVariants)
                .ToList();
        }

        private static IEnumerable<string> PrefixCuts(string source)
        {
            var start = Math.Min(MaxPrefixLength, source.Length);
            for (var length = start; length >= MinPrefixLength; length--)
                yield return source[..length];
        }

        private static void Add(List<string> target, string value)
        {
            value = CommonUtils.Text.CollapseSpaces(value);
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (!target.Contains(value, StringComparer.OrdinalIgnoreCase))
                target.Add(value);
        }

        private static string MapChars(string source, Dictionary<char, char> map)
        {
            var builder = new StringBuilder(source.Length);

            foreach (var symbol in source)
            {
                if (map.TryGetValue(char.ToLowerInvariant(symbol), out var replacement))
                    builder.Append(char.IsUpper(symbol) ? char.ToUpperInvariant(replacement) : replacement);
                else
                    builder.Append(symbol);
            }

            return builder.ToString();
        }

        private static string Transliterate(string source)
        {
            var builder = new StringBuilder(source.Length * 2);

            foreach (var symbol in source)
            {
                builder.Append(char.ToLowerInvariant(symbol) switch
                {
                    'а' => "a", 'б' => "b", 'в' => "v", 'г' => "g", 'д' => "d", 'е' => "e",
                    'ж' => "zh", 'з' => "z", 'и' => "i", 'й' => "y", 'к' => "k", 'л' => "l",
                    'м' => "m", 'н' => "n", 'о' => "o", 'п' => "p", 'р' => "r", 'с' => "s",
                    'т' => "t", 'у' => "u", 'ф' => "f", 'х' => "h", 'ц' => "ts", 'ч' => "ch",
                    'ш' => "sh", 'щ' => "sch", 'ы' => "y", 'э' => "e", 'ю' => "yu", 'я' => "ya",
                    'ь' => "", 'ъ' => "",
                    _ => symbol.ToString()
                });
            }

            return builder.ToString();
        }

        [GeneratedRegex(@"(https?://\S+)", RegexOptions.IgnoreCase)]
        private static partial Regex UrlRegex();

        [GeneratedRegex(@"^\s*(?:(?:х|и|играй)\s+)+", RegexOptions.IgnoreCase)]
        private static partial Regex LeadingCommandRegex();

        [GeneratedRegex(@"^\s*(?:споти|спотик)\s+", RegexOptions.IgnoreCase)]
        private static partial Regex SpotifyPrefixRegex();

        [GeneratedRegex(@"^\s*(?:склауд|саундклауд)\s+", RegexOptions.IgnoreCase)]
        private static partial Regex SoundCloudPrefixRegex();

        public static Audio8QueryPlan Normalize(string raw, string defaultPrefix)
        {
            var playlistIndex = 0;
            string fallback = null;
            string selectedTrackId = null;
            var query = raw ?? string.Empty;

            var urlMatch = UrlRegex().Match(query);
            query = urlMatch.Success
                ? urlMatch.Groups[1].Value.Trim().TrimEnd(')', ']', '}', '>', '.', ',', ';')
                : LeadingCommandRegex().Replace(query, string.Empty).Trim();

            if (query.StartsWith("ftts://", StringComparison.OrdinalIgnoreCase))
            {
                query = NormalizeTts(query).Replace("%", "%25", StringComparison.Ordinal);
                return new Audio8QueryPlan(query, 0, null, null);
            }

            if (query.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                query = query.Replace("youtu.be/", "youtube.com/watch?v=", StringComparison.OrdinalIgnoreCase);

            if (query.Contains("youtu", StringComparison.OrdinalIgnoreCase))
            {
                var firstQuestion = query.IndexOf('?');
                if (firstQuestion >= 0)
                    query = query[..(firstQuestion + 1)] + query[(firstQuestion + 1)..].Replace('?', '&');
            }

            if (Uri.TryCreate(query, UriKind.Absolute, out var uri) && IsYouTubeHost(uri.Host))
            {
                var parameters = ParseQuery(uri.Query);

                if (parameters.TryGetValue("list", out var listId) && !string.IsNullOrWhiteSpace(listId))
                {
                    if (parameters.TryGetValue("index", out var indexRaw) &&
                        int.TryParse(indexRaw, out var parsedIndex) && parsedIndex > 0)
                    {
                        playlistIndex = parsedIndex - 1;
                    }

                    if (parameters.TryGetValue("v", out var videoId) && !string.IsNullOrWhiteSpace(videoId))
                    {
                        selectedTrackId = videoId;
                        fallback = $"https://www.youtube.com/watch?v={videoId}";
                    }

                    query = $"https://www.youtube.com/playlist?list={listId}";
                }
            }

            var isUrl = Uri.TryCreate(query, UriKind.Absolute, out _);

            if (!isUrl && SpotifyPrefixRegex().IsMatch(query))
                query = Audio8Query.SpotifyPrefix + SpotifyPrefixRegex().Replace(query, string.Empty).Trim();
            else if (!isUrl && SoundCloudPrefixRegex().IsMatch(query))
                query = Audio8Query.SoundCloudPrefix + SoundCloudPrefixRegex().Replace(query, string.Empty).Trim();
            else if (!isUrl && !Audio8Query.HasKnownPrefix(query))
                query = defaultPrefix + query;

            return new Audio8QueryPlan(query, playlistIndex, fallback, selectedTrackId);
        }

        private static bool IsYouTubeHost(string host) =>
            host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

        private static string NormalizeTts(string query)
        {
            const string prefix = "ftts://";

            var rest = query[prefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(rest))
                return query;

            var optionsIndex = rest.IndexOf('?');
            var textPart = optionsIndex >= 0 ? rest[..optionsIndex] : rest;
            var optionsPart = optionsIndex >= 0 ? rest[optionsIndex..] : string.Empty;

            try { textPart = Uri.UnescapeDataString(textPart); }
            catch (UriFormatException) { }

            textPart = CommonUtils.Text.CollapseSpaces(textPart);

            return prefix + Uri.EscapeDataString(textPart) + optionsPart;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(query))
                return result;

            foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                result[Uri.UnescapeDataString(pair[0])] = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            }

            return result;
        }
    }
}
