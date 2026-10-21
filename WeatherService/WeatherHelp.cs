using Newtonsoft.Json;
using sblngavnav5X.Data;
namespace sblngavnav5X.Commands

{
   public class WeatherHelp
   {
       private HttpClient _Client;
       public WeatherHelp(HttpClient c)
       {
         _Client =c;
       }
       public async Task<WeatherSer> GetCityWeather(string City)
       {
           string Url = WeatherUrl.GetCityWeatherUrl(City,Utils.weatherApiKey);
           var  r = await _Client.GetAsync(Url);
           if(r.StatusCode == System.Net.HttpStatusCode.OK)
           {
               try
               {
                WeatherSer b = JsonConvert.DeserializeObject<WeatherSer>(await r.Content.ReadAsStringAsync());
                b.isValid = b.main != null && b.weather?.Any() == true;
                if (!b.isValid) b.Errors = "сервис погоды вернул пустой ответ";
                return b;
               }
               catch(Exception e)
               {
                  return new WeatherSer(){isValid = false , Errors=e.Message};
               }
           }
           else
           {
               return new WeatherSer{isValid=false,Errors=r.ReasonPhrase,HttpCode=r.StatusCode.ToString()};
           }
       }
   }
}
