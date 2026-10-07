using System.Text.Json.Serialization;
using static sblngavnav6.Data.DataRoots;

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
}
