using Microsoft.Extensions.Configuration;

namespace sblngavnav5X.Data
{
    public static class Global
    {
        public static class Vars
        {
            public static class Cfg
            {
                private const string DefaultConfigPath = "/opt/sbln/config.json";

                private static readonly IConfiguration _config = BuildConfig();

                private static IConfiguration BuildConfig()
                {
                    var path = Environment.GetEnvironmentVariable("SBLN_CONFIG");
                    if (string.IsNullOrWhiteSpace(path))
                        path = DefaultConfigPath;

                    var full = Path.GetFullPath(path);
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

                private static ulong UL(string key, ulong fallback)
                    => ulong.TryParse(_config[key], out var v) ? v : fallback;

                private static int Int(string key, int fallback)
                    => int.TryParse(_config[key], out var v) ? v : fallback;

                private static bool Bool(string key, bool fallback)
                    => bool.TryParse(_config[key], out var v) ? v : fallback;

                public static readonly string token = Str("System:BotToken");
                public static readonly string connectionString = Str("System:DbConnectionString");
                public static readonly string pref1 = Str("System:Prefix1", "x ");
                public static readonly string pref2 = Str("System:Prefix2", "х ");
                public static readonly ulong slashScopeGuild = UL("System:SlashScopeGuild", 0);
                public static readonly bool streamsEnabled = Bool("System:StreamsEnabled", true);
                public static readonly ulong messageSourceChannelId = UL("System:MessageSourceChannelId", 0);
                public static readonly string messagesFilePath = Str("System:MessagesFilePath");

                public static readonly string kumaConn = Str("Api:KumaConn");
                public static readonly string weatherApiKey = Str("Api:WeatherApiKey");
                public static readonly string streamCid = Str("Api:StreamCid");
                public static readonly string streamAuth = Str("Api:StreamAuth");
                public static readonly string gBooksApi = Str("Api:GBooksApi");
                public static readonly string pgApiBaseUrl = Str("Api:PgApiBaseUrl");
                public static readonly string pgApiToken = Str("Api:PgApiToken");

                public static readonly string lavaHost = Str("Lava:LavaHost", "127.0.0.1");
                public static readonly string lavaPass = Str("Lava:LavaPass", "youshallnotpass");
                public static readonly int lavaPort = Int("Lava:LavaPort", 2333);

                public static readonly string ppmImapHost = Str("Mail:MailImapHost", "127.0.0.1");
                public static readonly int ppmImapPort = Int("Mail:MailImapPort", 993);
                public static readonly bool ppmImapAllowInvalidCert = Bool("Mail:MailImapAllowInvalidCert", true);
                public static readonly string ppmDomain = Str("Mail:MailDomain", "example.com");
                public static readonly string ppmContainer = Str("Mail:MailContainer", "mailserver");
                public static readonly int ppmTtlMinutes = Int("Mail:MailTtlMinutes", 15);

                public static readonly int booksSeason = Int("Books:BooksSeason", 2);
                public static readonly string booksJsonPath = Str("Books:BooksJsonPath");
            }

            public static class BuiltIn
            {
                public const int streamUpdTime = 600;
                public const string streamNotifCh = "twitch";
                public static int govorUpdTime = 86400000;
                public static string govorVM = "выкл";

                public static readonly string[] greetList =
                {
                    "поздравляю!",
                    "соболезную!"
                };

                public static readonly string[] rotatingNumbers =
                {
                    "<:slyrHead:779359060225949757>",
                    "<:slyr2head:779363223467458571>",
                    "<:slyrGdetvoyasamoironiya:800698140021358612>"
                };
            }
        }
    }
}
