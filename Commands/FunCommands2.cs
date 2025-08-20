using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using Discord.Net.Udp;
using sblngavnav5X.Data;
using sblngavnav5X.Services;

namespace sblngavnav5X.Commands;

public class FunCommands2 : ModuleBase<SocketCommandContext>
{
    public double us, eu, tr, kz;

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

    [Command("ролл")]
    public async Task Roll(int min , int max)
    {
        await Context.Channel.SendMessageAsync("Твое число - " + $@"{Utils.RandomNumber(min,max)}", true);
    }

    [Command("кит")]
    [Alias("кот")]

    public async Task UploadCat()
    {
        using WebClient wc = new WebClient();
        CatData jsonData;
        try
        {
            jsonData = JsonConvert
                .DeserializeObject<CatData[]>(
                    wc.DownloadString("https://api.thecatapi.com/v1/images/search?format=json")).First();
        }
        catch (WebException e)
        {
            await ReplyAsync($"🔴ОШИБКА🔴 - {e.Message}");
            return;
        }

        wc.Dispose();

        EmbedBuilder builder = new EmbedBuilder
        {
            Color = Color.Teal,
            ImageUrl = jsonData.Url.OriginalString
        };
        builder.Title = "sbln кисики😼";
        builder.WithCurrentTimestamp();
        builder.WithFooter("powered by thecatapi.com");
        try
        {
            await ReplyAsync(embed: builder.Build());
        }
        catch (HttpRequestException e)
        {
            await ReplyAsync($"🔴ОШИБКА🔴 - {e.Message}");
        }
    }

    [Command("гэй")]
    public async Task GayCommand([Optional] IGuildUser User)
    {
        if (User == null)
            User = (IGuildUser)Context.User;

        int min = 0;
        int max = 101;
        int Percentage = Utils.RandomNumber(min, max);

        await Context.Channel.SendMessageAsync($"**{User.Mention}** уровень гейства - {(Percentage > 100 ? "***больше 9000!***" : $"**{Percentage}%**")}. "
            + $"\n{(User.Id == Context.User.Id ? "Ты" : User.Id == Context.Client.CurrentUser.Id ? "Я" : "Он")} **{(Percentage < 33 ? "гетеро" : Percentage < 66 ? "биби" : "гэй")}**! ");
    }

    [Command("кал")]
    public async Task MathAsync([Remainder] string math)
    {
        var dt = new DataTable();
        var result = dt.Compute(math, null);
        var m = new EmbedBuilder()
        {
            Description = $"{math} = {result}",
            Author = new EmbedAuthorBuilder()
            {
                Name = "sbln калькулятор📚📐",
            },
            Color = Color.DarkerGrey
        };
        await ReplyAsync(embed: m.Build());
    }

    [Command("шутка")]
    [Alias("анек")]
    public async Task JokeTask([Optional] string category)
    {
        int categoryId = 0;
        try
        {
            if (category == null)
            {
                var embed = new EmbedBuilder()
                {
                    Footer = new EmbedFooterBuilder()
                    {
                        Text = "powered by rzhunemogu.ru"
                    },
                    Author = new EmbedAuthorBuilder()
                    {
                        Name = "sbln шутки 😂",
                    },
                    Color = Color.Orange
                };
                embed.WithDescription("Выберите категорию\n 1 - Анекдоты\n 2 - Рассказы\n 3 - Стишки");
                await ReplyAsync(embed: embed.Build());
                return;
            }
            else
            {
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
                        var embed = new EmbedBuilder()
                        {
                            Footer = new EmbedFooterBuilder()
                            {
                                Text = "powered by rzhunemogu.ru"
                            },
                            Author = new EmbedAuthorBuilder()
                            {
                                Name = "sbln шутки 😂",
                            },
                            Color = Color.Orange
                        };
                        embed.WithDescription("Выберите категорию (ТОЛЬКО ИЗ СПИСКА!)\n 1 - Анекдоты\n 2 - Рассказы\n 3 - Стишки");
                        await ReplyAsync(embed: embed.Build());
                        return;
                }
            }
        }
        catch (Exception ex)
        {
            await ReplyAsync("🔴AШЫБКА🔴 - " + ex.Message);
            return;
        }

