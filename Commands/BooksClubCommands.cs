using Discord;
using Discord.Commands;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using sblngavnav5X.Data;

namespace sblngavnav5X.Commands
{
    public class BooksClubCommands : ModuleBase<SocketCommandContext>
    {

        [Command("книга")]
        public async Task FindBookAsync([Remainder] string title)
        {
            string url = $"https://www.googleapis.com/books/v1/volumes?q=intitle:{Uri.EscapeDataString(title)}&langRestrict=ru";

            using HttpClient client = new HttpClient();
            HttpResponseMessage response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                await ReplyAsync("❌ Ошибка при поиске книги");
                return;
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();
            JObject json = JObject.Parse(jsonResponse);

            var firstBook = json["items"]?[0]?["volumeInfo"];
            if (firstBook == null)
            {
                await ReplyAsync("❌ Книга не найдена");
                return;
            }

            string bookTitle = firstBook["title"]?.ToString() ?? "Неизвестно";
            string authors = firstBook["authors"] != null ? string.Join(", ", firstBook["authors"]) : "Автор неизвестен";
            string publishedDate = firstBook["publishedDate"]?.ToString() ?? "Неизвестно";
            string pageCount = firstBook["pageCount"]?.ToString() ?? "Не указано";
            string rating = firstBook["averageRating"]?.ToString() ?? "Нет оценок";
            string ratingCount = firstBook["ratingsCount"]?.ToString() ?? "0";
            string bookUrl = firstBook["infoLink"]?.ToString() ?? "Нет ссылки";

            string description = firstBook["description"]?.ToString() ?? "Нет описания";
            if (description.Length > 200)
            {
                description = description.Substring(0, 200) + "...";
            }

            string imageUrl = firstBook["imageLinks"]?["thumbnail"]?.ToString() ?? "";

            var embed = new EmbedBuilder()
                .WithTitle($"<:KKLOGO:1352283192014409869> {bookTitle}")
                .WithDescription($"✍️ **Автор(ы):** {authors}\n📅 **Год издания:** {publishedDate}\n📄 **Страниц:** {pageCount}" +
                $"\n🖼️ **Описание:** {description}")
                .AddField("🔗 Подробнее", $"[Gbooks]({bookUrl})", true)
                .WithColor(Color.Purple)
                .WithFooter("knizhniy klub📖");

            if (!string.IsNullOrEmpty(imageUrl))
                embed.WithThumbnailUrl(imageUrl);

            await ReplyAsync(embed: embed.Build());
        }

        [Command("выбор книги")]
        public async Task SelectBook([Remainder] string input = "")
        {
            string trimmedInput = input?.Trim().ToLower() ?? "";

            if (trimmedInput == "отмена")
            {
                if (DataBase.GetLastBook().id == 0)
                {
                    await ReplyAsync("❌ Нечего отменять — книга не выбрана");
                    return;
                }

                DataBase.RemoveLastBook();
                await ReplyAsync("✅ Последняя выбранная книга отменена");
                return;
            }

            if (!DataBase.CanSelectNewBook() || string.IsNullOrWhiteSpace(trimmedInput))
            {
                var book = DataBase.GetLastBook();
                var embed = new EmbedBuilder()
                    .WithTitle("<:KKLOGO:1352283192014409869> Книга недели уже выбрана")
                    .WithDescription($"**{book.title}**\n✍️ Автор(ы): {book.authors}\n📅 {book.selectedDate:yyyy-MM-dd}\n👤 {book.suggestedBy}")
                    .WithThumbnailUrl(book.image)
                    .WithColor(Color.DarkPurple)
                    .WithFooter("knizhniy klub📖");
                await ReplyAsync(embed: embed.Build());
                return;
            }

            string url = $"https://www.googleapis.com/books/v1/volumes?q=intitle:{Uri.EscapeDataString(input)}&langRestrict=ru";

            using var client = new HttpClient();
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                await ReplyAsync("❌ Ошибка при поиске книги");
                return;
            }

            string json = await response.Content.ReadAsStringAsync();
            var root = JObject.Parse(json);
            var info = root["items"]?[0]?["volumeInfo"];

            if (info == null)
            {
                await ReplyAsync("❌ Книга не найдена");
                return;
            }

            string title = info["title"]?.ToString() ?? "Неизвестно";
            string authors = info["authors"] != null ? string.Join(", ", info["authors"]) : "Автор неизвестен";
            string image = info["imageLinks"]?["thumbnail"]?.ToString() ?? "";

            DataBase.AddBook(title, authors, image, Context.User.Username);

