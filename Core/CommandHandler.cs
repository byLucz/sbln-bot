using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using sblngavnav5X.GVR;
using System.Timers;
using System.Text.RegularExpressions;
using System.Text;
using sblngavnav5X.Data;
using Timer = System.Timers.Timer;
using Victoria;
using sblngavnav5X.Services;


namespace sblngavnav5X.Core
{
    public class CommandHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly CommandService _commands;
        private readonly IServiceProvider _services;
        private readonly GovorConfig govorilka;
        private readonly LavaNode<LavaPlayer<LavaTrack>, LavaTrack> _lavaNode;
        public static Timer t = new Timer(Utils.govorUpdTime);

        public CommandHandler(IServiceProvider services, GovorConfig govorilka)
        {
            _commands = services.GetRequiredService<CommandService>();
            _client = services.GetRequiredService<DiscordSocketClient>();
            _services = services;
            _lavaNode = services.GetRequiredService<LavaNode<LavaPlayer<LavaTrack>, LavaTrack>>();
            this.govorilka = govorilka;
            HookEvents();
        }

        public async Task InitializeAsync()
        {
            await _commands.AddModulesAsync(
                assembly: Assembly.GetEntryAssembly(),
                services: _services);
            
        }

        public void HookEvents()
        {
            _commands.CommandExecuted += CommandExecutedAsync;
            _commands.Log += LogAsync;
            _client.MessageReceived += HandleMessageAsync;
            _client.Ready += StartTimer;
        }

        private async Task HandleMessageAsync(SocketMessage socketMessage)
        {
            var message = socketMessage as SocketUserMessage;
            var context = new SocketCommandContext(_client, message);

            if (message.Author.IsBot)
                return;

            int caracterPos = 0;
            if (message.HasStringPrefix(Utils.pref1, ref caracterPos) || message.HasStringPrefix(Utils.pref2, ref caracterPos))
            {
                var result = await _commands.ExecuteAsync(context, caracterPos, _services);
                if (!result.IsSuccess)
                    Console.WriteLine(result.ErrorReason);
                if (result.Error.Equals(CommandError.UnmetPrecondition))
                    await message.Channel.SendMessageAsync(result.ErrorReason);
            }
            else
            {
                if (govorilka.Rand == true)
                {
                    govorilka.Count = Utils.RandomNumber(3, 20);
                    await MarkovOutput(context);
                }
                else
                {
                    await MarkovOutput(context);
                }
            }
        }

        public async Task CommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
        {
            if (!command.IsSpecified)
                return;

            if (result.IsSuccess)
                return;

            await context.Channel.SendMessageAsync($"🔴ОШИБКА🔴 - {result}");
        }

        private Task LogAsync(LogMessage log)
        {
            Console.WriteLine(log.ToString());

            return Task.CompletedTask;
        }
        private async Task MarkovOutput(SocketCommandContext context)
        {
            if (Utils.RandomNumber(1, 100 + 1) <= govorilka.Chance)
            {
                using (context.Channel.EnterTypingState())
                {
                    await MarkovTalk(context, (int)govorilka.Step, (int)govorilka.Count);
                }
            }
        }
        private async Task MarkovTalk(SocketCommandContext ctx, int step, int wordCount)
        {
            var message = File.ReadLines("messages.csv");
            var messages = message.Select(x => x.ToString()).ToList();
            if (!message.Any()) return;

            var filtered = FilterMessages(ctx, messages);
            if (!filtered.Any()) return;

            var chain = MakeChain(filtered, step);
            if (!chain.Any()) return;

            var result = GenerateMessage(chain, step, wordCount);
            if (string.IsNullOrEmpty(result)) return;

            await ctx.Channel.SendMessageAsync(result);
        }
        private List<string> FilterMessages(SocketCommandContext ctx, List<string> messages)
        {

            var control = @"[x!?.,:;()[]//]+";
            var filter = @"";
            var filtered = new List<string>();

            foreach (var msg in messages)
            {

                var rep = Regex.Replace(msg, filter, "");
                if (string.IsNullOrEmpty(rep)) continue;

                var sb = new StringBuilder();
                foreach (var s in rep.Select(x => x.ToString()))
                {
                    sb.Append(Regex.IsMatch(s, control) ? $" {s} " : s);
                }
                    if (!sb.ToString().Contains(@"https://"))
                    {
                        if (!sb.ToString().Contains(@" x "))
                        {
                            var split = Regex.Split(sb.ToString().ToLower(), @"\s+");
                            if (!split.Any()) continue;

                            var noEmpty = split.Where(x => !string.IsNullOrEmpty(x));
                            if (!noEmpty.Any()) continue;

                            filtered.AddRange(noEmpty);
                        
                        }
                    }
            }
            return filtered;
        }
        private Dictionary<string, List<string>> MakeChain(List<string> filtered, int step)
        {
            var chain = new Dictionary<string, List<string>>();
            for (var i = 0; i < filtered.Count - step; i++)
            {
                var k = string.Join(" ", filtered.Skip(i).Take(step));
                var v = filtered[i + step];
                if (!chain.ContainsKey(k))
                {
                    chain.Add(k, new List<string> { v });
                }
                else
                {
                    chain[k].Add(v);
                }
            }
            return chain;
        }

