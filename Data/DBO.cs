using Discord;
using Discord.WebSocket;
using MySqlConnector;
using static sblngavnav5X.Data.DataRoots;
using static sblngavnav5X.Data.DataRoots.States;

namespace sblngavnav5X.Data
{
    public static class DataBase
    {
        public static readonly Dictionary<string, string> MemeCategories = new()
        {
            ["volk"] = "wolfs",
            ["yaica"] = "quotes",
            ["pat"] = "pat",
            ["fff"] = "pressf",
            ["hug"] = "hug",
            ["kiss"] = "kiss",
            ["kus"] = "bite",
            ["buhat"] = "drunk",
            ["ebalo"] = "stfu"
        };

        public static bool CanConnect()
        {
            try
            {
                using var conn = new MySqlConnection(Utils.connectionString);
                conn.Open();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string GetRandomMeme(string columnName)
        {
            if (!MemeCategories.TryGetValue(columnName, out var category))
                throw new ArgumentException($"Недопустимая категория мемов: {columnName}", nameof(columnName));

            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();

            using var cmd = new MySqlCommand("SELECT url FROM memes WHERE category = @c ORDER BY RAND() LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@c", category);
            var result = cmd.ExecuteScalar()?.ToString()?.Trim();
            return string.IsNullOrEmpty(result) ? null : result;
        }

        public static void DownloadStreamers()
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();

            using var cmd = new MySqlCommand("SELECT twitch_id, login FROM streamers ORDER BY login", conn);
            using var reader = cmd.ExecuteReader();

            var logins = new List<string>();
            var ids = new List<string>();
            while (reader.Read())
            {
                var login = reader.GetString("login");
                logins.Add(login);
                ids.Add($"{login}:{reader.GetString("twitch_id")}");
            }

            lock (Streamers)
            {
                Streamers.Clear();
                Streamers.AddRange(logins);
                StreamerIds.Clear();
                StreamerIds.AddRange(ids);
            }
        }

        public static void AddStreamer(string name, string id)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();

            using var cmd = new MySqlCommand(
                @"INSERT INTO streamers (twitch_id, login) VALUES (@id, @login)
                  ON DUPLICATE KEY UPDATE login = @login", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@login", name);
            cmd.ExecuteNonQuery();
        }

        public static void DeleteStreamer(string name)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();

            using var cmd = new MySqlCommand("DELETE FROM streamers WHERE login = @login", conn);
            cmd.Parameters.AddWithValue("@login", name);
            cmd.ExecuteNonQuery();
        }

