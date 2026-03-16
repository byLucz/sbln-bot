using Discord;
using Discord.Commands;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using static sblngavnav5X.Data.DataRoots;

namespace sblngavnav5X.Commands
{
    public class MealCommands : ModuleBase<SocketCommandContext>
    {
        private readonly HttpClient _http;

        private const string RandomMealUrl = "https://www.themealdb.com/api/json/v1/1/random.php";
        private const string MyMemoryTranslateUrl = "https://api.mymemory.translated.net/get";
        private static readonly Regex SentenceSplitRegex = new(@"(?<=[\.\!\?\n])\s+", RegexOptions.Compiled);

        public MealCommands(IHttpClientFactory httpClientFactory)
        {
            _http = httpClientFactory.CreateClient();
            _http.Timeout = TimeSpan.FromSeconds(10);
        }

        [Command("рецепт")]
        public async Task RandomRecipeAsync()
        {
            Meal? meal;

            try
            {
                meal = await GetRandomMealAsync();
            }
            catch
            {
                meal = null;
            }

            if (meal is null)
            {
                await ReplyAsync("Не удалось получить рецепт 🙈");
                return;
            }

            var msg = await ReplyAsync("🍳 Паркурю кукинг…");

            var ruName = await TranslateAsync(meal.strMeal ?? string.Empty);
            var ruInstr = await TranslateAsync(meal.strInstructions ?? string.Empty);
            var ruCat = await TranslateAsync(meal.strCategory ?? "—");
            var ruKitchen = await TranslateAsync(meal.strArea ?? "—");

            var ingredients = BuildIngredients(meal).ToList();
            var ruIngredients = await TranslateManyAsync(ingredients);
            var ingredientsStr = string.Join("\n", ruIngredients.Take(25));

            var eb = new EmbedBuilder()
                .WithTitle(string.IsNullOrWhiteSpace(ruName) ? meal.strMeal : ruName)
                .WithUrl(meal.strSource ?? meal.strYoutube ?? "https://www.themealdb.com")
                .WithImageUrl(meal.strMealThumb)
                .WithThumbnailUrl("https://media.discordapp.net/attachments/500682551296393231/1405588340836794439/slyrChef.png?ex=689f5fa7&is=689e0e27&hm=132f3dd57fd685bea30cd4b8dc1e3ba235447f5798243dd96acbc0542269cb33&=&format=webp&quality=lossless&width=230&height=230")
                .WithColor(new Color(139, 92, 246))
                .WithDescription(Trunc(string.IsNullOrWhiteSpace(ruInstr) ? meal.strInstructions : ruInstr, 2000))
                .WithFooter("sbln рецепты от шефчика👨‍🍳")
                .AddField("Категория", string.IsNullOrWhiteSpace(ruCat) ? "—" : ruCat, true)
                .AddField("Кухня", string.IsNullOrWhiteSpace(ruKitchen) ? "—" : ruKitchen, true);

            if (!string.IsNullOrWhiteSpace(ingredientsStr))
                eb.AddField("Ингредиенты", Trunc(ingredientsStr, 1024), false);

            if (!string.IsNullOrWhiteSpace(meal.strYoutube))
                eb.AddField("YouTube", meal.strYoutube, false);

            await msg.ModifyAsync(m =>
            {
                m.Content = "";
                m.Embed = eb.Build();
            });
        }

        private async Task<Meal?> GetRandomMealAsync()
        {
            var resp = await _http.GetFromJsonAsync<MealResponse>(RandomMealUrl);
            return resp?.Meals?.FirstOrDefault();
        }

