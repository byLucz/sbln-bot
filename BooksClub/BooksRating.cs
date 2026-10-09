using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using sblngavnav6.Common;
using sblngavnav6.Data;
using sblngavnav6.Services;

namespace sblngavnav6.BooksClub
{
    internal static class BookRating
    {
        public const string OpenId = "kkr_open";

        public static readonly (string Emoji, string Name)[] Criteria =
        [
            ("📜", "Сюжет/драматургия"),
            ("🖊️", "Стиль/язык"),
            ("👥", "Герои/характеры"),
            ("💡", "Оригинальность/влияние"),
            ("🌌", "Вайб")
        ];

        public static double Multiplier(int vibe) => 1 + (vibe - 1) * 0.06747;

        public static double Base(IReadOnlyList<int> scores) => (scores[0] + scores[1] + scores[2] + scores[3]) * 1.4;

        public static double Final(IReadOnlyList<int> scores) => Math.Round(Base(scores) * Multiplier(scores[4]), 0);

        public static MessageComponent OpenButton() =>
            new ComponentBuilder()
                .WithButton("Оценить", OpenId, ButtonStyle.Primary, new Emoji("⭐"))
                .Build();

        public static int[] Decode(string state)
        {
            var scores = new int[Criteria.Length];

            for (var i = 0; i < scores.Length && i < state.Length; i++)
                scores[i] = state[i] switch
                {
                    >= '1' and <= '9' => state[i] - '0',
                    'a' => 10,
                    _ => 0
                };

            return scores;
        }

        public static string Encode(IReadOnlyList<int> scores) =>
            string.Concat(scores.Select(score => score switch
            {
                10 => 'a',
                >= 1 and <= 9 => (char)('0' + score),
                _ => '0'
            }));

        public static Embed Draft(string title, string authors, IReadOnlyList<int> scores)
        {
            var filled = scores.Count(score => score > 0);

            string total;

            if (filled == Criteria.Length)
            {
                var final = Final(scores);
                total = $"**{final}** // {CommonUtils.GetScoreEmoji(final)}\n" +
                        $"база {Base(scores):0.#} × вайб {Multiplier(scores[4]):0.###}";
            }
            else if (scores.Take(4).All(score => score > 0))
            {
                total = $"база {Base(scores):0.#}, осталось выбрать вайб";
            }
            else
            {
                total = $"заполнено {filled}/{Criteria.Length}";
            }

            return EmbedHandler.Build(new EmbedSpec
            {
                Title = $"{BooksClubCommands.Logo} {title}",
                Description = $"✍️ **Автор(ы):** {authors}",
                Color = filled == Criteria.Length ? Color.Gold : Color.Purple,
                Fields = Criteria
                    .Select((criterion, index) => new EmbedFieldSpec(
                        $"{criterion.Emoji} {criterion.Name}",
                        scores[index] > 0 ? scores[index].ToString() : "-",
                        true))
                    .Append(new EmbedFieldSpec("⭐ Итоговый балл", total))
                    .ToArray(),
                Footer = filled == Criteria.Length
                    ? $"{BooksClubCommands.Footer} / проверь и сохрани"
                    : $"{BooksClubCommands.Footer} / выбери оценки 1-10"
            });
        }

        public static MessageComponent Selects(int bookId, IReadOnlyList<int> scores)
        {
            var state = Encode(scores);
            var builder = new ComponentBuilder();

            for (var index = 0; index < Criteria.Length; index++)
            {
                var (emoji, name) = Criteria[index];
                var menu = new SelectMenuBuilder()
                    .WithCustomId($"kkr:{bookId}:{index}:{state}")
                    .WithPlaceholder($"{emoji} {name}")
                    .WithMinValues(1)
                    .WithMaxValues(1);

                for (var value = 1; value <= 10; value++)
                    menu.AddOption($"{emoji} {name}: {value}", value.ToString(), isDefault: scores[index] == value);

                builder.WithSelectMenu(menu, row: index);
            }

            return builder.Build();
        }

        public static MessageComponent Confirm(int bookId, IReadOnlyList<int> scores)
        {
            var state = Encode(scores);

            return new ComponentBuilder()
                .WithButton("Сохранить", $"kkr_save:{bookId}:{state}", ButtonStyle.Success, new Emoji("💾"))
                .WithButton("Изменить", $"kkr_edit:{bookId}:{state}", ButtonStyle.Secondary, new Emoji("✏️"))
                .Build();
        }

        public static Embed Result(string title, string authors, string userName, IReadOnlyList<int> scores, double final) =>
            EmbedHandler.Build(new EmbedSpec
            {
                Title = $"{BooksClubCommands.Logo} {title}",
                Description = $"✍️ **Автор(ы):** {authors}",
                Color = Color.Purple,
                Fields = Criteria
                    .Select((criterion, index) => new EmbedFieldSpec($"{criterion.Emoji} {criterion.Name}", scores[index].ToString(), true))
                    .Prepend(new EmbedFieldSpec("📢 Оценка пользователя", userName))
                    .Append(new EmbedFieldSpec("⭐ Итоговый балл", $"{final} // {CommonUtils.GetScoreEmoji(final)}"))
                    .ToArray(),
                Footer = BooksClubCommands.Footer
            });

