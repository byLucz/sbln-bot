using Discord;
using sblngavnav6.Common;
using static sblngavnav6.Common.CommonUtils.Text;
using static sblngavnav6.Data.DataRoots;

namespace sblngavnav6.Commands
{
    internal static class WeatherEmbeds
    {
        public static readonly string[] Views = ["сейчас", "24 часа", "5 дней"];

        private const string Source = "powered by openweathermap🗺";
        private const double MmPerHpa = 0.750062;
        private const int HoursAhead = 8;

        private static readonly string[] Winds = ["С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ"];
        private static readonly string[] Days = ["вс", "пн", "вт", "ср", "чт", "пт", "сб"];

        public static List<Embed> Pages(WeatherResult result)
        {
            var pages = new List<Embed> { Now(result.Current) };

            if (result.Forecast?.List is { Count: > 0 })
            {
                pages.Add(Hours(result.Current, result.Forecast));
                pages.Add(Daily(result.Current, result.Forecast));
            }

            return pages;
        }

        private static Embed Now(WeatherCurrent current)
        {
            var condition = Condition(current.Weather);
            var offset = Offset(current.Timezone);

            var fields = new List<EmbedFieldSpec>
            {
                new("🌡️ температура", $"**{Temp(current.Main.Temp)}**\nощущается {Temp(current.Main.FeelsLike)}", true),
                new("📊 за сутки", $"мин {Temp(current.Main.TempMin)}\nмакс {Temp(current.Main.TempMax)}", true),
                new($"{Icon(condition?.Icon)} состояние", Describe(condition), true),
                new("💨 ветер", $"{current.Wind?.Speed ?? 0:0.#} м/с {Direction(current.Wind?.Deg ?? 0)}"
                    + (current.Wind?.Gust > 0 ? $"\nпорывы {current.Wind.Gust:0.#} м/с" : ""), true),
                new("🌫️ влажность", $"{current.Main.Humidity:0}%", true),
                new("🌪️ давление", $"{current.Main.Pressure * MmPerHpa:0} мм рт. ст.", true),
                new("☁️ облачность", $"{current.Clouds?.All ?? 0}%", true),
                new("👁️ видимость", current.Visibility >= 10000 ? "больше 10 км" : $"{current.Visibility / 1000.0:0.#} км", true),
                new("🌄 восход", Clock(current.Sys?.Sunrise ?? 0, offset), true),
                new("🌆 закат", Clock(current.Sys?.Sunset ?? 0, offset), true)
            };

            return EmbedHandler.Build(new EmbedSpec
            {
                AuthorName = "sbln погода🥵🥶",
                Title = Place(current.Name, current.Sys?.Country),
                Color = Tint(current.Main.Temp),
                ThumbnailUrl = Art(condition?.Icon),
                Fields = fields,
                Footer = Footer(0)
            });
        }

        private static Embed Hours(WeatherCurrent current, WeatherForecast forecast)
        {
            var offset = Offset(forecast.City?.Timezone ?? current.Timezone);

            var rows = forecast.List
                .Take(HoursAhead)
                .Select(slot => (IReadOnlyList<string>)
                [
                    Clock(slot.Dt, offset),
                    Temp(slot.Main.Temp),
                    $"{slot.Pop * 100:0}%",
                    Describe(Condition(slot.Weather))
                ]);

            return EmbedHandler.Build(new EmbedSpec
            {
                AuthorName = "sbln погода🥵🥶",
                Title = Place(forecast.City?.Name ?? current.Name, forecast.City?.Country ?? current.Sys?.Country),
                Description = CodeTable(rows, "`нет данных`"),
                Color = Tint(current.Main.Temp),
                Footer = Footer(1)
            });
        }

        private static Embed Daily(WeatherCurrent current, WeatherForecast forecast)
        {
            var offset = Offset(forecast.City?.Timezone ?? current.Timezone);

            var rows = forecast.List
                .GroupBy(slot => Local(slot.Dt, offset).Date)
                .Select(day =>
                {
                    var slots = day.ToList();

                    return (IReadOnlyList<string>)
                    [
                        $"{Days[(int)day.Key.DayOfWeek]} {day.Key:dd.MM}",
                        $"{Temp(slots.Min(slot => slot.Main.TempMin))}..{Temp(slots.Max(slot => slot.Main.TempMax))}",
                        $"{slots.Max(slot => slot.Pop) * 100:0}%",
                        Describe(Dominant(slots))
                    ];
                });

            return EmbedHandler.Build(new EmbedSpec
            {
                AuthorName = "sbln погода🥵🥶",
                Title = Place(forecast.City?.Name ?? current.Name, forecast.City?.Country ?? current.Sys?.Country),
                Description = CodeTable(rows, "`нет данных`"),
                Color = Tint(current.Main.Temp),
                Footer = Footer(2)
            });
        }

        private static string Footer(int view) => $"{Views[view]} / {Source}";

        private static WeatherCondition Condition(List<WeatherCondition> conditions) =>
            conditions is { Count: > 0 } ? conditions[0] : null;

        private static WeatherCondition Dominant(List<WeatherSlot> slots) =>
            slots
                .Select(slot => Condition(slot.Weather))
                .Where(condition => condition is not null)
                .GroupBy(condition => condition.Icon?[..2] ?? "")
                .OrderByDescending(group => group.Count())
                .FirstOrDefault()?.First();

        private static string Describe(WeatherCondition condition)
        {
            var text = condition?.Description;

            return string.IsNullOrWhiteSpace(text) ? "неизвестно" : char.ToUpper(text[0]) + text[1..];
        }

        private static string Place(string city, string country)
        {
            var name = string.IsNullOrWhiteSpace(city) ? "город" : city;

            return string.IsNullOrWhiteSpace(country) ? name : $"{name}, {country}";
        }

        private static string Temp(double value) => $"{Math.Round(value):+0;-0;0}°";

        private static TimeSpan Offset(int seconds) => TimeSpan.FromSeconds(Math.Clamp(seconds, -50400, 50400));

        private static DateTimeOffset Local(long unix, TimeSpan offset) =>
            DateTimeOffset.FromUnixTimeSeconds(unix).ToOffset(offset);

        private static string Clock(long unix, TimeSpan offset) =>
            unix <= 0 ? "нет данных" : Local(unix, offset).ToString("HH:mm");

        private static string Direction(double degrees) =>
            Winds[(int)Math.Round(((degrees % 360) + 360) % 360 / 45) % Winds.Length];

        private static string Icon(string icon) => (icon is { Length: >= 2 } ? icon[..2] : "") switch
        {
            "01" => icon.EndsWith('n') ? "🌙" : "☀️",
            "02" => "🌤️",
            "03" => "⛅",
            "04" => "☁️",
            "09" => "🌧️",
            "10" => "🌦️",
            "11" => "⛈️",
            "13" => "❄️",
            "50" => "🌫️",
            _ => "🌡️"
        };

        private static string Art(string icon) =>
            string.IsNullOrWhiteSpace(icon) ? null : $"https://openweathermap.org/img/wn/{icon}@2x.png";

        private static Color Tint(double temp) => temp switch
        {
            >= 30 => Color.Red,
            >= 20 => Color.Orange,
            >= 10 => Color.Gold,
            >= 0 => Color.Teal,
            >= -10 => Color.Blue,
            _ => Color.DarkBlue
        };
    }
}