            var embedNew = new EmbedBuilder()
                .WithTitle("<:KKLOGO:1352283192014409869> Книга недели выбрана!")
                .WithDescription($"**{title}**\n✍️ {authors}\n📅 {DateTime.UtcNow:yyyy-MM-dd}\n👤 Выбрал: {Context.User.Username}")
                .WithThumbnailUrl(image)
                .WithColor(Color.Purple)
                .WithFooter("knizhniy klub📖");
            await ReplyAsync(embed: embedNew.Build());
        }

        [Command("оценить")]
        public async Task Rate([Remainder] string input)
        {
            var parts = input.Split(' ');
            if (parts.Length != 5)
            {
                await ReplyAsync("❌ Введите 5 чисел, например: `х оценить 8 7 9 10 9`");
                return;
            }

            if (!DataBase.TryParseScores(parts, out var scores))
            {
                await ReplyAsync("❌ Оценки должны быть числами от 1 до 10");
                return;
            }

            var book = DataBase.GetLastBook();

            if (DataBase.UserHasRated(Context.User.Id.ToString(), book.id))
            {
                await ReplyAsync("⚠️ Эта книга уже получала оценку");
                return;
            }

            double baseScore = (scores[0] + scores[1] + scores[2] + scores[3]) * 1.4;
            double multiplier = 1 + (scores[4] - 1) * 0.06747;
            double finalScore = Math.Round(baseScore * multiplier, 0);

            string scoreEmoji = Utils.GetScoreEmoji(finalScore);
            DataBase.SaveRating(Context.User.Id.ToString(), book.id, scores, finalScore);

            var embed = new EmbedBuilder()
                .WithTitle($"<:KKLOGO:1352283192014409869> {book.title}")
                .WithDescription($"✍️ **Автор(ы):** {book.authors}")
                .AddField("📢 Оценка пользователя", $"{Context.User.Username}", false)
                .AddField("📜 Сюжет/драматургия", $"{scores[0]}", true)
                .AddField("🖊️ Стиль/язык", $"{scores[1]}", true)
                .AddField("👥 Герои/характеры", $"{scores[2]}", true)
                .AddField("💡 Оригинальность/влияние", $"{scores[3]}", true)
                .AddField("🌌 Вайб", $"{scores[4]}", true)
                .AddField("⭐ Итоговый балл", $"{finalScore} // {scoreEmoji}", false)
                .WithColor(Color.Purple)
                .WithFooter("knizhniy klub📖");

            await ReplyAsync(embed: embed.Build());
        }

        [Command("клуб")]
        public async Task ClubInfoAsync()
        {
            var embed = new EmbedBuilder()
                .WithTitle("<:KKLOGO:1352283192014409869> Добро пожаловать в KNIZHNIY KLUB!")
                .WithDescription(
                    "В нашем клубе мы читаем, обсуждаем и оцениваем книги по специальной <:KK90:1352292878252249191> балльной системе. " +
                    "На про4тение дается одна неделя, книга должна быть не более 400-500 страниц. Дефолтный день сбора клуба - **четверг**\n\n" +
                    "<:KKLOGO2:1352293663031558186> **Как оценивать книги?**\n" +
                    "Используется четыре базовых критерия (по 10 баллов) + множитель **вайба**\n" +
                    "Финальная оценка рассчитывается по специальной формуле"
                )
                .AddField("📖 1. Сюжет/драматургия", "Насколько интересна история, логичность развития событий, глубина конфликта", false)
                .AddField("🖊️ 2. Стиль/язык", "Выразительность, богатство, ритм повествования и грамотность текста", false)
                .AddField("👥 3. Герои/характеры", "Насколько персонажи глубоки, проработаны, их мотивация реалистична", false)
                .AddField("💡 4. Оригинальность/влияние", "Влияет ли книга на жанр, есть ли новизна и авторский стиль", false)
                .AddField("🌌 5. Вайб", "Передаёт ли книга эмоции? Насколько она захватывает?", false)
                .AddField("📊 **Формула расчёта**",
                    "<:KK30:1352292869179965544> (Сюжет + Стиль + Герои + Оригинальность) × 1.4\n" +
                    "<:KK60:1352292871260344400> Умножаем на множитель **вайба** (от 1.00 до 1.6072)\n" +
                    "<:KK90:1352292878252249191> является максимально возможной оценкой", false)
                .AddField("🌡️ **Как работает индекс душноты**",
                    "1️⃣ Берётся разница между средним по книге и вашей оценкой (только если вы ниже среднего)\n" +
                    "2️⃣ Для чужих пиков  книг разница умножается на 2\n" +
                    "3️⃣ Усреднённая полученная величина нормируется на 56 (фундаментальные оценки без множителя) и переводится в %", false)
                .AddField("📚 **Доступные команды**",
                    "**х книга (название)** — ищет книгу по названию\n" +
                    "**х выбор книги (название)** — установка книги недели (или `отмена` для отмены)\n" +
                    "**х оценить 8 9 7 10 9 ** — оценить выбранную книгу по критериям\n" +
                    "**х рейтинг** — показать текущий рейтинг клуба\n" +
                    "**х членыклуба** — статистика по средним оценкам и индексу душноты", false)
                .WithColor(Color.Gold)
                .WithFooter("knizhniy klub📖");

            await ReplyAsync(embed: embed.Build());
        }


