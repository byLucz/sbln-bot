using System.Text.Json.Serialization;

namespace sblngavnav6.Data
{
    public static class DataRoots
    {
        public static class States
        {
            public static int RealId { get; set; }
            public static List<string> Streamers { get; } = new();
            public static List<string> StreamerIds { get; } = new();
            public static Dictionary<string, string> StreamerMap { get; } = new();
            public static List<string> StatusText { get; } = new();
            public static List<string> StatusPos { get; } = new();
            public static List<string> StatusLink { get; } = new();
            public static List<string> StatusType { get; } = new();
        }

        public class Rates
        {
            public double BYN { get; set; }
            public double HKD { get; set; }
            public double USD { get; set; }
            public double EUR { get; set; }
            public double KZT { get; set; }
            public double CNY { get; set; }
            public double PLN { get; set; }
            public double TRY { get; set; }
            public double JPY { get; set; }
            public double RUB { get; set; }
        }

        public class BitfinexCoin
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

        public class Converter
        {
            public string @base { get; set; }
            public Rates rates { get; set; }
            public string source { get; set; }
            public DateTime localISODate { get; set; }
            public DateTime putISODate { get; set; }
        }
        public class CatData
        {
            [JsonPropertyName("url")] public Uri Url { get; set; }
        }

        public class BookWithRating
        {
            public int Id { get; set; }
            public string Title { get; set; }
            public string Authors { get; set; }
            public string SuggestedBy { get; set; }
            public double AvgScore { get; set; }
            public int Votes { get; set; }
            public string Image { get; set; }
        }

        public class VersionEntry
        {
            public string Version { get; set; }
            public DateTime Date { get; set; }
        }

        public class PackageVersionEntry
        {
            public string PackageName { get; set; }
            public string PackageVersion { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class RatingEntry
        {
            public string UserId { get; set; }
            public int BookId { get; set; }
            public int[] Scores { get; set; }
            public double FinalScore { get; set; }
        }

        public sealed class BookExportDto
        {
            public int id { get; set; }
            public string title { get; set; } = "";
            public string authors { get; set; } = "";
            public string suggestedBy { get; set; } = "";
            public int season { get; set; }
            public Dictionary<string, RatingExportDto> ratings { get; set; } = new();
        }

        public sealed class RatingExportDto
        {
            public int[] scores { get; set; } = [];
            public double final { get; set; }
        }

        public class MealResponse
        {
            [JsonPropertyName("meals")]
            public List<Meal>? Meals { get; set; }
        }

        public class Meal
        {
            public string? idMeal { get; set; }
            public string? strMeal { get; set; }
            public string? strCategory { get; set; }
            public string? strArea { get; set; }
            public string? strInstructions { get; set; }
            public string? strMealThumb { get; set; }
            public string? strTags { get; set; }
            public string? strYoutube { get; set; }
            public string? strSource { get; set; }

            public string? strIngredient1 { get; set; }
            public string? strMeasure1 { get; set; }
            public string? strIngredient2 { get; set; }
            public string? strMeasure2 { get; set; }
            public string? strIngredient3 { get; set; }
            public string? strMeasure3 { get; set; }
            public string? strIngredient4 { get; set; }
            public string? strMeasure4 { get; set; }
            public string? strIngredient5 { get; set; }
            public string? strMeasure5 { get; set; }
            public string? strIngredient6 { get; set; }
            public string? strMeasure6 { get; set; }
            public string? strIngredient7 { get; set; }
            public string? strMeasure7 { get; set; }
            public string? strIngredient8 { get; set; }
            public string? strMeasure8 { get; set; }
            public string? strIngredient9 { get; set; }
            public string? strMeasure9 { get; set; }
            public string? strIngredient10 { get; set; }
            public string? strMeasure10 { get; set; }
            public string? strIngredient11 { get; set; }
            public string? strMeasure11 { get; set; }
            public string? strIngredient12 { get; set; }
            public string? strMeasure12 { get; set; }
            public string? strIngredient13 { get; set; }
            public string? strMeasure13 { get; set; }
            public string? strIngredient14 { get; set; }
            public string? strMeasure14 { get; set; }
            public string? strIngredient15 { get; set; }
            public string? strMeasure15 { get; set; }
            public string? strIngredient16 { get; set; }
            public string? strMeasure16 { get; set; }
            public string? strIngredient17 { get; set; }
            public string? strMeasure17 { get; set; }
            public string? strIngredient18 { get; set; }
            public string? strMeasure18 { get; set; }
            public string? strIngredient19 { get; set; }
            public string? strMeasure19 { get; set; }
            public string? strIngredient20 { get; set; }
            public string? strMeasure20 { get; set; }
        }

        public class PpmMailbox
        {
            public int Id { get; set; }
            public string Email { get; set; }
            public string Password { get; set; }
            public string OwnerId { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? ExpiresAt { get; set; }
            public bool IsPermanent { get; set; }
        }

        public class MyMemoryResult
        {
            public MyMemoryData? responseData { get; set; }
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

        public class PpmMessageView
        {
            public string From { get; set; }
            public string Subject { get; set; }
            public DateTime Date { get; set; }
            public string Body { get; set; }
            public string Folder { get; set; }
        }

        public sealed record PastaEntry(string Author, string AvatarUrl, string Content);

        public sealed record ClubMemberStats(
            string Name,
            double Plot,
            double Style,
            double Characters,
            double Originality,
            double Vibe,
            double Total,
            double Stuffiness);

        public class MyMemoryData
        {
            public string? translatedText { get; set; }
        }
    }
}
