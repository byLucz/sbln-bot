using System.Text.RegularExpressions;

namespace sblngavnav6.GVR
{
    internal static partial class GVRText
    {
        private const int MinWords = 2;
        private const int MaxLength = 1024;
        private const char Guard = '\u0001';

        public static string Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var kept = new List<string>();

            var text = InvisibleRegex().Replace(raw, string.Empty);

            text = DiscordTokenRegex().Replace(text, match =>
            {
                kept.Add(match.Value);
                return $"{Guard}{kept.Count - 1}{Guard}";
            });

            text = BulletRegex().Replace(text, " ");
            text = LinkRegex().Replace(text, " ");
            text = SnowflakeRegex().Replace(text, " ");
            text = AngleJunkRegex().Replace(text, " ");
            text = SpaceRegex().Replace(text, " ").Trim();

            text = GuardRegex().Replace(text, match => kept[int.Parse(match.Groups[1].Value)]);

            if (text.Length == 0 || text.Length > MaxLength)
                return null;

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length < MinWords || LooksLikeList(text) || LooksLikeBotList(words))
                return null;

            return text;
        }

        private static bool LooksLikeList(string text)
        {
            var pipes = text.Count(symbol => symbol is '|');
            var handles = text.Count(symbol => symbol is '@');

            return pipes >= 3 || handles >= 3;
        }

        private static bool LooksLikeBotList(string[] words)
        {
            var repeats = words
                .GroupBy(word => word, StringComparer.OrdinalIgnoreCase)
                .Max(group => group.Count());

            if (repeats >= 3)
                return true;

            if (words.Length >= 4 && HasRepeatedPair(words))
                return true;

            var emoji = words.Count(IsEmojiOnly);

            return words.Length >= 5 && emoji * 2 >= words.Length;
        }

        private static bool HasRepeatedPair(string[] words)
        {
            var pairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index + 1 < words.Length; index++)
                if (!pairs.Add($"{words[index]} {words[index + 1]}"))
                    return true;

            return false;
        }

        private static bool IsEmojiOnly(string word) =>
            word.Length > 0 && word.All(symbol => char.IsSurrogate(symbol) || symbol > '\u2100');

        [GeneratedRegex(@"<a?:\w+:\d+>|<@[!&]?\d+>|<#\d+>|<t:\d+(?::[a-zA-Z])?>|</[\w -]+:\d+>", RegexOptions.Compiled)]
        private static partial Regex DiscordTokenRegex();

        [GeneratedRegex(@"\b\d{15,20}\b", RegexOptions.Compiled)]
        private static partial Regex SnowflakeRegex();

        [GeneratedRegex(@"[<>]", RegexOptions.Compiled)]
        private static partial Regex AngleJunkRegex();

        [GeneratedRegex(@"[•·‣▪●◦⁃∙]", RegexOptions.Compiled)]
        private static partial Regex BulletRegex();

        [GeneratedRegex(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
        private static partial Regex LinkRegex();

        [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F­​-‏‪-‮⁠﻿]", RegexOptions.Compiled)]
        private static partial Regex InvisibleRegex();

        [GeneratedRegex(@"\u0001(\d+)\u0001", RegexOptions.Compiled)]
        private static partial Regex GuardRegex();

        [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
        private static partial Regex SpaceRegex();
    }
}
