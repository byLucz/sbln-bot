using System.Text.Json.Serialization;

namespace sblngavnav5X.Data
{
    public static class DataRoots
    {
        public static class States
        {
            public static int RealId { get; set; }
            public static List<string> YaicaList { get; } = new();
            public static List<string> VolkList { get; } = new();
            public static List<string> PatList { get; } = new();
            public static List<string> FffList { get; } = new();
            public static List<string> HugList { get; } = new();
            public static List<string> KissList { get; } = new();
            public static List<string> KusList { get; } = new();
            public static List<string> BuhatList { get; } = new();
            public static List<string> EbaloList { get; } = new();
            public static List<string> Streamers { get; } = new();
            public static List<string> StreamerIds { get; } = new();
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

        // Почтовый ящик PechkinPostManager (строка таблицы temp_mailboxes).
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

        public class MyMemoryData
        {
            public string? translatedText { get; set; }
        }
    }
}