        public static Dictionary<string, string> UserNames(SocketGuild guild) =>
            guild.Users.ToDictionary(user => user.Id.ToString(), user => user.Username);

        public static void TriggerExport(SocketGuild guild)
        {
            if (string.IsNullOrWhiteSpace(Global.Vars.Cfg.booksJsonPath))
                return;

            var userNames = UserNames(guild);

            _ = Task.Run(async () =>
            {
                try { await DataBase.ExportBooksJson(Global.Vars.Cfg.booksJsonPath, userNames); }
                catch (Exception ex) { await LoggingService.LogWarningAsync("BOOKS", $"JSON export fail: {ex.Message}"); }
            });
        }
    }

    public class BooksRatingInteractions : InteractionModuleBase<SocketInteractionContext>
    {
        private static readonly SemaphoreSlim SaveGate = new(1, 1);

        [ComponentInteraction(BookRating.OpenId)]
        public async Task OpenAsync()
        {
            if (Context.Guild is null)
            {
                await RespondAsync("только на сервере", ephemeral: true);
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

            var scores = new int[BookRating.Criteria.Length];

            await RespondAsync(
                embed: BookRating.Draft(book.title, book.authors, scores),
                components: BookRating.Selects(book.id, scores),
                ephemeral: true);
        }

        [ComponentInteraction("kkr:*:*:*")]
        public async Task PickAsync(string bookRaw, string indexRaw, string state, string[] values)
        {
            if (!int.TryParse(bookRaw, out var bookId) ||
                !int.TryParse(indexRaw, out var index) ||
                index < 0 || index >= BookRating.Criteria.Length ||
                values is not { Length: > 0 } ||
                !int.TryParse(values[0], out var value) || value is < 1 or > 10)
            {
                await DeferAsync();
                return;
            }

            var book = await CurrentBookAsync(bookId);
            if (book is null)
                return;

            var scores = BookRating.Decode(state);
            scores[index] = value;

            var complete = scores.All(score => score > 0);

            await UpdateAsync(
                BookRating.Draft(book.Value.title, book.Value.authors, scores),
                complete ? BookRating.Confirm(bookId, scores) : BookRating.Selects(bookId, scores));
        }

        [ComponentInteraction("kkr_edit:*:*")]
        public async Task EditAsync(string bookRaw, string state)
        {
            if (!int.TryParse(bookRaw, out var bookId))
            {
                await DeferAsync();
                return;
            }

            var book = await CurrentBookAsync(bookId);
            if (book is null)
                return;

            var scores = BookRating.Decode(state);

            await UpdateAsync(
                BookRating.Draft(book.Value.title, book.Value.authors, scores),
                BookRating.Selects(bookId, scores));
        }

        [ComponentInteraction("kkr_save:*:*")]
        public async Task SaveAsync(string bookRaw, string state)
        {
            var scores = BookRating.Decode(state);

            if (!int.TryParse(bookRaw, out var bookId) || scores.Any(score => score is < 1 or > 10))
            {
                await DeferAsync();
                return;
            }

            var book = await CurrentBookAsync(bookId);
            if (book is null)
                return;

            var userId = Context.User.Id.ToString();
            var final = BookRating.Final(scores);

            await SaveGate.WaitAsync();
            try
            {
                if (await DataBase.UserHasRated(userId, bookId))
                {
                    await UpdateAsync(await EmbedHandler.CreateErrorEmbed("книжный клуб", "эта книга уже получала твою оценку"), null);
                    return;
                }

                await DataBase.SaveRating(userId, bookId, scores, final);
            }
            finally
            {
                SaveGate.Release();
            }

            BookRating.TriggerExport(Context.Guild as SocketGuild);

            await UpdateAsync(
                EmbedHandler.Build(new EmbedSpec
                {
                    Description = $"✅ Оценка **{final}** сохранена",
                    Color = Color.Green,
                    Footer = BooksClubCommands.Footer
                }),
                null);

            await Context.Channel.SendMessageAsync(embed: BookRating.Result(
                book.Value.title, book.Value.authors, Context.User.Username, scores, final));
        }

        private async Task<(int id, string title, string authors, string image, DateTime selectedDate, string suggestedBy)?> CurrentBookAsync(int bookId)
        {
            var book = await DataBase.GetLastBook();

            if (book.id == bookId)
                return book;

            await UpdateAsync(await EmbedHandler.CreateErrorEmbed("книжный клуб", "книга недели уже сменилась, открой оценку заново"), null);
            return null;
        }

        private Task UpdateAsync(Embed embed, MessageComponent components) =>
            ((SocketMessageComponent)Context.Interaction).UpdateAsync(message =>
            {
                message.Embed = embed;
                message.Components = components ?? new ComponentBuilder().Build();
            });

        private async Task FailAsync(string reason) =>
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("книжный клуб", reason), ephemeral: true);
    }
}
