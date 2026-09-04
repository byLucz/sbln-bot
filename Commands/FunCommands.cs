using Discord;
using Discord.Commands;
using sblngavnav5X.Core;
using sblngavnav5X.Common;
using sblngavnav5X.Data;
using sblngavnav5X.Services;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace sblngavnav5X.Commands
{
    public class FunCommands : ModuleBase<SocketCommandContext>
    {
        private readonly HttpClient _httpClient;
        private static readonly Regex JokeRegex =
            new(@"""content""\s*:\s*""(?<joke>.*?)""", RegexOptions.Singleline | RegexOptions.Compiled);

        public FunCommands(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        [Command("паста")]
        public async Task RandomMessageAsync()
        {
            if (Context.Guild == null)
            {
                await ReplyAsync("В этом канале нет доступных сообщений");
                return;
            }

            var channel = Context.Guild.GetTextChannel(858713660352233473);
            if (channel == null)
            {
                await ReplyAsync("В этом канале нет доступных сообщений");
                return;
            }

            var allMessages = new List<IMessage>();
            ulong? lastId = null;

            while (true)
            {
                IEnumerable<IMessage> pageEnumerable;

                if (lastId == null)
                {
                    pageEnumerable = await channel
                        .GetMessagesAsync(100)
                        .FlattenAsync();
                }
                else
                {
                    var lastMsg = await channel.GetMessageAsync(lastId.Value);
                    if (lastMsg == null)
                        break;

                    pageEnumerable = await channel
                        .GetMessagesAsync(lastMsg, Direction.Before, 100)
                        .FlattenAsync();
                }

                var page = pageEnumerable.ToList();
                if (page.Count == 0)
                    break;

                var textPage = page
                    .Where(m => !m.Author.IsBot && !string.IsNullOrWhiteSpace(m.Content))
                    .ToList();

                allMessages.AddRange(textPage);

                if (page.Count < 100)
                    break;

                lastId = page.Last().Id;
            }

            if (allMessages.Count == 0)
            {
                await ReplyAsync("В этом канале нет доступных сообщений");
                return;
            }

            var msg = allMessages[Random.Shared.Next(allMessages.Count)];

            var embed = new EmbedBuilder()
                .WithAuthor(msg.Author)
                .WithDescription(msg.Content)
                .WithFooter("sbln паста-карбонара🍝")
                .WithTimestamp(msg.Timestamp)
                .WithColor(Color.Red)
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("шутка")]
        [Alias("анек")]
        public async Task JokeTask([Optional] string category)
        {
            int categoryId;

            if (category == null)
            {
                await ReplyAsync(embed: BuildJokeMenuEmbed(
                    "Выберите категорию\n 1 - Анекдоты\n 2 - Рассказы\n 3 - Стишки"));
                return;
            }

            switch (category)
            {
                case "1":
                    categoryId = 11;
                    break;
                case "2":
                    categoryId = 12;
                    break;
                case "3":
                    categoryId = 13;
                    break;
                default:
                    await ReplyAsync(embed: BuildJokeMenuEmbed(
                        "Выберите категорию (ТОЛЬКО ИЗ СПИСКА!)\n 1 - Анекдоты\n 2 - Рассказы\n 3 - Стишки"));
                    return;
            }

            string jokeText = "";
            const int maxRetries = 5;
            int attempt = 0;
            bool success = false;

            while (attempt < maxRetries && !success)
            {
                try
                {
                    using var response = await _httpClient.GetAsync($"http://rzhunemogu.ru/RandJSON.aspx?CType={categoryId}");
                    response.EnsureSuccessStatusCode();

                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    var responseBody = Encoding.GetEncoding(1251).GetString(bytes);

                    int startIndex = responseBody.IndexOf("{", StringComparison.Ordinal);
                    int endIndex = responseBody.LastIndexOf("}", StringComparison.Ordinal);

                    if (startIndex == -1 || endIndex == -1 || endIndex <= startIndex)
                        throw new Exception("json ржаки не будет...");

                    string jsonString = responseBody.Substring(startIndex, endIndex - startIndex + 1);

                    try
                    {
                        var json = JsonNode.Parse(jsonString);
                        jokeText = json?["content"]?.ToString() ?? "";
                    }
                    catch
                    {
                        var match = JokeRegex.Match(jsonString);
                        if (match.Success)
                        {
                            jokeText = match.Groups["joke"].Value;
                        }
                        else
                        {
                            throw new Exception("ржаки не будет...");
                        }
                    }

                    success = true;
                }
                catch (Exception ex)
                {
                    attempt++;
                    if (attempt >= maxRetries)
                        jokeText = $"🔴AШЫБКА🔴 - {ex.Message}";
                }
            }

            var jokeMessage = await ReplyAsync(embed: EmbedHandler.Authored(
                "sbln шутки 😂",
                jokeText,
                Color.Orange,
                "powered by rzhunemogu.ru"));
            var emote = Emote.Parse("<:slyr4head:816639053008338944>");
            await jokeMessage.AddReactionAsync(emote);
        }

        [Command("цитаты")]
        [Alias("цит")]
        public async Task Quores()
        {
            string author;
            string toReturn;

            try
            {
                using var response = await _httpClient.GetAsync("https://api.forismatic.com/api/1.0/?method=getQuote&format=json&lang=ru");
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var text = JsonNode.Parse(responseBody);

                toReturn = text?["quoteText"]?.ToString() ?? "";
                author = string.IsNullOrEmpty(text?["quoteAuthor"]?.ToString())
                    ? "*без автора*"
                    : text!["quoteAuthor"]!.ToString();
            }
            catch (Exception e)
            {
                toReturn = $"бля - {e.Message}";
                author = $"бля - {e.Message}";
            }

            await ReplyAsync(embed: EmbedHandler.Authored(
                "sbln цитаты⛲",
                $"***{toReturn}***",
                Color.LighterGrey,
                author,
                "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcTdfR4o2lIVZ0sLL1y_SRYPYIXQ5hXxI-w89A&s"));
        }

        [Command("волк")]
        public async Task WolfMeme()
        {
            var url = DataBase.GetRandomMeme("volk");
            if (url == null)
                await ReplyAsync("Мемов пока нет =(");
            else
                await ReplyAsync(url);
        }

        [Command("8 яиц")]
        [Alias("?")]
        public async Task EightEggs([Remainder] string args = null)
        {
            var url = DataBase.GetRandomMeme("yaica");
            if (url == null)
                await ReplyAsync("Мемов пока нет =(");
            else
                await ReplyAsync(url);
        }

        [Command("гэй")]
        public async Task GayCommand([Optional] IGuildUser user)
        {
            user ??= (IGuildUser)Context.User;

            int percentage = Utils.RandomNumber(0, 101);

            await Context.Channel.SendMessageAsync(
                $"**{user.Mention}** уровень гейства - {(percentage > 100 ? "***больше 9000!***" : $"**{percentage}%**")}. " +
                $"\n{(user.Id == Context.User.Id ? "Ты" : user.Id == Context.Client.CurrentUser.Id ? "Я" : "Он")} **{(percentage < 33 ? "гетеро" : percentage < 66 ? "биби" : "гэй")}**! ");
        }

        [Command("трап")]
        public async Task TrapTrapTrap()
        {
            await ReplyAsync("Если🌈 услышим👂звук🔈сирены 🚨и мигалки🚔ран🏃Небо🌇осень🍂кап💧кап💦Когда😞близко👨❤👨лав💖лав🤍Мои 🤏 деньги💸ап⏫ап🆙ап🔝ап🔼ап⏫ап🔺Предлагали☑десять🔟забрал🙌десять по десять⃣⃣Кем🧑🦽я👀стал❓Всему 🥶виной😵💈трап🧝трап🦸трап");
        }

        [Command("иди нахуй")]
        public async Task FuckOff()
        {
            await ReplyAsync("сам иди.");
        }

        [Command("андерстендебел")]
        [Alias("анд")]
        [Cooldown(5)]
        public async Task Understandable()
        {
            await ReplyAsync("https://www.dailydot.com/wp-content/uploads/c1b/b4/ca394a1143d6d0e5.png");
        }

        [Command("WW")]
        [Alias("ww")]
        public async Task WW()
        {
            await ReplyAsync("https://cdn.7tv.app/emote/01GM9KFF4G000BYX6NYFV0K5MZ/4x.avif");
        }

        [Command("погладить")]
        public async Task Pat([Remainder] string input)
        {
            await SendMemeActionAsync("pat", $"{Context.User.Mention} погладил {input}💕");
        }

        [Command("чмокнуть")]
        public async Task Kiss([Remainder] string input)
        {
            await SendMemeActionAsync("kiss", $"{Context.User.Mention} чмокнул {input}💕");
        }

        [Command("обнять")]
        public async Task Hug([Remainder] string input)
        {
            await SendMemeActionAsync("hug", $"{Context.User.Mention} обнял {input}💕");
        }

        [Command("ф")]
        public async Task F([Remainder] string input)
        {
            await SendMemeActionAsync("fff", $"{Context.User.Mention} дает респект {input} <:sadge:853604643456024576>");
        }

        [Command("кусь")]
        public async Task Kus([Remainder] string input)
        {
            await SendMemeActionAsync("kus", $"{Context.User.Mention} куснул {input}💕");
        }

        [Command("бухнуть")]
        public async Task Buhat([Remainder] string input)
        {
            await SendMemeActionAsync("buhat", $"{Context.User.Mention} хочет бухнуть с {input} \U0001f974");
        }

        [Command("заткнуть")]
        [Alias("завали ебало")]
        public async Task Zavali([Remainder] string input)
        {
            await SendMemeActionAsync("ebalo", $"{Context.User.Mention} затыкает {input} 🤐");
        }

        private static Embed BuildJokeMenuEmbed(string description)
            => EmbedHandler.Authored("sbln шутки 😂", description, Color.Orange, "powered by rzhunemogu.ru");

        private async Task SendMemeActionAsync(string category, string title)
        {
            var url = DataBase.GetRandomMeme(category);

            if (string.IsNullOrWhiteSpace(url))
            {
                await ReplyAsync("Мемов пока нет =(");
                return;
            }

            var em = EmbedHandler.CreateFImgEmbed(title, url);
            await Context.Channel.SendMessageAsync(embed: await em);
        }
    }
}