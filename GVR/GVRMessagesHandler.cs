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

        private const string MessagesFilePath = "messages.csv";

        private static readonly Regex SplitRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex UrlRegex = new(@"https?://", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PunctuationRegex = new(@"[!?.,:;()\[\]/]+", RegexOptions.Compiled);
        private static readonly Regex StartPunctuationRegex = new(@"[х!?.,;()\[\]/]+", RegexOptions.Compiled);

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

            if (!File.Exists(MessagesFilePath))
            {
                await LoggingService.LogInformationAsync("GOVOR", "Файл messages.csv не найден, генерация ответа пропущена");
                return;
            }

            var messages = await File.ReadAllLinesAsync(MessagesFilePath);
            if (messages.Length == 0)
                return;

            var filtered = FilterMessages(messages);
            if (filtered.Count == 0)
                return;

            var chain = MakeChain(filtered, step);
            if (chain.Count == 0)
                return;

            var generated = GenerateMessage(chain, step, wordCount);
            if (string.IsNullOrWhiteSpace(generated))
                return;

            await context.Channel.SendMessageAsync(generated);
        }

        private static List<string> FilterMessages(IEnumerable<string> messages)
        {
            var filtered = new List<string>();

            foreach (var msg in messages)
            {
                if (string.IsNullOrWhiteSpace(msg))
                    continue;

                if (UrlRegex.IsMatch(msg))
                    continue;

                var sb = new StringBuilder();

                foreach (var ch in msg)
                {
                    var symbol = ch.ToString();
                    sb.Append(PunctuationRegex.IsMatch(symbol) ? $" {symbol} " : symbol);
                }

                var normalized = sb.ToString();

                if (normalized.Contains(" x ", StringComparison.OrdinalIgnoreCase))
                    continue;

                var split = SplitRegex
                    .Split(normalized.ToLowerInvariant())
                    .Where(x => !string.IsNullOrWhiteSpace(x));

                filtered.AddRange(split);
            }

            return filtered;
        }

        private static Dictionary<string, List<string>> MakeChain(List<string> filtered, int step)
        {
            var chain = new Dictionary<string, List<string>>();

            for (var i = 0; i < filtered.Count - step; i++)
            {
                var key = string.Join(" ", filtered.Skip(i).Take(step));
                var value = filtered[i + step];

                if (!chain.TryGetValue(key, out var list))
                {
                    list = new List<string>();
                    chain[key] = list;
                }

                list.Add(value);
            }

            return chain;
        }

        private string GenerateMessage(Dictionary<string, List<string>> chain, int step, int wordCount)
        {
            if (chain.Count == 0)
                return string.Empty;

            var result = new StringBuilder();
            var temp = new List<string>
            {
                chain.ElementAt(Random.Shared.Next(chain.Count)).Key
            };

            for (int i = 0; i < wordCount; i++)
            {
                var key = string.Join(" ", temp.Skip(i).Take(step));

                if (!chain.ContainsKey(key))
                    key = chain.ElementAt(Random.Shared.Next(chain.Count)).Key;

                var values = chain[key];
                var value = values[Random.Shared.Next(values.Count)];

                while (result.Length == 0 && StartPunctuationRegex.IsMatch(value))
                {
                    key = chain.ElementAt(Random.Shared.Next(chain.Count)).Key;
                    values = chain[key];
                    value = values[Random.Shared.Next(values.Count)];
                }

                temp.Add(value);

                if (Random.Shared.NextDouble() < 0.05)
                {
                    var funny = FunnyInterjections[Random.Shared.Next(FunnyInterjections.Length)];
                    result.Append(' ').Append(funny);
                }

                if (_govorilka.VerbalAbuseBySheff)
                    result.Append(StartPunctuationRegex.IsMatch(value) ? value : $" {value} бля");
                else
                    result.Append(StartPunctuationRegex.IsMatch(value) ? value : $" {value} ");
            }

            return result.ToString().Trim();
        }
    }
}