        private static IEnumerable<string> BuildIngredients(Meal meal)
        {
            var ingredients = new[]
            {
                meal.strIngredient1, meal.strIngredient2, meal.strIngredient3, meal.strIngredient4, meal.strIngredient5,
                meal.strIngredient6, meal.strIngredient7, meal.strIngredient8, meal.strIngredient9, meal.strIngredient10,
                meal.strIngredient11, meal.strIngredient12, meal.strIngredient13, meal.strIngredient14, meal.strIngredient15,
                meal.strIngredient16, meal.strIngredient17, meal.strIngredient18, meal.strIngredient19, meal.strIngredient20
            };

            var measures = new[]
            {
                meal.strMeasure1, meal.strMeasure2, meal.strMeasure3, meal.strMeasure4, meal.strMeasure5,
                meal.strMeasure6, meal.strMeasure7, meal.strMeasure8, meal.strMeasure9, meal.strMeasure10,
                meal.strMeasure11, meal.strMeasure12, meal.strMeasure13, meal.strMeasure14, meal.strMeasure15,
                meal.strMeasure16, meal.strMeasure17, meal.strMeasure18, meal.strMeasure19, meal.strMeasure20
            };

            for (int i = 0; i < ingredients.Length; i++)
            {
                var ing = ingredients[i]?.Trim();
                var mea = measures[i]?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(ing))
                    continue;

                yield return $"• {(!string.IsNullOrEmpty(mea) ? mea + " " : "")}{ing}";
            }
        }

        private async Task<string> TranslateAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            try
            {
                var chunks = ChunkForMyMemory(text);
                if (chunks.Count == 0)
                    return text;

                var translatedChunks = new List<string>(chunks.Count);

                foreach (var chunk in chunks)
                {
                    var url =
                        $"{MyMemoryTranslateUrl}?q={Uri.EscapeDataString(chunk)}&langpair=en|ru";

                    using var res = await _http.GetAsync(url);

                    if (!res.IsSuccessStatusCode)
                    {
                        if ((int)res.StatusCode is 429 or 503)
                            await Task.Delay(800);

                        translatedChunks.Add(chunk);
                        continue;
                    }

                    var mm = await res.Content.ReadFromJsonAsync<MyMemoryResult>();
                    var translated = WebUtility.HtmlDecode(mm?.responseData?.translatedText ?? "");

                    translatedChunks.Add(string.IsNullOrWhiteSpace(translated) ? chunk : translated);

                    await Task.Delay(250);
                }

                var joined = string.Join("", translatedChunks);
                return string.IsNullOrWhiteSpace(joined) ? text : joined;
            }
            catch
            {
                return text;
            }
        }

        private async Task<List<string>> TranslateManyAsync(IEnumerable<string> items)
        {
            var source = items?.ToList() ?? new List<string>();
            var result = new List<string>(source.Count);

            foreach (var item in source)
            {
                var ru = await TranslateAsync(item ?? string.Empty);
                result.Add(string.IsNullOrWhiteSpace(ru) ? item ?? string.Empty : ru);
                await Task.Delay(200);
            }

            return result;
        }

        private static List<string> ChunkForMyMemory(string text, int hardLimit = 500, int safety = 80)
        {
            var limit = Math.Max(120, hardLimit - safety);

            var sentences = SentenceSplitRegex.Split(text);
            var chunks = new List<string>();
            var current = new List<string>();

            void FlushCurrent()
            {
                if (current.Count == 0)
                    return;

                chunks.Add(string.Join(" ", current));
                current.Clear();
            }

            foreach (var sentence in sentences)
            {
                if (string.IsNullOrWhiteSpace(sentence))
                    continue;

                var candidate = current.Count == 0
                    ? sentence
                    : string.Join(" ", current) + " " + sentence;

                if (Uri.EscapeDataString(candidate).Length <= limit)
                {
                    current.Add(sentence);
                    continue;
                }

                if (Uri.EscapeDataString(sentence).Length > limit)
                {
                    FlushCurrent();

                    var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var buffer = new List<string>();

                    foreach (var word in words)
                    {
                        var candidateWord = buffer.Count == 0
                            ? word
                            : string.Join(" ", buffer) + " " + word;

                        if (Uri.EscapeDataString(candidateWord).Length <= limit)
                        {
                            buffer.Add(word);
                        }
                        else
                        {
                            if (buffer.Count > 0)
                                chunks.Add(string.Join(" ", buffer));

                            buffer.Clear();
                            buffer.Add(word);
                        }
                    }

                    if (buffer.Count > 0)
                        chunks.Add(string.Join(" ", buffer));
                }
                else
                {
                    FlushCurrent();
                    current.Add(sentence);
                }
            }

            FlushCurrent();
            return chunks;
        }

        private static string Trunc(string? s, int max)
        {
            s ??= string.Empty;
            return s.Length <= max ? s : s[..max] + "…";
        }
    }
}