using Discord;
using Discord.Commands;

namespace sblngavnav5X.Commands
{
    public class WeatherCommand:ModuleBase<SocketCommandContext>
    {
        private WeatherHelp _WService;
        public DateTime sunrise;
        public DateTime sunset;

        public WeatherCommand(WeatherHelp wh)
        {
            _WService = wh;
        }
        [Command("погода")]
        public async Task WeatherInfo(params string[] cityname)
        {
           string city= "" ;
           foreach(string s in cityname)
           {
              city += $"{s} "; 
           }
           WeatherSer b =  await _WService.GetCityWeather(city);
           if(b.isValid)
           {
           List<WeatherModel> models= b.weather.ToList<WeatherModel>();
                DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                sunrise = dtDateTime.AddSeconds(b.sysSc.sunrise).ToLocalTime();
                sunset = dtDateTime.AddSeconds(b.sysSc.sunset).ToLocalTime();
                string Iconurl =  $@"http://openweathermap.org/img/wn/{models[0].icon}@2x.png";
           var e = new EmbedBuilder()
           {
               Author = new EmbedAuthorBuilder()
               {
                   Name = "sbln погода🥵🥶",
               },
               Color = Color.Gold,
               ThumbnailUrl = Iconurl,
               Footer = new EmbedFooterBuilder()
               {
                   Text = "powered by openweathermap🗺"
               }
           };
           e.AddField("погода на данный момент",$"в городе {city}",false);
           e.AddField("🌡температура", $"{Convert.ToDecimal(b.main.temp)-273}°",true);
                e.AddField("🤒ощущается как", $"{Convert.ToDecimal(b.main.feels_like)-273}°",true);
                e.AddField("🧊мин.температура", $"{Convert.ToDecimal(b.main.temp_min)-273}°", true);
                e.AddField("🌈детали", $"{models[0].description}",true);
                e.AddField("🌪давление", $"{Convert.ToDecimal(b.main.pressure)}hPa", true);
                e.AddField("🌫влажность", $"{Convert.ToDecimal(b.main.humidity)}%", true);
                e.AddField("💨ветер", $"{Convert.ToDecimal(b.windSc.speed)}m/s", true);
                e.AddField("🌄восход", $"{sunrise}", true);
                e.AddField("🌆закат", $"{sunset}", true);
                await ReplyAsync(embed:e.Build());
           }
           else
           {
                await ReplyAsync(b.Errors);
           }
        }

    }

}