        public static void AddStatus(string text, string pos, string linkStr, string type)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql =
                @"INSERT INTO statusbar (StatusText, StatusPos, StatusLink, StatusType)
                VALUES (@text, @pos, @link, @type)";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@text", text ?? "");
            cmd.Parameters.AddWithValue("@pos", pos ?? "");
            cmd.Parameters.AddWithValue("@link", linkStr ?? "");
            cmd.Parameters.AddWithValue("@type", type ?? "");
            cmd.ExecuteNonQuery();
        }

        public static (string text, string pos, string link, string type) GetLastStatus()
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(
                "SELECT StatusText, StatusPos, StatusLink, StatusType FROM statusbar ORDER BY id DESC LIMIT 1", conn);
            using var r = cmd.ExecuteReader();
            if (!r.Read())
                return ("", "", "", "");

            return (
                r.IsDBNull(0) ? "" : r.GetString(0),
                r.IsDBNull(1) ? "" : r.GetString(1),
                r.IsDBNull(2) ? "" : r.GetString(2),
                r.IsDBNull(3) ? "" : r.GetString(3));
        }

        public static List<string> GetAllEmotes()
        {
            var list = new List<string>();
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            using var cmd = new MySqlCommand("SELECT raw_line FROM emotes", conn);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                var line = rdr.GetString(0)?.Trim();
                if (!string.IsNullOrEmpty(line))
                    list.Add(line);
            }
            return list;
        }

        public static string GetRandomEmote()
        {
            var all = GetAllEmotes();
            if (all.Count == 0)
                return null;
            return all.RandomList();
        }

        public static async Task ApplyLastStatusAsync(DiscordSocketClient client)
        {
            var (stText, stPos, stLink, stType) = GetLastStatus();

            var userStatus = stPos switch
            {
                "днд" => UserStatus.DoNotDisturb,
                "спит" => UserStatus.Idle,
                "инвиз" => UserStatus.Invisible,
                "онлайн" => UserStatus.Online,
                _ => UserStatus.Online
            };
            await client.SetStatusAsync(userStatus);

            var activityType = stType switch
            {
                "Streaming" => ActivityType.Streaming,
                "Playing" => ActivityType.Playing,
                "Watching" => ActivityType.Watching,
                "Listening" => ActivityType.Listening,
                "Competing" => ActivityType.Competing,
                _ => ActivityType.Playing
            };
            string linkForActivity = activityType == ActivityType.Streaming ? stLink : null;
            await client.SetGameAsync(string.IsNullOrWhiteSpace(stText) ? null : stText, linkForActivity, activityType);
        }

        public static void AddBook(string title, string authors, string imageUrl, string user)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql =
                @"INSERT INTO books (title, authors, image, selected_date, suggested_by, season)
                VALUES (@t, @a, @i, @d, @u, @s)";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@t", title);
            cmd.Parameters.AddWithValue("@a", authors);
            cmd.Parameters.AddWithValue("@i", imageUrl);
            cmd.Parameters.AddWithValue("@d", DateTime.UtcNow.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@u", user);
            cmd.Parameters.AddWithValue("@s", Utils.booksSeason);
            cmd.ExecuteNonQuery();
        }

        public static (int id, string title, string authors, string image, DateTime selectedDate, string suggestedBy) GetLastBook()
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql = @"SELECT id, title, authors, image, selected_date, suggested_by FROM books ORDER BY selected_date DESC LIMIT 1";
            using var cmd = new MySqlCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                return (
                    r.GetInt32("id"),
                    r.GetString("title"),
                    r.GetString("authors"),
                    r.GetString("image"),
                    r.GetDateTime("selected_date"),
                    r.GetString("suggested_by")
                );
            }
            return (0, string.Empty, string.Empty, string.Empty, DateTime.MinValue, string.Empty);
        }

        public static bool CanSelectNewBook()
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql = "SELECT MAX(selected_date) FROM books";
            using var cmd = new MySqlCommand(sql, conn);
            var result = cmd.ExecuteScalar();
            if (result == DBNull.Value || result == null)
                return true;
            var last = Convert.ToDateTime(result);
            return (DateTime.UtcNow - last).TotalDays >= 7;
        }

        public static void RemoveLastBook()
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql = @"DELETE FROM books WHERE id = (SELECT id FROM books ORDER BY selected_date DESC LIMIT 1)";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        public static bool BookHasRatings(int bookId)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            using var cmd = new MySqlCommand("SELECT COUNT(*) FROM booksRating WHERE book_id = @b", conn);
            cmd.Parameters.AddWithValue("@b", bookId);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public static bool TryParseScores(string[] input, out int[] scores)
        {
            scores = new int[5];
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    scores[i] = int.Parse(input[i]);
                    if (scores[i] < 1 || scores[i] > 10)
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void SaveRating(string userId, int bookId, int[] s, double final)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql =
                @"INSERT INTO booksRating
                  (user_id, book_id, score_plot, score_style, score_characters, score_originality, score_vibe, final_score, rated_at)
                VALUES
                  (@u, @b, @s1, @s2, @s3, @s4, @s5, @f, @d)";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@b", bookId);
            cmd.Parameters.AddWithValue("@s1", s[0]);
            cmd.Parameters.AddWithValue("@s2", s[1]);
            cmd.Parameters.AddWithValue("@s3", s[2]);
            cmd.Parameters.AddWithValue("@s4", s[3]);
            cmd.Parameters.AddWithValue("@s5", s[4]);
            cmd.Parameters.AddWithValue("@f", final);
            cmd.Parameters.AddWithValue("@d", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        public static bool UserHasRated(string userId, int bookId)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql = "SELECT COUNT(*) FROM booksRating WHERE user_id = @u AND book_id = @b";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@b", bookId);
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            return count > 0;
        }

        public static List<BookWithRating> GetBooksWithRatings(int? season)
        {
            var list = new List<BookWithRating>();
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();

            string sql =
                @"SELECT b.id,
            b.title,
            b.authors,
            b.suggested_by,
            b.image,
            COALESCE(r.avg_score, 0) AS avg_score,
            COALESCE(r.votes, 0)     AS votes
          FROM books b
          LEFT JOIN (
              SELECT book_id,
                     ROUND(AVG(final_score), 1) AS avg_score,
                     COUNT(*)                   AS votes
              FROM booksRating
              GROUP BY book_id
          ) r ON r.book_id = b.id
          /**where**/
          ORDER BY avg_score DESC, votes DESC, b.id ASC";

            using var cmd = new MySqlCommand(
                sql.Replace("/**where**/", season.HasValue ? "WHERE b.season = @season" : string.Empty),
                conn
            );

            if (season.HasValue)
                cmd.Parameters.AddWithValue("@season", season.Value);

            using var reader = cmd.ExecuteReader();

            int idIdx = reader.GetOrdinal("id");
            int titleIdx = reader.GetOrdinal("title");
            int authorsIdx = reader.GetOrdinal("authors");
            int suggestedIdx = reader.GetOrdinal("suggested_by");
            int imageIdx = reader.GetOrdinal("image");
            int avgScoreIdx = reader.GetOrdinal("avg_score");
            int votesIdx = reader.GetOrdinal("votes");

            while (reader.Read())
            {
                list.Add(new BookWithRating
                {
                    Id = reader.GetInt32(idIdx),
                    Title = reader.GetString(titleIdx),
                    Authors = reader.GetString(authorsIdx),
                    SuggestedBy = reader.GetString(suggestedIdx),
                    Image = reader.IsDBNull(imageIdx) ? "" : reader.GetString(imageIdx),
                    AvgScore = reader.IsDBNull(avgScoreIdx) ? 0.0 : reader.GetDouble(avgScoreIdx),
                    Votes = reader.IsDBNull(votesIdx) ? 0 : reader.GetInt32(votesIdx)
                });
            }

            return list;
        }

        public static int GetMaxSeason()
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql = "SELECT COALESCE(MAX(season), 0) FROM books";
            using var cmd = new MySqlCommand(sql, conn);
            var obj = cmd.ExecuteScalar();
            return (obj == null || obj == DBNull.Value) ? 0 : Convert.ToInt32(obj);
        }

        public static Dictionary<int, string> GetBookSuggesters()
        {
            var dict = new Dictionary<int, string>();
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql = "SELECT id, suggested_by FROM books";
            using var cmd = new MySqlCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                dict[r.GetInt32("id")] = r.GetString("suggested_by");
            return dict;
        }

        public static List<string> LoadStreamsOnline()
        {
            var list = new List<string>();
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(
                "SELECT twitch_id FROM streamers WHERE is_online = 1", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(r.GetString("twitch_id"));
            return list;
        }

        public static void AddStreamOnline(string streamId)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(
                "UPDATE streamers SET is_online = 1 WHERE twitch_id = @id", conn);
            cmd.Parameters.AddWithValue("@id", streamId);
            cmd.ExecuteNonQuery();
        }

        public static void RemoveStreamOnline(string streamId)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(
                "UPDATE streamers SET is_online = 0 WHERE twitch_id = @id", conn);
            cmd.Parameters.AddWithValue("@id", streamId);
            cmd.ExecuteNonQuery();
        }

        public static List<RatingEntry> GetAllRatings()
        {
            var list = new List<RatingEntry>();
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql =
                @"SELECT user_id, book_id,
                       score_plot, score_style, score_characters,
                       score_originality, score_vibe, final_score
                FROM booksRating";

            using var cmd = new MySqlCommand(sql, conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new RatingEntry
                {
                    UserId = r.GetString("user_id"),
                    BookId = r.GetInt32("book_id"),
                    Scores = new[]
                    {
                        r.GetInt32("score_plot"),
                        r.GetInt32("score_style"),
                        r.GetInt32("score_characters"),
                        r.GetInt32("score_originality"),
                        r.GetInt32("score_vibe")
                    },
                    FinalScore = r.GetDouble("final_score")
                });
            }
            return list;
        }
    }
}
