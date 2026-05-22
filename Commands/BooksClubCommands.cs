using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using sblngavnav5X.Data;
using sblngavnav5X.Services;

namespace sblngavnav5X.Commands
{
    public class BooksClubCommands : ModuleBase<SocketCommandContext>
    {
        private readonly HttpClient _http;
        private readonly DiscordSocketClient _client;

        private static ulong _ratingMsgId;
        private static List<Embed> _ratingPages = new();
        private static int _ratingPage;
        private static bool _ratingActive;
        private static readonly Dictionary<ulong, DateTime> _ratingLastClick = new();

        public BooksClubCommands(IHttpClientFactory httpClientFactory, DiscordSocketClient client)
        {
            _http = httpClientFactory.CreateClient();
            _client = client;
            _client.ReactionAdded -= OnRatingReactionAdded;
            _client.ReactionAdded += OnRatingReactionAdded;
        }

        [Command("книга")]
        public async Task FindBookAsync([Remainder] string title)
        {
            string url = $"https://www.googleapis.com/books/v1/volumes?q=intitle:{Uri.EscapeDataString(title)}&langRestrict=ru&key={Utils.gBooksApi}";

            HttpResponseMessage response = await _http.GetAsync(url);

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

            var response = await _http.GetAsync(url);
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
            TriggerBooksExport();

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

        private void TriggerBooksExport()
        {
            if (string.IsNullOrWhiteSpace(Utils.booksJsonPath)) return;
            var userNames = Context.Guild.Users
                .ToDictionary(u => u.Id.ToString(), u => u.Username);
            Task.Run(() =>
            {
                try { DataBase.ExportBooksJson(Utils.booksJsonPath, userNames); }
                catch (Exception ex) { _ = LoggingService.LogWarningAsync("BOOKS", $"JSON export fail: {ex.Message}"); }
            });
        }

        [Command("книжныйэкспорт")]
        public async Task ManualExportAsync()
        {
            if (string.IsNullOrWhiteSpace(Utils.booksJsonPath))
            {
                await ReplyAsync("❌ `booksJsonPath` не задан в Utils");
                return;
            }
            try
            {
                var userNames = Context.Guild.Users
                    .ToDictionary(u => u.Id.ToString(), u => u.Username);
                DataBase.ExportBooksJson(Utils.booksJsonPath, userNames);
                await ReplyAsync("✅ `books_data.json` обновлён");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Ошибка экспорта: {ex.Message}");
            }
        }

        [Command("рейтинг")]
        public async Task ShowSeasonRatingAsync(int? season = null)
        {
            _ratingPages = BuildSeasonEmbeds();
            if (_ratingPages.Count == 0)
                _ratingPages = new() { new EmbedBuilder().WithTitle("---").WithColor(Color.DarkGrey).Build() };

            int max = DataBase.GetMaxSeason();
            _ratingPage = season.HasValue && season.Value >= 1
                ? Math.Clamp(season.Value - 1, 0, _ratingPages.Count - 1)
                : 0;

            var msg = await ReplyAsync(embed: _ratingPages[_ratingPage]);
            _ratingMsgId = msg.Id;
            _ratingActive = true;

            await msg.AddReactionAsync(new Emoji("◀"));
            await msg.AddReactionAsync(new Emoji("▶"));
        }

        private async Task OnRatingReactionAdded(
            Cacheable<IUserMessage, ulong> message,
            Cacheable<IMessageChannel, ulong> channel,
            SocketReaction reaction)
        {
            try
            {
                if (reaction.UserId == _client.CurrentUser.Id) return;
                if (!_ratingActive) return;
                if (reaction.MessageId != _ratingMsgId) return;

                var now = DateTime.UtcNow;
                if (_ratingLastClick.TryGetValue(reaction.UserId, out var prev) && (now - prev).TotalSeconds < 1)
                    return;
                _ratingLastClick[reaction.UserId] = now;

                var msg = await message.GetOrDownloadAsync();
                if (msg is null) return;

                if (reaction.Emote.Name == "◀")
                {
                    if (_ratingPage > 0) _ratingPage--;
                }
                else if (reaction.Emote.Name == "▶")
                {
                    if (_ratingPage < _ratingPages.Count - 1) _ratingPage++;
                }
                else return;

                await msg.ModifyAsync(m => m.Embed = _ratingPages[_ratingPage]);

                var user = await msg.Channel.GetUserAsync(reaction.UserId);
                if (user != null)
                    try { await msg.RemoveReactionAsync(reaction.Emote, user); } catch { }
            }
            catch
            {
                await LoggingService.LogErrorAsync("knizhniy klub", "ошибка пагинации рейтинга");
            }
        }

        private static List<Embed> BuildSeasonEmbeds()
        {
            var list = new List<Embed>();
            int max = DataBase.GetMaxSeason();
            var seasons = max >= 1 ? Enumerable.Range(1, max).ToList() : new List<int> { 1 };
            int nextSeason = Math.Max(1, max) + 1;

            foreach (var s in seasons)
                list.Add(BuildSeasonEmbed(s, max));
            list.Add(BuildSeasonEmbed(nextSeason, max));

            int idx = list.FindIndex(e => e.Title?.EndsWith($"сезон {Math.Max(1, max)}") == true);
            if (idx > 0) { var first = list[idx]; list.RemoveAt(idx); list.Insert(0, first); }

            return list;
        }

        private static Embed BuildSeasonEmbed(int season, int maxSeason)
        {
            var eb = new EmbedBuilder()
                .WithTitle($"<:KKLOGO:1352283192014409869> Рейтинг клуба SZN#{season}")
                .WithColor(Color.Gold)
                .WithFooter("knizhniy klub📖");

            var books = DataBase.GetBooksWithRatings(season);
            if (books == null || books.Count == 0)
            {
                if (season > maxSeason && maxSeason >= 0)
                    eb.WithDescription("📭COMING SOON!");
                return eb.Build();
            }

            foreach (var b in books)
            {
                var emoji = Utils.GetScoreEmoji(b.AvgScore);
                eb.AddField(
                    $"📖 {b.Title} ({b.Authors})",
                    $"👤 {b.SuggestedBy}\n⭐ Средняя оценка: {b.AvgScore:F1} // {emoji} ({b.Votes} голосов)",
                    inline: false);
            }

            eb.WithDescription("[📊 Полный рейтинг на сайте](https://lois.media/sbln/books)");
            return eb.Build();
        }
    }
}