        [Command("членыклуба")]
        public async Task ClubMembersAsync()
        {
            var all = DataBase.GetAllRatings();
            var owners = DataBase.GetBookSuggesters();
            if (!all.Any())
            {
                await ReplyAsync("❌ Пока нет ни одной оценки.");
                return;
            }

            var avgNormByBook = all
                .GroupBy(r => r.BookId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Average(r =>
                    {
                        double vm = 1 + (r.Scores[4] - 1) * 0.06747;
                        return r.FinalScore / vm;
                    })
                );

            const double MaxBase = (10 + 10 + 10 + 10) * 1.4;

            var stats = all
                .GroupBy(r => r.UserId)
                .Select(g =>
                {
                    string userId = g.Key;
                    var list = g.ToList();

                    double avgPlot = list.Average(x => x.Scores[0]);
                    double avgStyle = list.Average(x => x.Scores[1]);
                    double avgChars = list.Average(x => x.Scores[2]);
                    double avgOrig = list.Average(x => x.Scores[3]);
                    double avgVibe = list.Average(x => x.Scores[4]);
                    double avgTotal = (avgPlot + avgStyle + avgChars + avgOrig + avgVibe) / 5.0;

                    var diffs = list.Select(r =>
                    {
                        double norm = r.FinalScore / (1 + (r.Scores[4] - 1) * 0.06747);
                        double bookAvg = avgNormByBook[r.BookId];
                        double diff = bookAvg - norm;
                        if (diff <= 0) return 0.0;

                        owners.TryGetValue(r.BookId, out var owner);
                        bool other = owner != null && owner != Context.Guild.GetUser(ulong.Parse(userId))?.Username;
                        return diff * (other ? 2.0 : 1.0);
                    })
                    .Where(d => d > 0)
                    .ToList();

                    double avgDiff = diffs.Any() ? diffs.Average() : 0.0;
                    double basePct = Math.Min(100.0, Math.Round(avgDiff / MaxBase * 100.0));

                    double vibeFactor = 1.0 - avgVibe / 10.0;
                    double finalPct = Math.Round(basePct * vibeFactor);
                    if (finalPct < 0) finalPct = 0;

                    string name = Context.Guild.GetUser(ulong.Parse(userId))?.Username
                                  ?? $"<@{userId}>";

                    return new
                    {
                        Name = name,
                        AvgPlot = avgPlot,
                        AvgStyle = avgStyle,
                        AvgChars = avgChars,
                        AvgOrig = avgOrig,
                        AvgVibe = avgVibe,
                        AvgTotal = avgTotal,
                        DushPct = finalPct
                    };
                })
                .OrderByDescending(u => u.DushPct)
                .ToList();

            var embed = new EmbedBuilder()
                .WithTitle("<:KKLOGO:1352283192014409869> Члены клуба и их показатели")
                .WithColor(Color.DarkPurple)
                .WithFooter("knizhniy клуб📖");

            foreach (var u in stats)
            {
                embed.AddField(
                    "Mr. " + u.Name,
                    $"📊 **Средние по критериям:**\n" +
                    $"Сюжет: **{u.AvgPlot:F1}**  Стиль: **{u.AvgStyle:F1}**\n" +
                    $"Герои: **{u.AvgChars:F1}**  Оригинальность: **{u.AvgOrig:F1}**\n" +
                    $"Вайб: **{u.AvgVibe:F1}**\n" +
                    $"⭐ Общий средний: **{u.AvgTotal:F1}**\n" +
                    $"🥵 **Индекс душноты:** **{u.DushPct:F0}%**",
                    inline: false
                );
            }

            await ReplyAsync(embed: embed.Build());
        }
    }
}