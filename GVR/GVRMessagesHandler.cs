using sblngavnav6.Common;
using Discord;
using Discord.Commands;
using sblngavnav6.Services;
using System.Text;
using System.Text.RegularExpressions;

namespace sblngavnav6.GVR
{
    public sealed class GVRMessagesHandler
    {
        private readonly GVRConfig _config;
        private readonly GVRDb _db;

        private static readonly Regex SplitRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex PunctRegex = new(@"^[!?.,;]+$", RegexOptions.Compiled);

        private static readonly string[] FunnyInterjections =
        {
            "лол", "лейм", "YZL", ")", "(((", "соси", "шефчик", "йоу", "чел"
        };

        private readonly SemaphoreSlim _modelGate = new(1, 1);
        private Model _model;
        private int _modelStep;
        private int _modelCount;
        private long _modelMaxId;
        private long _modelRevision;

        public GVRMessagesHandler(GVRConfig config, GVRDb db)
        {
            _config = config;
            _db = db;
        }

        public async Task TrySendGeneratedMessageAsync(SocketCommandContext context)
        {
            if (CommonUtils.RandomNumber(1, 101) > _config.Chance)
                return;

            var words = _config.Rand
                ? CommonUtils.RandomNumber(3, 20)
                : _config.Count;

            using (context.Channel.EnterTypingState())
            {
                await SendGeneratedMessageAsync(context, (int)_config.Step, words);
            }
        }

        public async Task SendGeneratedMessageAsync(SocketCommandContext context, int step, int wordCount)
        {
            if (step <= 0 || wordCount <= 0)
                return;

            var model = await GetModelAsync(step).ConfigureAwait(false);
            if (model is null)
                return;

            var generated = GenerateMessage(model, step, wordCount);
            if (string.IsNullOrWhiteSpace(generated))
                return;

            if (generated.Length > 2000)
                generated = generated[..2000];

            await context.Channel.SendMessageAsync(generated, allowedMentions: AllowedMentions.None);
        }

        private async Task<Model> GetModelAsync(int step)
        {
            var (count, maxId) = await _db.StampAsync().ConfigureAwait(false);

            if (count == 0)
                return null;

            var revision = _db.Revision;

            if (IsFresh(step, count, maxId, revision))
                return _model;

            await _modelGate.WaitAsync().ConfigureAwait(false);

            try
            {
                if (IsFresh(step, count, maxId, revision))
                    return _model;

                var rawLines = await _db.LoadAsync().ConfigureAwait(false);

                if (rawLines.Count == 0)
                    return null;

                var sentences = ParseSentences(rawLines);

                if (sentences.Count == 0)
                    return null;

                var built = MakeChain(sentences, step);

                if (built is null)
                    return null;

                _model = built;
                _modelStep = step;
                _modelCount = count;
                _modelMaxId = maxId;
                _modelRevision = revision;

                await LoggingService.LogDebugAsync(
                    "GOVOR",
                    $"Цепь пересобрана: строк {rawLines.Count}, ключей {built.Keys.Length}, шаг {step}");

                return _model;
            }
            finally
            {
                _modelGate.Release();
            }
        }

        private bool IsFresh(int step, int count, long maxId, long revision) =>
            _model is not null && _modelStep == step && _modelCount == count && _modelMaxId == maxId && _modelRevision == revision;

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

        private static Model MakeChain(List<List<string>> sentences, int step)
        {
            var chain = new Dictionary<string, List<string>>();
            var starts = new List<string>();

            foreach (var words in sentences)
            {
                if (words.Count <= step) continue;

                if (!PunctRegex.IsMatch(words[0]))
                    starts.Add(string.Join(" ", words.Take(step)));

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

            return chain.Count == 0 ? null : new Model(chain, starts, chain.Keys.ToArray());
        }

        private string GenerateMessage(Model model, int step, int wordCount)
        {
            int sentenceCount = wordCount <= 5 ? 1 : wordCount <= 12 ? Random.Shared.Next(1, 3) : Random.Shared.Next(1, 4);
            int baseWords = wordCount / sentenceCount;

            var parts = new List<string>();
            for (int s = 0; s < sentenceCount; s++)
            {
                int w = s == sentenceCount - 1 ? wordCount - baseWords * s : baseWords;
                var sentence = GenerateSentence(model, step, Math.Max(w, 3));
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

            return Polish(string.Join(" ", parts));
        }

        private string GenerateSentence(Model model, int step, int wordCount)
        {
            var chain = model.Chain;

            var startKey = model.Starts.Count > 0
                ? model.Starts[Random.Shared.Next(model.Starts.Count)]
                : model.RandomKey();

            if (!chain.ContainsKey(startKey))
                startKey = model.RandomKey();

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
                        key = model.RandomKey();
                }

                var value = Choose(chain[key], temp);

                if (value is null)
                    break;

                temp.Add(value);

                if (PunctRegex.IsMatch(value))
                    result.Append(value);
                else if (_config.VerbalAbuseBySheff)
                    result.Append($" {value} бля");
                else
                    result.Append($" {value}");

                if (value is "." or "!" or "?")
                    break;
            }

            return Polish(result.ToString());
        }

        private static string Choose(List<string> values, List<string> history)
        {
            var previous = history.Count > 0 ? history[^1] : null;
            var beforeThat = history.Count > 1 ? history[^2] : null;

            List<string> distinct = null;

            for (var attempt = 0; attempt < 4; attempt++)
            {
                string value;

                if (Surprise())
                {
                    distinct ??= values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    value = distinct[Random.Shared.Next(distinct.Count)];
                }
                else
                {
                    value = values[Random.Shared.Next(values.Count)];
                }

                if (string.Equals(value, previous, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (beforeThat is not null && string.Equals(value, beforeThat, StringComparison.OrdinalIgnoreCase))
                    continue;

                return value;
            }

            return values.FirstOrDefault(value => !string.Equals(value, previous, StringComparison.OrdinalIgnoreCase));
        }

        private static bool Surprise() => Random.Shared.NextDouble() < 0.2;

        private static string Polish(string sentence)
        {
            sentence = sentence.Trim().TrimStart('.', ',', ';', '!', '?', ' ');
            sentence = sentence.TrimEnd(' ', ',', ';', '-', '–', '—');

            return sentence;
        }

        private sealed record Model(Dictionary<string, List<string>> Chain, List<string> Starts, string[] Keys)
        {
            public string RandomKey() => Keys[Random.Shared.Next(Keys.Length)];
        }
    }
}
