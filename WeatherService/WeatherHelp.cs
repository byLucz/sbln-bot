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
                WeatherApiBase b =  JsonConvert.DeserializeObject<WeatherApiBase>(await r.Content.ReadAsStringAsync());
                return new WeatherSer(){main = b.main , weather = b.weather , windSc = b.windSc, sysSc = b.sysSc, isValid = true};
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