        private string GenerateMessage(Dictionary<string, List<string>> chain, int step, int wordCount)
        {
            var control = @"[х!?.,;()[\]//]+";
            var rand = new Random(DateTime.UtcNow.Millisecond);
            var result = new StringBuilder();
            var funnyInterjections = new List<string>
            {
                "лол", "лейм", "YZL", ")", "(((","соси", "шефчик", "йоу", "чел"
            };

            var temp = new List<string>
            {
                chain.ElementAt(rand.Next(0, chain.Count)).Key,
            };

            for (int i = 0; i < wordCount; i++)
            {
                var key = string.Join(" ", temp.Skip(i).Take(step));
                if (!chain.ContainsKey(key))
                {
                    key = chain.ElementAt(rand.Next(0, chain.Count)).Key;
                }
                var value = chain[key].ElementAt(rand.Next(0, chain[key].Count));
                while (result.Length == 0 && Regex.IsMatch(value, control))
                {
                    key = chain.ElementAt(rand.Next(0, chain.Count)).Key;
                    value = chain[key].ElementAt(rand.Next(0, chain[key].Count));
                }
                temp.Add(value);

                if (rand.NextDouble() < 0.05)
                {
                    var funny = funnyInterjections[rand.Next(funnyInterjections.Count)];
                    result.Append($" {funny}");
                }

                if (govorilka.VerbalAbuseBySheff == true)
                {
                    result.Append(Regex.IsMatch(value, control) ? value : $" {value} бля");
                }
                else
                {
                    result.Append(Regex.IsMatch(value, control) ? value : $" {value} ");
                }
            }
            return result.ToString();
        }



        private async Task StartTimer()
        {
            t.AutoReset = true;
            t.Elapsed += new ElapsedEventHandler(OnTimedEvent);
            t.Start();
            if (!_lavaNode.IsConnected)
            {
                await _services.UseLavaNodeAsync();
            }
            if (_lavaNode.IsConnected)
            {
                await LoggingService.LogInformationAsync("VI-KA", $"есть контакт");
            }
            else
            {
                await LoggingService.LogCriticalAsync("VI-KA", $"нет контакт");
            }

        }

        private async void OnTimedEvent(Object sender, ElapsedEventArgs e)
        {
            ulong id = 500683173298962432;
            var channel = _client.GetChannel(id) as IMessageChannel;
            if (channel == null)
            {
                await LoggingService.LogInformationAsync("GVR", $"Не удалось получить канал с ID {id}. Канал равен null");
                return;
            }

            var messages = channel.GetMessagesAsync((int)govorilka.Collection).Flatten();
            if (messages == null)
            {
                await LoggingService.LogInformationAsync("GVR", $"Не удалось получить сообщения. Коллекция сообщений пуста");
                return;
            }

            try
            {
                using (StreamWriter sw = new StreamWriter("messages.csv", append: true))
                {
                    await foreach (IMessage message in messages)
                    {
                        if (message == null)
                        {
                            continue;
                        }

                        if (string.IsNullOrEmpty(message.Content))
                        {
                            continue;
                        }

                        if (message.Attachments.Any() || message.Embeds.Any())
                        {
                            continue;
                        }

                        try
                        {
                            sw.WriteLine(message.Content);
                        }
                        catch (Exception ex)
                        {
                            await LoggingService.LogCriticalAsync("GVR", $"Ошибка при записи сообщения. ID={message.Id}, Содержимое={message.Content}, Ошибка: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync("GVR", $"Произошла ошибка при записи сообщений: {ex.Message}");
            }

            try
            {
                await RemoveDuplicates();
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync("GVR", $"Произошла ошибка при удалении дубликатов: {ex.Message}");
            }
        }

        private async Task RemoveDuplicates()
        {
            try
            {
                string[] lines = File.ReadAllLines("messages.csv");
                if (lines.Length == 0)
                {
                    await LoggingService.LogInformationAsync("GVR", $"Файл messages.csv пуст, нечего очищать от дубликатов");
                    return;
                }

                await File.WriteAllLinesAsync("messages.csv", lines.Distinct().ToArray());
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync("GVR", $"Произошла ошибка при чтении/записи файла: {ex.Message}");
            }
        }
    }
}
