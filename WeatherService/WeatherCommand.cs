using Discord.Commands;
using DiscordTelegramFrontier;
using sblngavnav6.Common;
using sblngavnav6.Core;

namespace sblngavnav6.Commands
{
    public sealed class WeatherCommand : ModuleBase<SocketCommandContext>
    {
        private readonly WeatherClient _weather;
        private readonly PaginatorService _pager;

        public WeatherCommand(WeatherClient weather, PaginatorService pager)
        {
            _weather = weather;
            _pager = pager;
        }

        [FrontierAsImage]
        [Command("погода", RunMode = RunMode.Async)]
        public async Task WeatherInfo(params string[] cityParts)
        {
            var city = string.Join(" ", cityParts ?? []).Trim();

            if (string.IsNullOrWhiteSpace(city))
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("погода", "напиши город: `погода Москва`"));
                return;
            }

            var result = await _weather.GetAsync(city);

            if (!result.Ok)
            {
                await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("погода", result.Error));
                return;
            }

            var pages = WeatherEmbeds.Pages(result);

            await _pager.SendAsync(
                Context.Channel,
                pages,
                decorate: pages.Count > 1 ? WeatherControls.Views(pages.Count) : null,
                pager: false);
        }
    }
}
