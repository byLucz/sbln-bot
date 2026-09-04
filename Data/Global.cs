using Discord;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace sblngavnav5X.Data
{

    public static class Utils
    {
        public const string sblnver = "5.6.1";

        // Единый файл секретов вне репо. Путь: env SBLN_SECRETS, иначе дефолт /opt/sbln/secrets.json.
        // Туда же — хосты, зависящие от среды (LavaHost/MailImapHost). Абсолютный путь => не зависит от cwd.
        private const string DefaultSecretsPath = "/opt/sbln/secrets.json";

        private static readonly IConfiguration _config = BuildConfig();

        private static IConfiguration BuildConfig()
        {
            var path = Environment.GetEnvironmentVariable("SBLN_SECRETS");
            if (string.IsNullOrWhiteSpace(path))
                path = DefaultSecretsPath;

            var full = Path.GetFullPath(path);
            return new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(full))
                .AddJsonFile(Path.GetFileName(full), optional: false, reloadOnChange: false)
                .Build();
        }

        // Значение из secrets.json. Пусто/нет ключа → fallback.
        private static string Cfg(string key, string fallback = "")
        {
            var v = _config[key];
            return string.IsNullOrWhiteSpace(v) ? fallback : v;
        }

        public static readonly string token = Cfg("BotToken");

        public const string pref1 = "x ";

        public const string pref2 = "х ";

        public static readonly string connectionString = Cfg("DbConnectionString");

        public static readonly string kumaConn = Cfg("KumaConn");

        public static readonly string weatherApiKey = Cfg("WeatherApiKey");

        public static readonly string streamCid = Cfg("StreamCid");

        public static readonly string streamAuth = Cfg("StreamAuth");

        public static readonly string gBooksApi = Cfg("GBooksApi");

        public static readonly string pgApiBaseUrl = Cfg("PgApiBaseUrl");

        public static readonly string pgApiToken = Cfg("PgApiToken");


        public const int streamUpdTime = 600;

        public const string streamNotifCh = "twitch";

        public const ulong messageSourceChannelId = 11111111111;

        public const string messagesFilePath = "INSERT_HERE";

        public static int govorUpdTime = 86400000;

        public static string govorVM = "выкл";

        public const int booksSeason = 2;

        public const string booksJsonPath = "INSERT_HERE";

        // --- lavalink --- (host зависит от среды → secrets.json; остальное одинаково → const)
        public static readonly string lavaHost = Cfg("LavaHost", "127.0.0.1");   // docker: "lavalink"
        public const int lavaPort = 2333;
        public static readonly string lavaPass = Cfg("LavaPass", "youshallnotpass");

        // --- PechkinPostManager (temp mail) --- (host зависит от среды → secrets.json; остальное → const)
        public const string ppmDomain = "lois.media";
        public const string ppmContainer = "mailserver";                        // имя docker-контейнера docker-mailserver
        public static readonly string ppmImapHost = Cfg("MailImapHost", "127.0.0.1");   // docker: "host.docker.internal"
        public const int ppmImapPort = 993;
        public const bool ppmImapAllowInvalidCert = true;
        public const int ppmTtlMinutes = 15;

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
                return "<:KK90:1352292878252249191> <:KKplus:1352292867170631731>"; // 90+
            if (score == 90)
                return "<:KK90:1352292878252249191>"; // 90
            if (score >= 75)
                return "<:KK90:1352292878252249191> <:KKminus:1352292880022114324>"; // 90-
            if (score >= 61)
                return "<:KK60:1352292871260344400> <:KKplus:1352292867170631731>"; // 60+
            if (score == 60)
                return "<:KK60:1352292871260344400>"; // 60
            if (score >= 45)
                return "<:KK60:1352292871260344400> <:KKminus:1352292880022114324>"; // 60-
            if (score >= 31)
                return "<:KK30:1352292869179965544> <:KKplus:1352292867170631731>"; // 30+
            if (score == 30)
                return "<:KK30:1352292869179965544>"; // 30
            return "<:KK30:1352292869179965544> <:KKminus:1352292880022114324>"; // 30-
        }
        public static double Round(double value, int places)
        {

            long factor = (long)Math.Pow(10, places);
            value = value * factor;
            long tmp = (long)Math.Round(value);
            return (double)tmp / factor;
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

        public static int RandomNumber(int min, int max)
        {
            Random random = Random.Shared;
            return random.Next(min, max);
        }
        public static T RandomList<T>(this IList<T> items)
        {
            var random = Random.Shared;

            return items[random.Next(items.Count)];
        }
    }
}
