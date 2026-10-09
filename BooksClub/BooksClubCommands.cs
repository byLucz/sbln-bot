using Discord;
using Discord.Commands;
using DiscordTelegramFrontier;
using sblngavnav6.Common;
using sblngavnav6.Core;
using sblngavnav6.Data;
using sblngavnav6.Services;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static sblngavnav6.Common.CommonUtils.Text;
using static sblngavnav6.Data.DataRoots;

namespace sblngavnav6.BooksClub
{
    public class BooksClubCommands : ModuleBase<SocketCommandContext>
    {
        internal const string Logo = "<:KKLOGO:1352283192014409869>";
        internal const string Footer = "knizhniy klub📖";

        private readonly HttpClient _http;
        private readonly PaginatorService _pager;

        public BooksClubCommands(IHttpClientFactory httpClientFactory, PaginatorService pager)
        {
            _http = httpClientFactory.CreateClient();
            _pager = pager;
        }

        [Frontier]
        [Command("книга")]
        public async Task FindBookAsync([Remainder] string title)
        {
            var info = await FetchVolumeAsync(title);

            if (info is null)
            {
                await FailAsync("книга не найдена или гугл недоступен");
                return;
            }

            var subtitle = Text(info, "subtitle", null);
            var year = Text(info, "publishedDate", null);
            var pages = info["pageCount"]?.GetValue<int>() ?? 0;
            var genre = (info["categories"] as JsonArray)?.FirstOrDefault()?.ToString();
            var rating = info["averageRating"]?.ToString();
            var description = Regex.Replace(Text(info, "description", "Описания нет"), "<[^>]+>", " ");

            var fields = new List<EmbedFieldSpec> { new("✍️ Автор(ы)", Authors(info), true) };

            if (year is not null)
                fields.Add(new EmbedFieldSpec("📅 Год", year.Length >= 4 ? year[..4] : year, true));

            if (pages > 0)
                fields.Add(new EmbedFieldSpec("📄 Страниц", pages.ToString(), true));

            if (Text(info, "publisher", null) is { } publisher)
                fields.Add(new EmbedFieldSpec("🏢 Издательство", publisher, true));

            if (genre is not null)
                fields.Add(new EmbedFieldSpec("🏷️ Жанр", genre, true));

            if (rating is not null)
                fields.Add(new EmbedFieldSpec("⭐ Рейтинг", $"{rating} ({Text(info, "ratingsCount", "0")} оценок)", true));

            await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
            {
                Title = $"{Logo} {Text(info, "title", "Неизвестно")}{(subtitle is null ? "" : $". {subtitle}")}",
                Url = Text(info, "infoLink", null),
                Description = Truncate(CollapseSpaces(description), 400),
                ThumbnailUrl = Thumbnail(info),
                Color = Color.Purple,
                Fields = fields,
                Footer = Footer
            }));
        }

        [RequireGuild]
        [Command("выбор книги")]
        public async Task SelectBook([Remainder] string input = "")
        {
            var trimmed = input?.Trim() ?? string.Empty;

            if (trimmed.Equals("отмена", StringComparison.OrdinalIgnoreCase))
            {
                var last = await DataBase.GetLastBook();

                if (last.id == 0)
                {
                    await FailAsync("нечего отменять, книга не выбрана");
                    return;
                }

                if (await DataBase.BookHasRatings(last.id))
                {
                    await FailAsync($"**{last.title}** уже оценивали, отменить нельзя");
                    return;
                }

                await DataBase.RemoveLastBook();
                await ReplyAsync(embed: Simple("✅ Последняя выбранная книга отменена", Color.Purple));
                return;
            }

            if (!await DataBase.CanSelectNewBook() || string.IsNullOrWhiteSpace(trimmed))
            {
                var current = await DataBase.GetLastBook();

                await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
                {
                    Title = $"{Logo} Книга недели уже выбрана",
                    Description =
                        $"**{current.title}**\n" +
                        $"✍️ Автор(ы): {current.authors}\n" +
                        $"📅 {current.selectedDate:yyyy-MM-dd}\n" +
                        $"👤 {current.suggestedBy}",
                    ThumbnailUrl = current.image,
                    Color = Color.DarkPurple,
                    Footer = Footer
                }), components: current.id == 0 ? null : _pager.BuildControls(BookRating.OpenButton()));
                return;
            }

            var info = await FetchVolumeAsync(trimmed);

            if (info is null)
            {
                await FailAsync("книга не найдена или гугл недоступен");
                return;
            }

            var title = Text(info, "title", "Неизвестно");
            var authors = Authors(info);
            var image = Thumbnail(info);

            await DataBase.AddBook(title, authors, image, Context.User.Username);

            await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
            {
                Title = $"{Logo} Книга недели выбрана!",
                Description = $"**{title}**\n✍️ {authors}\n📅 {DateTime.UtcNow:yyyy-MM-dd}\n👤 Выбрал: {Context.User.Username}",
                ThumbnailUrl = image,
                Color = Color.Purple,
                Footer = Footer
            }), components: _pager.BuildControls(BookRating.OpenButton()));
        }

        [RequireGuild]
        [Command("оценить")]
        public async Task Rate([Remainder] string input = null)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                var current = await DataBase.GetLastBook();

                if (current.id == 0)
                {
                    await FailAsync("книга недели не выбрана, оценивать нечего");
                    return;
                }

                await ReplyAsync(
                    embed: Simple($"⭐ Оценка книги **{current.title}**, жми кнопку", Color.Purple),
                    components: _pager.BuildControls(BookRating.OpenButton()));
                return;
            }

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 5)
            {
                await FailAsync("нужно 5 чисел, например: `х оценить 8 7 9 10 9`");
                return;
            }

            if (!DataBase.TryParseScores(parts, out var scores))
            {
                await FailAsync("оценки должны быть числами от 1 до 10");
                return;
            }

            var book = await DataBase.GetLastBook();

            if (book.id == 0)
            {
                await FailAsync("книга недели не выбрана, оценивать нечего");
                return;
            }

            if (await DataBase.UserHasRated(Context.User.Id.ToString(), book.id))
            {
                await FailAsync("эта книга уже получала твою оценку");
                return;
            }

            var finalScore = BookRating.Final(scores);

            await DataBase.SaveRating(Context.User.Id.ToString(), book.id, scores, finalScore);
            BookRating.TriggerExport(Context.Guild);

            await ReplyAsync(embed: BookRating.Result(book.title, book.authors, Context.User.Username, scores, finalScore));
        }

        [RequireGuild]
        [Command("клуб")]
        public Task ClubInfoAsync() => ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
        {
            Title = $"{Logo} Добро пожаловать в KNIZHNIY KLUB!",
            Description =
                "В нашем клубе мы читаем, обсуждаем и оцениваем книги по специальной <:KK90:1352292878252249191> балльной системе. " +
                "На про4тение дается одна неделя, книга должна быть не более 400-500 страниц. Дефолтный день сбора клуба - **четверг**\n\n" +
                "<:KKLOGO2:1352293663031558186> **Как оценивать книги?**\n" +
                "Используется четыре базовых критерия (по 10 баллов) + множитель **вайба**\n" +
                "Финальная оценка рассчитывается по специальной формуле",
            Color = Color.Gold,
            Fields =
            [
                new EmbedFieldSpec("📖 1. Сюжет/драматургия", "Насколько интересна история, логичность развития событий, глубина конфликта"),
                new EmbedFieldSpec("🖊️ 2. Стиль/язык", "Выразительность, богатство, ритм повествования и грамотность текста"),
                new EmbedFieldSpec("👥 3. Герои/характеры", "Насколько персонажи глубоки, проработаны, их мотивация реалистична"),
                new EmbedFieldSpec("💡 4. Оригинальность/влияние", "Влияет ли книга на жанр, есть ли новизна и авторский стиль"),
                new EmbedFieldSpec("🌌 5. Вайб", "Передаёт ли книга эмоции? Насколько она захватывает?"),
                new EmbedFieldSpec("📊 **Формула расчёта**",
                    "<:KK30:1352292869179965544> (Сюжет + Стиль + Герои + Оригинальность) × 1.4\n" +
                    "<:KK60:1352292871260344400> Умножаем на множитель **вайба** (от 1.00 до 1.6072)\n" +
                    "<:KK90:1352292878252249191> является максимально возможной оценкой"),
                new EmbedFieldSpec("🌡️ **Как работает индекс душноты**",
                    "1️⃣ Берётся разница между средним по книге и вашей оценкой (только если вы ниже среднего)\n" +
                    "2️⃣ Для чужих пиков книг разница умножается на 2\n" +
                    "3️⃣ Усреднённая полученная величина нормируется на 56 (фундаментальные оценки без множителя) и переводится в %"),
                new EmbedFieldSpec("📚 **Доступные команды**",
                    "**х книга (название)** — ищет книгу по названию\n" +
                    "**х выбор книги (название)** — установка книги недели (или `отмена` для отмены)\n" +
                    "**х оценить** — оценить книгу недели (или сразу `х оценить 8 9 7 10 9`)\n" +
                    "**х рейтинг** — показать текущий рейтинг клуба\n" +
                    "**х членыклуба** — статистика по средним оценкам и индексу душноты\n" +
                    "**х книжныйэкспорт** — пересобрать books_data.json")
            ],
            Footer = Footer
        }));

        [RequireGuild]
        [Command("членыклуба")]
        public async Task ClubMembersAsync()
        {
            var all = await DataBase.GetAllRatings();

            if (all.Count == 0)
            {
                await FailAsync("пока нет ни одной оценки");
                return;
            }

            var owners = await DataBase.GetBookSuggesters();

            var bookAverages = all
                .GroupBy(rating => rating.BookId)
                .ToDictionary(group => group.Key, group => group.Average(rating => Normalized(rating.FinalScore, rating.Scores[4])));

            var stats = all
                .GroupBy(rating => rating.UserId)
                .Select(group => BuildMemberStats(group.Key, group.ToList(), bookAverages, owners))
                .OrderByDescending(member => member.Stuffiness)
                .ToList();

            var pages = stats
                .Chunk(6)
                .Select(chunk => EmbedHandler.Build(new EmbedSpec
                {
                    Title = $"{Logo} Члены клуба и их показатели",
                    Color = Color.DarkPurple,
                    Fields = chunk.Select(member => new EmbedFieldSpec($"Mr. {member.Name}",
                        "📊 **Средние по критериям:**\n" +
                        $"Сюжет: **{member.Plot:F1}**  Стиль: **{member.Style:F1}**\n" +
                        $"Герои: **{member.Characters:F1}**  Оригинальность: **{member.Originality:F1}**\n" +
                        $"Вайб: **{member.Vibe:F1}**\n" +
                        $"⭐ Общий средний: **{member.Total:F1}**\n" +
                        $"🥵 **Индекс душноты:** **{member.Stuffiness:F0}%**")).ToArray(),
                    Footer = Footer
                }))
                .ToList();

            await _pager.SendAsync(Context.Channel, pages);
        }

        [RequireGuild]
        [Command("рейтинг")]
        public async Task ShowSeasonRatingAsync(int? season = null)
        {
            var max = await DataBase.GetMaxSeason();
            var seasons = Enumerable.Range(1, Math.Max(1, max) + 1).ToList();

            var pages = new List<Embed>();
            var starts = new Dictionary<int, int>();

            foreach (var number in seasons)
            {
                starts[number] = pages.Count;
                pages.AddRange(await BuildSeasonPages(number, max));
            }

            var start = season.HasValue && starts.TryGetValue(season.Value, out var index) ? index : starts[Math.Max(1, max)];

            await _pager.SendAsync(Context.Channel, pages, startPage: start);
        }

        private static async Task<List<Embed>> BuildSeasonPages(int season, int maxSeason)
        {
            var books = await DataBase.GetBooksWithRatings(season);

            if (books is not { Count: > 0 })
            {
                return
                [
                    EmbedHandler.Build(new EmbedSpec
                    {
                        Title = $"{Logo} Рейтинг клуба SZN#{season}",
                        Description = season > maxSeason ? "📭COMING SOON!" : "тут пока пусто",
                        Color = Color.Gold,
                        Footer = Footer
                    })
                ];
            }

            return books
                .Chunk(10)
                .Select(chunk => EmbedHandler.Build(new EmbedSpec
                {
                    Title = $"{Logo} Рейтинг клуба SZN#{season}",
                    Description = "[📊 Полный рейтинг на сайте](https://lois.media/sbln/books)",
                    Color = Color.Gold,
                    Fields = chunk.Select(book => new EmbedFieldSpec(
                        $"📖 {book.Title} ({book.Authors})",
                        $"👤 {book.SuggestedBy}\n⭐ Средняя оценка: {book.AvgScore:F1} // {CommonUtils.GetScoreEmoji(book.AvgScore)} ({book.Votes} голосов)")).ToArray(),
                    Footer = Footer
                }))
                .ToList();
        }

        [RequireGuild]
        [Command("книжныйэкспорт")]
        public async Task ManualExportAsync()
        {
            if (string.IsNullOrWhiteSpace(Global.Vars.Cfg.booksJsonPath))
            {
                await FailAsync("`Books:BooksJsonPath` не задан в конфиге");
                return;
            }

            try
            {
                await DataBase.ExportBooksJson(Global.Vars.Cfg.booksJsonPath, BookRating.UserNames(Context.Guild));
                await ReplyAsync(embed: Simple("✅ `books_data.json` обновлён", Color.Green));
            }
            catch (Exception ex) when (CommonUtils.IsIoFailure(ex))
            {
                await FailAsync($"экспорт не удался: {ex.Message}");
            }
        }

        private ClubMemberStats BuildMemberStats(
            string userId,
            List<DataRoots.RatingEntry> ratings,
            Dictionary<int, double> bookAverages,
            IReadOnlyDictionary<int, string> owners)
        {
            var name = Context.Guild.GetUser(ulong.Parse(userId))?.Username;

            var differences = ratings
                .Select(rating =>
                {
                    var diff = bookAverages[rating.BookId] - Normalized(rating.FinalScore, rating.Scores[4]);

                    if (diff <= 0)
                        return 0.0;

                    owners.TryGetValue(rating.BookId, out var owner);

                    return diff * (owner is not null && owner != name ? 2.0 : 1.0);
                })
                .Where(diff => diff > 0)
                .ToList();

            var vibe = ratings.Average(rating => rating.Scores[4]);
            var basePercent = differences.Count > 0
                ? Math.Min(100.0, Math.Round(differences.Average() / (40 * 1.4) * 100.0))
                : 0.0;

            return new ClubMemberStats(
                name ?? $"<@{userId}>",
                ratings.Average(rating => rating.Scores[0]),
                ratings.Average(rating => rating.Scores[1]),
                ratings.Average(rating => rating.Scores[2]),
                ratings.Average(rating => rating.Scores[3]),
                vibe,
                ratings.Average(rating => rating.Scores.Take(5).Average()),
                Math.Max(0, Math.Round(basePercent * (1.0 - vibe / 10.0))));
        }

        private async Task<JsonNode> FetchVolumeAsync(string title)
        {
            var url = $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString(title)}&langRestrict=ru&country={Global.Vars.Cfg.booksCountry}";

            if (!string.IsNullOrWhiteSpace(Global.Vars.Cfg.gBooksApi))
                url += $"&key={Global.Vars.Cfg.gBooksApi}";

            try
            {
                using var response = await _http.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var reason = JsonNode.Parse(body)?["error"]?["message"]?.ToString() ?? response.ReasonPhrase;
                    await LoggingService.LogWarningAsync("BOOKS", $"Google Books ответил {(int)response.StatusCode}: {reason}");
                    return null;
                }

                var volumes = (JsonNode.Parse(body)?["items"] as JsonArray)?
                    .Select(item => item?["volumeInfo"])
                    .Where(info => info is not null)
                    .ToList() ?? [];

                var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                var volume = volumes
                    .OrderByDescending(info =>
                    {
                        var name = info["title"]?.ToString() ?? "";
                        return name.Contains(title.Trim(), StringComparison.OrdinalIgnoreCase) ? 2
                            : words.All(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)) ? 1
                            : 0;
                    })
                    .ThenByDescending(info => !string.IsNullOrWhiteSpace(info["description"]?.ToString()))
                    .ThenByDescending(info => Thumbnail(info) is not null)
                    .FirstOrDefault();

                if (volume is null)
                    await LoggingService.LogWarningAsync("BOOKS", $"Google Books ничего не нашёл по \"{title}\"");

                return volume;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                await LoggingService.LogWarningAsync("BOOKS", $"поиск книги не удался: {ex.Message}");
                return null;
            }
        }

        private static double Normalized(double finalScore, int vibe) => finalScore / BookRating.Multiplier(vibe);

        private static string Text(JsonNode info, string field, string fallback)
        {
            var value = info[field]?.ToString();

            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string Authors(JsonNode info) =>
            info["authors"] is JsonArray authors && authors.Count > 0
                ? string.Join(", ", authors.Select(author => author?.ToString()))
                : "Автор неизвестен";

        private static string Thumbnail(JsonNode info) =>
            info["imageLinks"]?["thumbnail"]?.ToString().Replace("http://", "https://").Replace("&edge=curl", "");

        private static Embed Simple(string description, Color color) => EmbedHandler.Build(new EmbedSpec
        {
            Description = description,
            Color = color,
            Footer = Footer
        });

        private async Task FailAsync(string reason) =>
            await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed("книжный клуб", reason));
    }
}
