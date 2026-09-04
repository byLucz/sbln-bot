using Discord;
using Discord.Commands;
using sblngavnav5X.Core;
using sblngavnav5X.Common;
using sblngavnav5X.Data;
using sblngavnav5X.Services;
using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using static sblngavnav5X.Data.DataRoots;

namespace sblngavnav5X.Commands;

public class MiscCommands : ModuleBase<SocketCommandContext>
{
    private readonly HttpClient _httpClient;

    private const string CatApiUrl = "https://api.thecatapi.com/v1/images/search?format=json";
    private const string BitfinexApiUrl = "https://api-pub.bitfinex.com/v2/tickers?symbols=tBTCUSD,tETHUSD,tSOLUSD,tTONUSD";
    private const string CurrencyApiUrl = "https://www.cbr-xml-daily.ru/latest.js";

    public MiscCommands(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient();
    }

    [Command("ролл")]
    public async Task Roll(int min, int max)
    {
        if (min > max)
            (min, max) = (max, min);

        await Context.Channel.SendMessageAsync("Твое число - " + $@"{Utils.RandomNumber(min, max)}");
    }

    [Command("кит")]
    [Alias("кот")]
    public async Task UploadCat()
    {
        CatData? jsonData;

        try
        {
            using var response = await _httpClient.GetAsync(CatApiUrl);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var cats = JsonSerializer.Deserialize(content, AppJsonContext.Default.CatDataArray);

            jsonData = cats?.FirstOrDefault();

            if (jsonData?.Url == null)
            {
                await ReplyAsync("🔴ОШИБКА🔴 - Не удалось получить кота");
                return;
            }
        }
        catch (Exception e) when (e is HttpRequestException || e is JsonException || e is TaskCanceledException)
        {
            await ReplyAsync($"🔴ОШИБКА🔴 - {e.Message}");
            return;
        }

        var builder = new EmbedBuilder
        {
            Color = Color.Teal,
            ImageUrl = jsonData.Url.OriginalString,
            Title = "sbln кисики😼"
        };

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

    private static readonly Regex _mathSafeRegex = new(@"^[\d\s\+\-\*\/\(\)\.]+$", RegexOptions.Compiled);

    [Command("кал")]
    public async Task MathAsync([Remainder] string math)
    {
        if (!_mathSafeRegex.IsMatch(math))
        {
            await ReplyAsync("🔴ОШИБКА🔴 - недопустимые символы в выражении");
            return;
        }

        try
        {
            using var dt = new DataTable();
            var result = dt.Compute(math, null);

            await ReplyAsync(embed: EmbedHandler.Authored(
                "sbln калькулятор📚📐",
                $"{math} = {result}",
                Color.DarkerGrey,
                "sbln"));
        }
        catch (Exception e)
        {
            await ReplyAsync($"🔴ОШИБКА🔴 - {e.Message}");
        }
    }

    [Command("биток")]
    [Alias("монетки", "мон")]
    public async Task GetCoins([Remainder] string unused = null)
    {
        await Context.Channel.TriggerTypingAsync();

        try
        {
            using var bfResponse = await _httpClient.GetAsync(BitfinexApiUrl);

            if (!bfResponse.IsSuccessStatusCode)
            {
                await ReplyAsync("битфинекс упал, статистики не будет((");
                return;
            }

            var content = await bfResponse.Content.ReadAsStringAsync();
            var results = JsonSerializer.Deserialize(content, AppJsonContext.Default.ListListJsonElement);

            if (results == null || results.Count == 0)
            {
                await ReplyAsync("битфинекс упал, статистики не будет((");
                return;
            }

            var coins = ConvertToBitfinexCoins(results);

            var btc = coins.Find(c => c.Symbol == "tBTCUSD");
            var eth = coins.Find(c => c.Symbol == "tETHUSD");
            var sol = coins.Find(c => c.Symbol == "tSOLUSD");
            var ton = coins.Find(c => c.Symbol == "tTONUSD");

            if (btc == null || eth == null || sol == null || ton == null)
            {
                await ReplyAsync("битфинекс упал, статистики не будет((");
                return;
            }

            const string iconUrl = "https://cdn0.iconfinder.com/data/icons/bitcoin-94/64/chip-bitcoin-512.png";

            var e = EmbedHandler.FieldsEmbed("sbln крипта💰📈", Color.LightOrange, "powered by bitfinex💸", iconUrl);

            e.AddField("*BTC*", $"{btc.LastPrice:0.00#}$", true);
            e.AddField("прирост", $"{btc.DailyChange:0.00#}$", true);
            e.AddField("в процентах", $"({btc.DailyChangePercentage:0.00#}%)", true);

            e.AddField("*ETH*", $"{eth.LastPrice:0.00#}$", true);
            e.AddField("прирост", $"{eth.DailyChange:0.00#}$", true);
            e.AddField("в процентах", $"({eth.DailyChangePercentage:0.00#}%)", true);

            e.AddField("*SOL*", $"{sol.LastPrice:0.00#}$", true);
            e.AddField("прирост", $"{sol.DailyChange:0.00#}$", true);
            e.AddField("в процентах", $"({sol.DailyChangePercentage:0.00#}%)", true);

            e.AddField("*TON*", $"{ton.LastPrice:0.00#}$", true);
            e.AddField("прирост", $"{ton.DailyChange:0.00#}$", true);
            e.AddField("в процентах", $"({ton.DailyChangePercentage:0.00#}%)", true);

            await ReplyAsync(embed: e.Build());
        }
        catch
        {
            await ReplyAsync("битфинекс упал, статистики не будет((");
        }
    }

    [Command("курс")]
    [Alias("кс")]
    public async Task Exchange()
    {
        Converter? b;

        try
        {
            using var response = await _httpClient.GetAsync(CurrencyApiUrl);

            if (!response.IsSuccessStatusCode)
            {
                await ReplyAsync($"сервис недоступен - ошибка ответа сервиса");
                return;
            }

            var content = await response.Content.ReadAsStringAsync();
            b = JsonSerializer.Deserialize(content, AppJsonContext.Default.Converter);

            if (b?.rates == null)
            {
                await ReplyAsync($"сервис недоступен - ошибка чтения ответа");
                return;
            }

            b.rates.USD = 1 / b.rates.USD;
            b.rates.EUR = 1 / b.rates.EUR;
            b.rates.TRY = 1 / b.rates.TRY;
            b.rates.PLN = 1 / b.rates.PLN;
            b.rates.CNY = 1 / b.rates.CNY;
            b.rates.BYN = 1 / b.rates.BYN;
            b.rates.JPY = 1 / b.rates.JPY;
            b.rates.HKD = 1 / b.rates.HKD;
        }
        catch (Exception e) when (e is HttpRequestException || e is JsonException || e is TaskCanceledException)
        {
            await ReplyAsync($"сервис недоступен - {e.Message}");
            return;
        }

        var z = EmbedHandler.FieldsEmbed(
            "sbln курс валют💱💵",
            Color.DarkTeal,
            "powered by CENTROBANK OF RUSSIA🏦🇷🇺",
            "https://upload.wikimedia.org/wikipedia/commons/1/18/Russia-Coin-1-2009-a.png");

        z.AddField("USD", $"{Utils.Round(b.rates.USD, 2)}₽", true);
        z.AddField("EUR", $"{Utils.Round(b.rates.EUR, 2)}₽", true);
        z.AddField("TRY", $"{Utils.Round(b.rates.TRY, 2)}₽", true);
        z.AddField("PLN", $"{Utils.Round(b.rates.PLN, 2)}₽", true);
        z.AddField("CNY", $"{Utils.Round(b.rates.CNY, 2)}₽", true);
        z.AddField("BYN", $"{Utils.Round(b.rates.BYN, 2)}₽", true);
        z.AddField("JPY", $"{Utils.Round(b.rates.JPY, 2)}₽", true);
        z.AddField("HKD", $"{Utils.Round(b.rates.HKD, 2)}₽", true);
        z.AddField("KZT", $"{Utils.Round(b.rates.KZT, 2)}₽", true);

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
                           .Where(x => !string.IsNullOrWhiteSpace(x))
                           .ToList();

        if (!items.Any())
        {
            await ReplyAsync("нет вариантов для выбора чел");
            return;
        }

        var chosen = items[Random.Shared.Next(items.Count)];

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

    [Command("ембед")]
    public async Task CmdEmbedMessage(int color = 0, [Remainder] string msg = "")
    {
        string[] input = msg.Split('|');

        var title = input.Length > 0 ? input[0] : string.Empty;
        var description = input.Length > 1 ? input[1] : string.Empty;

        var e = new EmbedBuilder()
        {
            Title = title,
            Description = description,
            Color = color switch
            {
                1 => Color.Red,
                2 => Color.Green,
                3 => Color.Blue,
                4 => Color.Gold,
                _ => Color.Default
            }
        };

        await ReplyAsync(embed: e.Build());
    }

    private static List<BitfinexCoin> ConvertToBitfinexCoins(List<List<JsonElement>> obj)
    {
        var coins = new List<BitfinexCoin>();

        foreach (var coin in obj)
        {
            if (coin == null || coin.Count < 11)
                continue;

            try
            {
                coins.Add(new BitfinexCoin
                {
                    Symbol = Str(coin[0]),
                    Bid = Dec(coin[1]),
                    BidSize = Dec(coin[2]),
                    Ask = Dec(coin[3]),
                    AskSize = Dec(coin[4]),
                    DailyChange = Dec(coin[5]),
                    DailyChangePercentage = Dec(coin[6]) * 100,
                    LastPrice = Dec(coin[7]),
                    Volume = Dec(coin[8]),
                    High = Dec(coin[9]),
                    Low = Dec(coin[10])
                });
            }
            catch
            {
            }
        }

        return coins;
    }

    private static string Str(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.GetRawText();

    private static decimal Dec(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number
            ? e.GetDecimal()
            : decimal.Parse(e.GetString() ?? "0", CultureInfo.InvariantCulture);
}