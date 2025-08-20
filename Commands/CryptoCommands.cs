using Discord;
using Discord.Commands;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace sblngavnav5X.Commands
{
    [Group("coins")]
    [Alias("монетки", "мон", "биток")]
    public class CryptoCommands : ModuleBase
    {
        private readonly HttpClient _httpClient;

        public CryptoCommands(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        [Command]
        public async Task GetCoins([Remainder]string unused = null)
        {
            HttpResponseMessage bfResponse;

            await Context.Channel.TriggerTypingAsync();
            try
            {
                bfResponse = await _httpClient.GetAsync("https://api-pub.bitfinex.com/v2/tickers?symbols=tBTCUSD,tETHUSD,tSOLUSD,tTONUSD");
            }
            catch
            {
                await ReplyAsync("битфинекс упал, статистики не будет((");
                return;
            }

            if (!bfResponse.IsSuccessStatusCode)
            {
                await ReplyAsync("битфинекс упал, статистики не будет((");
                return;
            }

            var results = JsonConvert.DeserializeObject<List<List<object>>>(await bfResponse.Content.ReadAsStringAsync());
            var coins = ConvertToBitfinexCoins(results);

            var btc = coins.Find(c => c.Symbol == "tBTCUSD");
            var eth = coins.Find(c => c.Symbol == "tETHUSD");
            var sol = coins.Find(c => c.Symbol == "tSOLUSD");
            var ton = coins.Find(c => c.Symbol == "tTONUSD");
            string Iconurl = $@"https://cdn0.iconfinder.com/data/icons/bitcoin-94/64/chip-bitcoin-512.png";


            var e = new EmbedBuilder()
            {
                Author = new EmbedAuthorBuilder()
                {
                    Name = "sbln крипта💰📈",
                },
                Color = Color.LightOrange,
                ThumbnailUrl = Iconurl,
                Footer = new EmbedFooterBuilder()
                {
                    Text = "powered by bitfinex💸"
                }

            };

            e.AddField("*BTC*", $"{btc.LastPrice.ToString("0.00#")}$", true);
            e.AddField("прирост", $"{btc.DailyChange.ToString("0.00#")}$", true);
            e.AddField("в процентах", $"({btc.DailyChangePercentage.ToString("0.00#")}%)", true);
            e.AddField("*ETH*", $"{eth.LastPrice.ToString("0.00#")}$", true);
            e.AddField("прирост", $"{eth.DailyChange.ToString("0.00#")}$", true);
            e.AddField("в процентах", $"({eth.DailyChangePercentage.ToString("0.00#")}%)", true);
            e.AddField("*SOL*", $"{sol.LastPrice.ToString("0.00#")}$", true);
            e.AddField("прирост", $"{sol.DailyChange.ToString("0.00#")}$", true);
            e.AddField("в процентах", $"({sol.DailyChangePercentage.ToString("0.00#")}%)", true);
            e.AddField("*TON*", $"{ton.LastPrice.ToString("0.00#")}$", true);
            e.AddField("прирост", $"{ton.DailyChange.ToString("0.00#")}$", true);
            e.AddField("в процентах", $"({ton.DailyChangePercentage.ToString("0.00#")}%)", true);

            await ReplyAsync(embed: e.Build());
        
      
        }

        private static List<BitfinexCoin> ConvertToBitfinexCoins(List<List<object>> obj)
        {
            var coins = new List<BitfinexCoin>();

            foreach (var coin in obj)
            {
                coins.Add(new BitfinexCoin
                {
                    Symbol = (string)coin[0],
                    Bid = Convert.ToDecimal(coin[1]),
                    BidSize = Convert.ToDecimal(coin[2]),
                    Ask = Convert.ToDecimal(coin[3]),
                    AskSize = Convert.ToDecimal(coin[4]),
                    DailyChange = Convert.ToDecimal(coin[5]),
                    DailyChangePercentage = Convert.ToDecimal(coin[6]) * 100,
                    LastPrice = Convert.ToDecimal(coin[7]),
                    Volume = Convert.ToDecimal(coin[8]),
                    High = Convert.ToDecimal(coin[9]),
                    Low = Convert.ToDecimal(coin[10])
                });
            }

            return coins;
        }

        private class BitfinexCoin
        {
            public string Symbol { get; set; }
            public decimal Bid { get; set; }
            public decimal BidSize { get; set; }
            public decimal Ask { get; set; }
            public decimal AskSize { get; set; }
            public decimal DailyChange { get; set; }
            public decimal DailyChangePercentage { get; set; }
            public decimal LastPrice { get; set; }
            public decimal Volume { get; set; }
            public decimal High { get; set; }
            public decimal Low { get; set; }
        }
    }
}
