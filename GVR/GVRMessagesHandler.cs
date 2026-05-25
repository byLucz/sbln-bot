using Discord.Commands;
using sblngavnav5X.Data;
using sblngavnav5X.GVR;
using sblngavnav5X.Services;
using System.Text;
using System.Text.RegularExpressions;

namespace sblngavnav5X.Core
{
    public sealed class GVRMessagesHandler
    {
        private readonly GovorConfig _govorilka;

        private static readonly Regex SplitRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex UrlRegex = new(@"https?://", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PunctRegex = new(@"^[!?.,:;()\[\]/]+$", RegexOptions.Compiled);

        private static readonly string[] FunnyInterjections =
        {
            "лол", "лейм", "YZL", ")", "(((", "соси", "шефчик", "йоу", "чел"
        };

        public GVRMessagesHandler(GovorConfig govorilka)
        {
            _govorilka = govorilka;
        }

        public async Task TrySendGeneratedMessageAsync(SocketCommandContext context)
        {
            if (_govorilka.Rand)
                _govorilka.Count = Utils.RandomNumber(3, 20);

            if (Utils.RandomNumber(1, 101) > _govorilka.Chance)
                return;

            using (context.Channel.EnterTypingState())
            {
                await SendGeneratedMessageAsync(context, (int)_govorilka.Step, (int)_govorilka.Count);
            }
        }

        public async Task SendGeneratedMessageAsync(SocketCommandContext context, int step, int wordCount)
        {
            if (step <= 0 || wordCount <= 0)
                return;

            if (!File.Exists(Utils.messagesFilePath))
            {
                await LoggingService.LogInformationAsync("GOVOR", $"Файл {Utils.messagesFilePath} не найден, генерация ответа пропущена");
                return;
            }

            var rawLines = await File.ReadAllLinesAsync(Utils.messagesFilePath);
            if (rawLines.Length == 0)
                return;

            var sentences = ParseSentences(rawLines);
            if (sentences.Count == 0)
                return;

            var (chain, sentenceStarts) = MakeChain(sentences, step);
            if (chain.Count == 0)
                return;

            var generated = GenerateMessage(chain, sentenceStarts, step, wordCount);
            if (string.IsNullOrWhiteSpace(generated))
                return;

            await context.Channel.SendMessageAsync(generated);
        }

        private static List<List<string>> ParseSentences(IEnumerable<string> rawLines)
        {
            var result = new List<List<string>>();

            foreach (var line in rawLines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (UrlRegex.IsMatch(line)) continue;

                var sb = new StringBuilder();
                foreach (var ch in line)
                {
                    var s = ch.ToString();
                    sb.Append(PunctRegex.IsMatch(s) ? $" {s} " : s);
                }

                var words = SplitRegex
                    .Split(sb.ToString().ToLowerInvariant())
                    .Where(w => !string.IsNullOrWhiteSpace(w) && w != "x")
                    .ToList();

                if (words.Count >= 2)
                    result.Add(words);
            }

            return result;
        }

        private static (Dictionary<string, List<string>> chain, List<string> sentenceStarts)
            MakeChain(List<List<string>> sentences, int step)
        {
            var chain = new Dictionary<string, List<string>>();
            var sentenceStarts = new List<string>();

            foreach (var words in sentences)
            {
                if (words.Count <= step) continue;

                var startKey = string.Join(" ", words.Take(step));
                sentenceStarts.Add(startKey);

                for (int i = 0; i < words.Count - step; i++)
                {
                    var key = string.Join(" ", words.Skip(i).Take(step));
                    var value = words[i + step];

                    if (!chain.TryGetValue(key, out var list))
                    {
                        list = new List<string>();
                        chain[key] = list;
                    }
                    list.Add(value);
                }
            }

            return (chain, sentenceStarts);
        }

        private string GenerateMessage(
            Dictionary<string, List<string>> chain,
            List<string> sentenceStarts,
            int step,
            int wordCount)
        {
            int sentenceCount = wordCount <= 5 ? 1 : wordCount <= 12 ? Random.Shared.Next(1, 3) : Random.Shared.Next(1, 4);
            int baseWords = wordCount / sentenceCount;

            var parts = new List<string>();
            for (int s = 0; s < sentenceCount; s++)
            {
                int w = s == sentenceCount - 1 ? wordCount - baseWords * s : baseWords;
                var sentence = GenerateSentence(chain, sentenceStarts, step, Math.Max(w, 3));
                if (!string.IsNullOrWhiteSpace(sentence))
                    parts.Add(sentence);
            }

            if (parts.Count == 0)
                return string.Empty;

            if (parts.Count > 1 && Random.Shared.NextDouble() < 0.25)
            {
                var funny = FunnyInterjections[Random.Shared.Next(FunnyInterjections.Length)];
                parts.Insert(Random.Shared.Next(1, parts.Count), funny);
            }

            return string.Join(" ", parts);
        }

        private string GenerateSentence(
            Dictionary<string, List<string>> chain,
            List<string> sentenceStarts,
            int step,
            int wordCount)
        {
            var startKey = sentenceStarts.Count > 0
                ? sentenceStarts[Random.Shared.Next(sentenceStarts.Count)]
                : chain.ElementAt(Random.Shared.Next(chain.Count)).Key;

            if (!chain.ContainsKey(startKey))
                startKey = chain.ElementAt(Random.Shared.Next(chain.Count)).Key;

            var temp = new List<string>(startKey.Split(' '));
            var result = new StringBuilder();

            foreach (var w in temp)
                result.Append(result.Length == 0 ? w : $" {w}");

            for (int i = 0; i < wordCount; i++)
            {
                var key = string.Join(" ", temp.Skip(Math.Max(0, temp.Count - step)).Take(step));

                if (!chain.ContainsKey(key))
                {
                    bool found = false;
                    for (int back = 1; back < step; back++)
                    {
                        var keyParts = key.Split(' ');
                        if (keyParts.Length <= back) break;
                        var shorter = string.Join(" ", keyParts.Skip(back));
                        if (chain.ContainsKey(shorter)) { key = shorter; found = true; break; }
                    }
                    if (!found)
                        key = chain.ElementAt(Random.Shared.Next(chain.Count)).Key;
                }

                var values = chain[key];
                var value = values[Random.Shared.Next(values.Count)];
                temp.Add(value);

                if (PunctRegex.IsMatch(value))
                    result.Append(value);
                else if (_govorilka.VerbalAbuseBySheff)
                    result.Append($" {value} бля");
                else
                    result.Append($" {value}");

                if (value is "." or "!" or "?")
                    break;
            }

            return result.ToString().Trim();
        }
    }
}
