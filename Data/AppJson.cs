using System.Text.Json;
using System.Text.Json.Serialization;
using sblngavnav5X.Commands;
using static sblngavnav5X.Data.DataRoots;

namespace sblngavnav5X.Data
{
    [JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(CatData[]))]
    [JsonSerializable(typeof(Converter))]
    [JsonSerializable(typeof(WeatherApiBase))]
    [JsonSerializable(typeof(MealResponse))]
    [JsonSerializable(typeof(MyMemoryResult))]
    [JsonSerializable(typeof(List<BookExportDto>))]
    [JsonSerializable(typeof(List<List<JsonElement>>))]
    internal partial class AppJsonContext : JsonSerializerContext{}
}
