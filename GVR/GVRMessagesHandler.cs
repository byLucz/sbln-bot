using sblngavnav6.Common;
using Discord;
using Discord.Commands;
using sblngavnav6.Data;
using sblngavnav6.GVR;
using sblngavnav6.Services;
using System.Text;
using System.Text.RegularExpressions;

namespace sblngavnav6.Core
{
    public sealed class GVRMessagesHandler
    {
        private readonly GVRConfig _govorilka;
        private readonly GVRDb _db;

        private static readonly Regex SplitRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex UrlRegex = new(@"https?://", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PunctRegex = new(@"^[!?.,;]+$", RegexOptions.Compiled);

        private static readonly string[] FunnyInterjections =
        {
            "лол", "лейм", "YZL", ")", "(((", "соси", "шефчик", "йоу", "чел"
        };

        public GVRMessagesHandler(GVRConfig govorilka, GVRDb db)
        {
            _govorilka = govorilka;
            _db = db;
        }

        public async Task TrySendGeneratedMessageAsync(SocketCommandContext context)
        {
            if (CommonUtils.RandomNumber(1, 101) > _govorilka.Chance)
                return;

            var words = _govorilka.Rand
                ? CommonUtils.RandomNumber(3, 20)
                : _govorilka.Count;

            using (context.Channel.EnterTypingState())
            {
                await SendGeneratedMessageAsync(context, (int)_govorilka.Step, words);
            }
        }

        public async Task SendGeneratedMessageAsync(SocketCommandContext context, int step, int wordCount)
        {
            if (step <= 0 || wordCount <= 0)
                return;

            var model = await GetModelAsync(step).ConfigureAwait(false);
            if (model is null)
                return;

            var (chain, sentenceStarts) = model.Value;

            var generated = GenerateMessage(chain, sentenceStarts, step, wordCount);
            if (string.IsNullOrWhiteSpace(generated))
                return;

            if (generated.Length > 2000)
                generated = generated[..2000];

            await context.Channel.SendMessageAsync(generated, allowedMentions: AllowedMentions.None);
        }

        private readonly SemaphoreSlim _modelGate = new(1, 1);
        private (Dictionary<string, List<string>> Chain, List<string> Starts)? _model;
        private int _modelStep;
        private int _modelCount;
        private long _modelMaxId;

        private async Task<(Dictionary<string, List<string>> Chain, List<string> Starts)?> GetModelAsync(int step)
        {
            var (count, maxId) = await _db.StampAsync().ConfigureAwait(false);

            if (count == 0)
                return null;

            if (_model is { } cached && _modelStep == step && _modelCount == count && _modelMaxId == maxId)
                return cached;

            await _modelGate.WaitAsync().ConfigureAwait(false);

            try
            {
                if (_model is { } fresh && _modelStep == step && _modelCount == count && _modelMaxId == maxId)
                    return fresh;

                var rawLines = await _db.LoadAsync().ConfigureAwait(false);

                if (rawLines.Count == 0)
                    return null;

                var sentences = ParseSentences(rawLines);

                if (sentences.Count == 0)
                    return null;

                var built = MakeChain(sentences, step);

                if (built.chain.Count == 0)
                    return null;

                _model = (built.chain, built.sentenceStarts);
                _modelStep = step;
                _modelCount = count;
                _modelMaxId = maxId;

                await LoggingService.LogDebugAsync(
                    "GOVOR",
                    $"Цепь пересобрана: строк {rawLines.Count}, ключей {built.chain.Count}, шаг {step}");

                return _model;
            }
            finally
            {
                _modelGate.Release();
            }
        }

        private static List<List<string>> ParseSentences(IEnumerable<string> rawLines)
        {
            var result = new List<List<string>>();

            foreach (var raw in rawLines)
            {
                var line = GVRText.Sanitize(raw);

                if (line is null)
                    continue;

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

        private static string Choose(List<string> values, List<string> history)
        {
            var previous = history.Count > 0 ? history[^1] : null;
            var beforeThat = history.Count > 1 ? history[^2] : null;

            for (var attempt = 0; attempt < 4; attempt++)
            {
                var value = Surprise()
                    ? values.Distinct(StringComparer.OrdinalIgnoreCase).ElementAt(Random.Shared.Next(values.Distinct(StringComparer.OrdinalIgnoreCase).Count()))
                    : values[Random.Shared.Next(values.Count)];

                if (string.Equals(value, previous, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (beforeThat is not null && string.Equals(value, beforeThat, StringComparison.OrdinalIgnoreCase))
                    continue;

                return value;
            }

            return values.FirstOrDefault(value => !string.Equals(value, previous, StringComparison.OrdinalIgnoreCase));
        }

        private static bool Surprise() => Random.Shared.NextDouble() < 0.2;

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

                var value = Choose(chain[key], temp);

                if (value is null)
                    break;

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
