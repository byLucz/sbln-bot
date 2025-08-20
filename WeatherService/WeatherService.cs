namespace sblngavnav5X.Commands;


public static class WeatherUrl
{
    public static string GetCityWeatherUrl(string CityName, string ApiKey) => $@"https://api.openweathermap.org/data/2.5/weather?q={CityName}&appid={ApiKey}";
}
public class WeatherApiBase
{
    public IEnumerable<WeatherModel> weather { get; set; }
    public MainWeather main { get; set; }
    public Wind windSc { get; set; }
    public Sys sysSc { get; set; }

}

public class WeatherSer
{ 
    public IEnumerable<WeatherModel> weather { get; set; }
    public MainWeather main { get; set; }
    public Wind windSc { get; set; }
    public Sys sysSc { get; set; }
    public bool isValid { get; set; }
    public string Errors { get; set; }
    public string HttpCode { get; set; }
}

public class WeatherModel
{
    public string id { get; set; }
    public string main { get; set; }
    public string description { get; set; }
    public string icon { get; set; }
}
public class MainWeather
{
    public float temp { get; set; }
    public float feels_like { get; set; }
    public float temp_min { get; set; }
    public float temp_max { get; set; }
    public float pressure { get; set; }
    public float humidity { get; set; }
}

public class Sys
{
    public float sunrise { get; set; }
    public float sunset { get; set; }
}
public class Wind 
{ 
    public float speed { get; set; }
}