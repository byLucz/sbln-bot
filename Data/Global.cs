using Microsoft.Extensions.Configuration;

namespace sblngavnav5X.Data
{
    public static class Utils
    {
        public const string sblnver = "5X.SUP";

        private static readonly IConfiguration _config = BuildConfig();

        private static IConfiguration BuildConfig()
        {
            var path = Environment.GetEnvironmentVariable("SBLN_V5X_CONFIG");
            if (string.IsNullOrWhiteSpace(path))
                path = File.Exists("/opt/sbln/config-v5x.json") ? "/opt/sbln/config-v5x.json" : Path.Combine("config", "config-v5x.json");

            var full = Path.GetFullPath(path);
            if (!File.Exists(full))
                throw new FileNotFoundException($"нет файла {full} (задай SBLN_V5X_CONFIG)", full);

            return new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(full))
                .AddJsonFile(Path.GetFileName(full), optional: false, reloadOnChange: false)
                .Build();
        }

        private static string Str(string key, string fallback = "")
        {
            var v = _config[key];
            return string.IsNullOrWhiteSpace(v) ? fallback : v;
        }

        private static ulong UL(string key, ulong fallback = 0)
            => ulong.TryParse(_config[key], out var v) ? v : fallback;

        private static int Int(string key, int fallback)
            => int.TryParse(_config[key], out var v) ? v : fallback;

        private static bool Bool(string key, bool fallback)
            => bool.TryParse(_config[key], out var v) ? v : fallback;

        private static string DataPath(string key, string fileName)
            => Str(key, Path.Combine(DataDir, fileName));

        public static readonly string DataDir =
            Environment.GetEnvironmentVariable("SBLN_DATA_DIR") is { Length: > 0 } dir ? dir : AppContext.BaseDirectory;

        public static readonly string token = Str("System:BotToken");
        public static readonly string pref1 = Str("System:Prefix1", "x ");
        public static readonly string pref2 = Str("System:Prefix2", "х ");
        public static readonly string connectionString = Str("System:DbConnectionString");

        public static readonly string weatherApiKey = Str("Api:WeatherApiKey");
        public static readonly string streamCid = Str("Api:StreamCid");
        public static readonly string streamAuth = Str("Api:StreamAuth");
        public static readonly string gBooksApi = Str("Api:GBooksApi");

        public static readonly string lavaHost = Str("Lava:LavaHost", "127.0.0.1");
        public static readonly int lavaPort = Int("Lava:LavaPort", 2333);
        public static readonly string lavaPass = Str("Lava:LavaPass", "youshallnotpass");
        public static readonly string lavaAudioPath =
            Environment.GetEnvironmentVariable("SBLN_AUDIO_DIR_LAVA") ?? Str("Lava:AudioPath");

        public static readonly int streamUpdTime = Math.Max(60, Int("Streams:UpdateSeconds", 600));
        public static readonly string streamNotifCh = Str("Streams:NotifChannel", "twitch");

        public static readonly string welcomeChannel = Str("Welcome:Channel", "mainlobby");
        public static readonly string welcomeRole = Str("Welcome:Role", "Челик");

        public static readonly ulong messageSourceChannelId = UL("Govor:MessageSourceChannelId");
        public static readonly string messagesFilePath = DataPath("Govor:MessagesFile", "messages.csv");

        public static int govorUpdTime = Math.Max(1000, Int("Govor:IntervalMs", 86400000));

        public static string govorVM = "выкл";

        public static readonly int booksSeason = Int("Books:BooksSeason", 2);

        public static class Modules
        {
            public static readonly bool Music = Bool("Modules:Music", true);
            public static readonly bool Govor = Bool("Modules:Govor", true);
            public static readonly bool Streams = Bool("Modules:Streams", true);
            public static readonly bool Books = Bool("Modules:Books", true);
            public static readonly bool Weather = Bool("Modules:Weather", true);
            public static readonly bool Welcome = Bool("Modules:Welcome", true);
            public static readonly bool Meal = Bool("Modules:Meal", true);
            public static readonly bool Fun = Bool("Modules:Fun", true);
            public static readonly bool Games = Bool("Modules:Games", true);
        }

        public static string[] greetList = new[]
        {
            "поздравляю!",
            "соболезную!"
        };

        public static string[] rotatingNumbers = new[]
        {
            "<:slyrHead:779359060225949757>",
            "<:slyr2head:779363223467458571>",
            "<:slyrGdetvoyasamoironiya:800698140021358612>"
        };

        public static string GetScoreEmoji(double score)
        {
            if (score >= 91)
                return "<:KK90:1352292878252249191> <:KKplus:1352292867170631731>";
            if (score == 90)
                return "<:KK90:1352292878252249191>";
            if (score >= 75)
                return "<:KK90:1352292878252249191> <:KKminus:1352292880022114324>";
            if (score >= 61)
                return "<:KK60:1352292871260344400> <:KKplus:1352292867170631731>";
            if (score == 60)
                return "<:KK60:1352292871260344400>";
            if (score >= 45)
                return "<:KK60:1352292871260344400> <:KKminus:1352292880022114324>";
            if (score >= 31)
                return "<:KK30:1352292869179965544> <:KKplus:1352292867170631731>";
            if (score == 30)
                return "<:KK30:1352292869179965544>";
            return "<:KK30:1352292869179965544> <:KKminus:1352292880022114324>";
        }

        public static double Round(double value, int places) => Math.Round(value, places);

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

        public static int RandomNumber(int min, int max) => Random.Shared.Next(min, max);

        public static T RandomList<T>(this IList<T> items) => items[Random.Shared.Next(items.Count)];
    }
}
