using System.Text.Json.Serialization;

namespace sblngavnav6.Commands
{
    public static class WeatherUrl
    {
        private const string Root = "https://api.openweathermap.org/data/2.5";

        public static string Current(string city, string apiKey) =>
            $"{Root}/weather?q={Uri.EscapeDataString(city)}&units=metric&lang=ru&appid={apiKey}";

        public static string Forecast(string city, string apiKey) =>
            $"{Root}/forecast?q={Uri.EscapeDataString(city)}&units=metric&lang=ru&appid={apiKey}";
    }

    public sealed class WeatherCondition
    {
        public int Id { get; set; }
        public string Main { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; }
    }

    public sealed class WeatherMain
    {
        public double Temp { get; set; }
        [JsonPropertyName("feels_like")] public double FeelsLike { get; set; }
        [JsonPropertyName("temp_min")] public double TempMin { get; set; }
        [JsonPropertyName("temp_max")] public double TempMax { get; set; }
        public double Pressure { get; set; }
        public double Humidity { get; set; }
    }

    public sealed class WeatherWind
    {
        public double Speed { get; set; }
        public double Deg { get; set; }
        public double Gust { get; set; }
    }

    public sealed class WeatherSys
    {
        public string Country { get; set; }
        public long Sunrise { get; set; }
        public long Sunset { get; set; }
    }

    public sealed class WeatherClouds
    {
        public int All { get; set; }
    }

    public sealed class WeatherCurrent
    {
        public List<WeatherCondition> Weather { get; set; }
        public WeatherMain Main { get; set; }
        public WeatherWind Wind { get; set; }
        public WeatherSys Sys { get; set; }
        public WeatherClouds Clouds { get; set; }
        public int Visibility { get; set; }
        public long Dt { get; set; }
        public int Timezone { get; set; }
        public string Name { get; set; }
    }

    public sealed class WeatherSlot
    {
        public long Dt { get; set; }
        public WeatherMain Main { get; set; }
        public List<WeatherCondition> Weather { get; set; }
        public WeatherWind Wind { get; set; }
        public double Pop { get; set; }
    }

    public sealed class WeatherCity
    {
        public string Name { get; set; }
        public string Country { get; set; }
        public int Timezone { get; set; }
        public long Sunrise { get; set; }
        public long Sunset { get; set; }
    }

    public sealed class WeatherForecast
    {
        public List<WeatherSlot> List { get; set; }
        public WeatherCity City { get; set; }
    }

    public sealed record WeatherResult(WeatherCurrent Current, WeatherForecast Forecast, string Error)
    {
        public bool Ok => Error is null && Current is not null;

        public static WeatherResult Failed(string error) => new(null, null, error);
    }
}