        HttpClient client = new HttpClient();
        string jokeText = "";
        int maxRetries = 5;
        int attempt = 0;
        bool success = false;

        while (attempt < maxRetries && !success)
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync($"http://rzhunemogu.ru/RandJSON.aspx?CType=" + categoryId);
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();

                int startIndex = responseBody.IndexOf("{");
                int endIndex = responseBody.LastIndexOf("}");
                if (startIndex == -1 || endIndex == -1 || endIndex <= startIndex)
                {
                    throw new Exception("json ржаки не будет...");
                }
                string jsonString = responseBody.Substring(startIndex, endIndex - startIndex + 1);

                try
                {
                    var json = JObject.Parse(jsonString);
                    jokeText = json["content"].ToString();
                }
                catch
                {
                    var match = System.Text.RegularExpressions.Regex.Match(jsonString, @"""content""\s*:\s*""(?<joke>.*?)""", System.Text.RegularExpressions.RegexOptions.Singleline);
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
                {
                    jokeText = $"🔴AШЫБКА🔴 - {ex.Message}";
                }
            }
        }

        var embedMessage = new EmbedBuilder()
        {
            Footer = new EmbedFooterBuilder()
            {
                Text = "powered by rzhunemogu.ru"
            },
            Author = new EmbedAuthorBuilder()
            {
                Name = "sbln шутки 😂",
            },
            Color = Color.Orange
        };
        embedMessage.WithDescription($"{jokeText}");
        var jokeMessage = await ReplyAsync(embed: embedMessage.Build());
        var emote = Emote.Parse("<:slyr4head:816639053008338944>");
        await jokeMessage.AddReactionAsync(emote);
    }



    [Command("цитаты")]
    [Alias("цит")]
    public async Task Quores()
    {

        HttpClient client = new HttpClient();
        string Author;
        string toReturn;
        try
        {
            HttpResponseMessage response = await client.GetAsync("https://api.forismatic.com/api/1.0/?method=getQuote&format=json&lang=ru");
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            var text = JObject.Parse(responseBody);
            toReturn = text["quoteText"].ToString();
            if(string.IsNullOrEmpty((string)text["quoteAuthor"])) 
            {
                Author = "*без автора*";
            }
            else
            Author = text["quoteAuthor"].ToString();
        }
        catch (HttpRequestException e)
        {
            toReturn = $"бля - {e.Message}";
            Author = $"бля - {e.Message}";
        }
        var embedMessage = new EmbedBuilder()
        {
            Footer = new EmbedFooterBuilder()
            {
                Text = $"{Author}",
                IconUrl = "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcTdfR4o2lIVZ0sLL1y_SRYPYIXQ5hXxI-w89A&s"
            },
            Author = new EmbedAuthorBuilder()
            {
                Name = "sbln цитаты⛲"
            },
            Color = Color.LighterGrey
        };
        embedMessage.WithDescription($"***{toReturn}***");
        await ReplyAsync(embed: embedMessage.Build());
    }

    [Command("курс")]
    [Alias("кс")]
    public async Task Exchange()
    {

        HttpClient client = new HttpClient();

        try
        {
            var r = await client.GetAsync("https://www.cbr-xml-daily.ru/latest.js");
            if (r.StatusCode == System.Net.HttpStatusCode.OK)
            {

                    Root b = JsonConvert.DeserializeObject<Root>(await r.Content.ReadAsStringAsync());
                    us = 1/b.rates.USD;
                    eu = 1/b.rates.EUR;
                    tr = 1/b.rates.TRY;
                    kz = b.rates.KZT;
            }
           
        }
        catch (HttpRequestException e)
        {
            await ReplyAsync($"сервис недоступен - {e.Message}");
        }
        var z = new EmbedBuilder()
        {
            Author = new EmbedAuthorBuilder()
            {
                Name = "sbln курс валют💱💵",
            },
            Color = Color.DarkTeal,
            ThumbnailUrl = "https://upload.wikimedia.org/wikipedia/commons/1/18/Russia-Coin-1-2009-a.png",
            Footer = new EmbedFooterBuilder()
            {
                Text = "powered by CENTROBANK OF RUSSIA🏦🇷🇺"
            }

        };

        z.AddField("*USD*", $"{Utils.Round(us, 2)}₽", true);
        z.AddField("*EUR*", $"{Utils.Round(eu, 2)}₽", true);
        z.AddField("*TRY*", $"{Utils.Round(tr, 2)}₽", true);
        z.AddField("*KZT*", $"{Utils.Round(kz, 2)}₽", true);

        await ReplyAsync(embed: z.Build());
    }

    [Command("напомни", RunMode = RunMode.Async)]
    [Alias("н")]
    public async Task Remind(int seconds, [Remainder] string remindMsg)
    {
        var e = new EmbedBuilder()
        {
            Title = "sbln напоминалка⏰",
            Description = $"😎 ок, я отправлю тебе вот это --- **{remindMsg}**\n" +
            $"⌚ через **{seconds}** сек.",
            Color = Color.DarkGreen,
        };
        await ReplyAsync(embed: e.Build());
        await ReminderService.RemindAsyncSeconds(Context.User, seconds, remindMsg);
    }

    [Command("выбери")]
    public async Task ChooseAsync([Remainder] string options)
    {
        var items = options.Split('|', StringSplitOptions.RemoveEmptyEntries)
                           .Select(x => x.Trim())
                           .ToList();
        if (!items.Any())
        {
            await ReplyAsync("нет вариантов для выбора чел");
            return;
        }

        var rnd = new Random();
        var chosen = items[rnd.Next(items.Count)];

        var embed = new EmbedBuilder()
            .WithColor(Color.DarkBlue)
            .WithDescription("<a:NERDALERT:1275220081390911579> Дай подумать...")
            .WithFooter("sbln выбератор🤔")
            .Build();

        var msg = await ReplyAsync(embed: embed);

        await Task.Delay(2000);
        var embed1 = new EmbedBuilder()
            .WithColor(Color.Orange)
            .WithDescription("<:aga:1254820158669717565>  Хм, что же выбрать еп...")
            .WithFooter("sbln выбератор🤔")
            .Build();
        await msg.ModifyAsync(m => m.Embed = embed1);

        await Task.Delay(2000);
        var embed2 = new EmbedBuilder()
            .WithColor(Color.Green)
            .WithDescription("<:agerge:1275215945769685044>  Надо выбрать что-то вайбовое...")
            .WithFooter("sbln выбератор🤔")
            .Build();
        await msg.ModifyAsync(m => m.Embed = embed2);

        await Task.Delay(2000);

        var finalEmbed = new EmbedBuilder()
            .WithColor(Color.Gold)
            .WithDescription($"**Я выбираю:** `{chosen}`")
            .WithFooter("sbln выбератор🤔")
            .Build();
        await msg.ModifyAsync(m => m.Embed = finalEmbed);
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
        await ReplyAsync(url);
    }
    
    [Command("ембед")]
    public async Task CmdEmbedMessage([Optional]int color,[Remainder] string msg)
    {

        string[] input = msg.Split('|');
        msg = String.Join("|", input);
        var e = new EmbedBuilder()
        {
            Title = input[0],
            Description = input[1],
        };

        var clr = color switch
            {
                0 => e.Color = Color.Default,
                1 => e.Color = Color.Red,
                2 => e.Color = Color.Green,
                3 => e.Color = Color.Blue,
                4 => e.Color = Color.Gold
            };
        await ReplyAsync(embed: e.Build());
        Thread.Sleep(5000);

    }

    [Command("паста")]
    public async Task RandomMessageAsync()
    {
        var channel = Context.Guild.GetTextChannel(858713660352233473);
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
            var textPage = page
                .Where(m => !m.Author.IsBot && !string.IsNullOrWhiteSpace(m.Content))
                .ToList();

            if (textPage.Count == 0)
                break;

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

        var rnd = new Random();
        var msg = allMessages[rnd.Next(allMessages.Count)];

        var embed = new EmbedBuilder()
            .WithAuthor(msg.Author)
            .WithDescription(msg.Content)
            .WithFooter("sbln паста-карбонара🍝")
            .WithTimestamp(msg.Timestamp)
            .WithColor(Color.Red)
            .Build();

        await ReplyAsync(embed: embed);
    }

    public class CatData
    {
        [JsonProperty("url")] public Uri Url { get; set; }
    }
}