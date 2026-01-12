using Discord;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;

namespace sblngavnav5X.Data
{

    public static class Utils
    {
        public const string sblnver = "5.5.1";

        public const string token = "INSERT_HERE";

        public const string pref1 = "x ";
        public const string pref2 = "х ";

        public static int govorUpdTime = 600000;

        public const int streamUpdTime = 600;

        public static string verbalMode = "выкл";

        public const int booksSeason = 2;

        public const string connectionString = "INSERT_HERE";

        public const string kumaConn = "INSERT_HERE";

        public const string weatherApiKey = "INSERT_HERE";

        public const string streamCid = "INSERT_HERE";

        public const string streamAuth = "INSERT_HERE";

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

        public static int RandomNumber(int min, int max)
        {
            Random random = new Random();
            return random.Next(min, max);
        }
        public static T RandomList<T>(this IList<T> items)
        {
            var random = new Random();

            return items[random.Next(items.Count)];
        }
    }
}
