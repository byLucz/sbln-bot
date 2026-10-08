using Discord;
using Discord.Commands;
using sblngavnav6.Common;
using static sblngavnav6.Common.CommonUtils.Chat;
using sblngavnav6.Data;
using static sblngavnav6.Data.DataRoots;
using sblngavnav6.Services;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DiscordTelegramFrontier;

namespace sblngavnav6.Commands;

public class ApiCommands : ModuleBase<SocketCommandContext>
{
    private const string CatApiUrl = "https://api.thecatapi.com/v1/images/search?format=json";
    private const string BitfinexApiUrl = "https://api-pub.bitfinex.com/v2/tickers?symbols=tBTCUSD,tETHUSD,tSOLUSD,tTONUSD";
    private const string CurrencyApiUrl = "https://www.cbr-xml-daily.ru/latest.js";

    private static readonly Regex JokeRegex =
        new(@"""content""\s*:\s*""(?<joke>.*?)""", RegexOptions.Singleline | RegexOptions.Compiled);


    private readonly HttpClient _http;

    static ApiCommands() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public ApiCommands(IHttpClientFactory httpClientFactory)
    {
        _http = httpClientFactory.CreateClient();
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    [FrontierAsImage]
    [Command("кит")]
    [Alias("кот")]
    public async Task UploadCat()
    {
        CatData cat;

        try
        {
            using var response = await _http.GetAsync(CatApiUrl);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            cat = JsonSerializer.Deserialize(content, AppJsonContext.Default.CatDataArray)?.FirstOrDefault();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            await FailAsync("кисики", ex.Message);
            return;
        }

        if (cat?.Url is null)
        {
            await FailAsync("кисики", "кота не выдали");
            return;
        }

        await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
        {
            Title = "sbln кисики😼",
            ImageUrl = cat.Url.OriginalString,
            Color = Color.Teal,
            Footer = "powered by thecatapi.com",
            Timestamp = true
        }));
    }

    [Frontier]
    [Command("биток")]
    [Alias("монетки", "мон")]
    public async Task GetCoins([Remainder] string unused = null)
    {
        const string down = "битфинекс упал, статистики не будет((";

        await Context.Channel.TriggerTypingAsync();

        List<BitfinexCoin> coins;

        try
        {
            using var response = await _http.GetAsync(BitfinexApiUrl);

            if (!response.IsSuccessStatusCode)
            {
                await ReplyAsync(down);
                return;
            }

            var content = await response.Content.ReadAsStringAsync();
            var results = JsonSerializer.Deserialize(content, AppJsonContext.Default.ListListJsonElement);

            coins = results is { Count: > 0 } ? ConvertToBitfinexCoins(results) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            await ReplyAsync(down);
            return;
        }

        (string Label, string Symbol)[] wanted =
        [
            ("BTC", "tBTCUSD"),
            ("ETH", "tETHUSD"),
            ("SOL", "tSOLUSD"),
            ("TON", "tTONUSD")
        ];

        var fields = new List<EmbedFieldSpec>();

        foreach (var (label, symbol) in wanted)
        {
            var coin = coins?.Find(item => item.Symbol == symbol);

            if (coin is null)
            {
                await ReplyAsync(down);
                return;
            }

            fields.Add(new EmbedFieldSpec($"*{label}*", $"{coin.LastPrice:0.00#}$", true));
            fields.Add(new EmbedFieldSpec("прирост", $"{coin.DailyChange:0.00#}$", true));
            fields.Add(new EmbedFieldSpec("в процентах", $"({coin.DailyChangePercentage:0.00#}%)", true));
        }

        await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
        {
            AuthorName = "sbln крипта💰📈",
            AuthorIconUrl = "https://cdn0.iconfinder.com/data/icons/bitcoin-94/64/chip-bitcoin-512.png",
            Color = Color.LightOrange,
            Fields = fields,
            Footer = "powered by bitfinex💸"
        }));
    }

    [Frontier]
    [Command("курс")]
    [Alias("кс")]
    public async Task Exchange()
    {
        Converter converter;

        try
        {
            using var response = await _http.GetAsync(CurrencyApiUrl);

            if (!response.IsSuccessStatusCode)
            {
                await FailAsync("курс валют", "сервис недоступен");
                return;
            }

            var content = await response.Content.ReadAsStringAsync();
            converter = JsonSerializer.Deserialize(content, AppJsonContext.Default.Converter);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            await FailAsync("курс валют", ex.Message);
            return;
        }

        if (converter?.rates is not { } rates)
        {
            await FailAsync("курс валют", "ответ без курсов");
            return;
        }

        (string Name, double Rate, bool Invert)[] values =
        [
            ("USD", rates.USD, true),
            ("EUR", rates.EUR, true),
            ("TRY", rates.TRY, true),
            ("PLN", rates.PLN, true),
            ("CNY", rates.CNY, true),
            ("BYN", rates.BYN, true),
            ("JPY", rates.JPY, true),
            ("HKD", rates.HKD, true),
            ("KZT", rates.KZT, false)
        ];

        var fields = values
            .Where(item => item.Rate > 0)
            .Select(item => new EmbedFieldSpec(
                item.Name,
                $"{Math.Round(item.Invert ? 1 / item.Rate : item.Rate, 2)}₽",
                true))
            .ToList();

        await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
        {
            AuthorName = "sbln курс валют💱💵",
            Color = Color.DarkTeal,
            ThumbnailUrl = "https://upload.wikimedia.org/wikipedia/commons/1/18/Russia-Coin-1-2009-a.png",
            Fields = fields,
            Footer = "powered by CENTROBANK OF RUSSIA🏦🇷🇺"
        }));
    }

    [Frontier]
    [Command("шутка")]
    [Alias("анек")]
    public async Task JokeTask([Optional] string category)
    {
        const string author = "sbln шутки 😂";
        const string footer = "powered by rzhunemogu.ru";

        (string Id, int Category, string Name)[] categories =
        [
            ("1", 11, "Анекдоты"),
            ("2", 12, "Рассказы"),
            ("3", 13, "Стишки")
        ];

        var picked = categories.FirstOrDefault(item => item.Id == category);

        if (picked.Id is null)
        {
            var menu = string.Join("\n", categories.Select(item => $" {item.Id} - {item.Name}"));
            var header = category is null ? "Выберите категорию" : "Выберите категорию (ТОЛЬКО ИЗ СПИСКА!)";

            await ReplyAsync(embed: EmbedHandler.Authored(author, $"{header}\n{menu}", Color.Orange, footer));
            return;
        }

        var joke = await FetchJokeAsync(picked.Category);

        if (joke is null)
        {
            await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("шутки", "ржаки не будет, сервис не отвечает"));
            return;
        }

        var message = await ReplyAsync(embed: EmbedHandler.Authored(author, joke, Color.Orange, footer));
        await ReactAsync(message, "<:slyr4head:816639053008338944>");
    }

    private async Task<string> FetchJokeAsync(int categoryId)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync($"http://rzhunemogu.ru/RandJSON.aspx?CType={categoryId}");
                response.EnsureSuccessStatusCode();

                var bytes = await response.Content.ReadAsByteArrayAsync();
                var body = Encoding.GetEncoding(1251).GetString(bytes);

                var start = body.IndexOf('{', StringComparison.Ordinal);
                var end = body.LastIndexOf('}');

                if (start == -1 || end <= start)
                    throw new InvalidOperationException("ответ без json");

                var payload = body[start..(end + 1)];
                string joke;

                try { joke = JsonNode.Parse(payload)?["content"]?.ToString(); }
                catch (JsonException) { joke = JokeRegex.Match(payload) is { Success: true } hit ? hit.Groups["joke"].Value : null; }

                if (!string.IsNullOrWhiteSpace(joke))
                    return joke;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
            {
                await LoggingService.LogWarningAsync("JOKES", $"шутка не пришла: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(400));
        }

        return null;
    }

    private static List<BitfinexCoin> ConvertToBitfinexCoins(List<List<JsonElement>> rows)
    {
        var coins = new List<BitfinexCoin>();

        foreach (var row in rows)
        {
            if (row is null || row.Count < 11)
                continue;

            try
            {
                coins.Add(new BitfinexCoin
                {
                    Symbol = Str(row[0]),
                    Bid = Dec(row[1]),
                    BidSize = Dec(row[2]),
                    Ask = Dec(row[3]),
                    AskSize = Dec(row[4]),
                    DailyChange = Dec(row[5]),
                    DailyChangePercentage = Dec(row[6]) * 100,
                    LastPrice = Dec(row[7]),
                    Volume = Dec(row[8]),
                    High = Dec(row[9]),
                    Low = Dec(row[10])
                });
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
            }
        }

        return coins;
    }

    private static string Str(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText();

    private static decimal Dec(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number
            ? element.GetDecimal()
            : decimal.Parse(element.GetString() ?? "0", CultureInfo.InvariantCulture);

    private async Task FailAsync(string source, string reason) =>
        await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed(source, reason));
}
