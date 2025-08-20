using Discord;
using Discord.Commands;
using Discord.Interactions;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using static System.Net.WebRequestMethods;

namespace sblngavnav5X.Commands
{
    public class MealCommands : ModuleBase<SocketCommandContext>
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        [Command("рецепт")]
        public async Task RandomRecipeAsync()
        {
            var meal = await GetRandomMeal();
            if (meal is null)
            {
                await ReplyAsync("Не удалось получить рецепт 🙈");
                return;
            }

            var msg = await ReplyAsync("🍳 Паркурю кукинг…");

            var ruName = await TranslateAsync(meal.strMeal);
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
                .AddField("Категория", ruCat, inline: true)
                .AddField("Кухня", ruKitchen, inline: true);

            if (!string.IsNullOrWhiteSpace(ingredientsStr))
                eb.AddField("Ингредиенты", Trunc(ingredientsStr, 1024), inline: false);

            if (!string.IsNullOrWhiteSpace(meal.strYoutube))
                eb.AddField("YouTube", meal.strYoutube, inline: false);

            await msg.ModifyAsync(m =>
            {
                m.Content = "";
                m.Embed = eb.Build();
            });
        }

        private async Task<Meal?> GetRandomMeal()
        {
            var url = "https://www.themealdb.com/api/json/v1/1/random.php";
            var resp = await _http.GetFromJsonAsync<MealResponse>(url);
            return resp?.Meals?.FirstOrDefault();
        }

        private static IEnumerable<string> BuildIngredients(Meal m)
        {
            for (int i = 1; i <= 20; i++)
            {
                var ing = GetProp(m, $"strIngredient{i}");
                var mea = GetProp(m, $"strMeasure{i}");
                if (string.IsNullOrWhiteSpace(ing)) continue;

                ing = ing.Trim();
                mea = (mea ?? string.Empty).Trim();

                yield return $"• {(!string.IsNullOrEmpty(mea) ? mea + " " : "")}{ing}";
            }

            static string? GetProp(Meal m, string name)
                => m.GetType().GetProperty(name)?.GetValue(m) as string;
        }

        private async Task<string> TranslateAsync(string text)
        {
            text = text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return text;

            var libreUrl = "https://libretranslate.com";

            try
            {
                var chunks = ChunkForMyMemory(text);
                var translatedChunks = new List<string>(chunks.Count);

                foreach (var c in chunks)
                {
                    var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(c)}&langpair=en|ru";
                    using var res = await _http.GetAsync(url);
                    if (!res.IsSuccessStatusCode)
                    {
                        if ((int)res.StatusCode == 429 || (int)res.StatusCode == 503)
                            await Task.Delay(800);
                        translatedChunks.Add(c);
                        continue;
                    }

                    var mm = await res.Content.ReadFromJsonAsync<MyMemoryResult>();
                    var t = WebUtility.HtmlDecode(mm?.responseData?.translatedText ?? "");
                    translatedChunks.Add(string.IsNullOrWhiteSpace(t) ? c : t);

                    await Task.Delay(250);
                }

                var joined = string.Join("", translatedChunks);
                if (!string.IsNullOrWhiteSpace(joined)) return joined;
            }
            catch { }
            return text;
        }
        private async Task<List<string>> TranslateManyAsync(IEnumerable<string> items)
        {
            var src = items?.ToList() ?? new();
            var outList = new List<string>(src.Count);

            foreach (var s in src)
            {
                var ru = await TranslateAsync(s ?? string.Empty);
                outList.Add(string.IsNullOrWhiteSpace(ru) ? s ?? string.Empty : ru);
                await Task.Delay(200);
            }

            return outList;
        }
        private static List<string> ChunkForMyMemory(string text, int hardLimit = 500, int safety = 80)
        {
            int limit = Math.Max(120, hardLimit - safety);

            var sentences = Regex.Split(text, @"(?<=[\.\!\?\n])\s+");
            var chunks = new List<string>();
            var current = new List<string>();

            void flushCurrent()
            {
                if (current.Count == 0) return;
                var s = string.Join(" ", current);
                chunks.Add(s);
                current.Clear();
            }

            foreach (var sent in sentences)
            {
                var candidate = current.Count == 0 ? sent : string.Join(" ", current) + " " + sent;
                if (Uri.EscapeDataString(candidate).Length <= limit)
                {
                    current.Add(sent);
                    continue;
                }

                if (Uri.EscapeDataString(sent).Length > limit)
                {
                    flushCurrent();
                    var words = sent.Split(' ');
                    var buf = new List<string>();
                    foreach (var w in words)
                    {
                        var cand2 = buf.Count == 0 ? w : string.Join(" ", buf) + " " + w;
                        if (Uri.EscapeDataString(cand2).Length <= limit)
                        {
                            buf.Add(w);
                        }
                        else
                        {
                            if (buf.Count > 0) chunks.Add(string.Join(" ", buf));
                            buf.Clear();
                            buf.Add(w);
                        }
                    }
                    if (buf.Count > 0) chunks.Add(string.Join(" ", buf));
                }
                else
                {
                    flushCurrent();
                    current.Add(sent);
                }
            }
            flushCurrent();
            return chunks;
        }

        private static string Trunc(string s, int max)
            => s.Length <= max ? s : s[..max] + "…";

        private sealed class MealResponse
        {
            [JsonPropertyName("meals")]
            public List<Meal>? Meals { get; set; }
        }

        private sealed class Meal
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

        private sealed class MyMemoryResult
        {
            public MyMemoryData? responseData { get; set; }
        }
        private sealed class MyMemoryData
        {
            public string? translatedText { get; set; }
        }
    }
}