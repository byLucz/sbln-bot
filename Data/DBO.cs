using Discord;
using Discord.WebSocket;
using MySqlConnector;
using System.Text.Json;
using static sblngavnav5X.Data.DataRoots;
using static sblngavnav5X.Data.DataRoots.States;

namespace sblngavnav5X.Data
{
    public static class DataBase
    {
        public static string GetRandomMeme(string columnName)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();

            var allowed = new HashSet<string> { "volk", "yaica", "pat", "fff", "hug", "kiss", "kus", "buhat", "ebalo" };
            if (!allowed.Contains(columnName))
                throw new ArgumentException($"Недопустимая категория мемов: {columnName}", nameof(columnName));

            string sql = $"SELECT `{columnName}` FROM memes LIMIT 1";
            using var cmd = new MySqlCommand(sql, conn);
            var result = cmd.ExecuteScalar()?.ToString();
            if (string.IsNullOrEmpty(result))
                return null;

            var items = result
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            if (items.Length == 0)
                return null;

            var rnd = new Random();
            return items[rnd.Next(items.Length)];
        }

        public static void DownloadStreamers()
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();

            const string sqlStrims = "SELECT strimaki FROM streamers";
            using (var cmd = new MySqlCommand(sqlStrims, conn))
            using (var reader = cmd.ExecuteReader())
            {
                States.Streamers.Clear();
                while (reader.Read())
                    Streamers.Add(reader.GetString("strimaki"));
            }

            const string sqlIds = "SELECT puk FROM streamersid";
            using (var cmd = new MySqlCommand(sqlIds, conn))
            using (var reader = cmd.ExecuteReader())
            {
                StreamerIds.Clear();
                while (reader.Read())
                    StreamerIds.Add(reader.GetString("puk"));
            }
        }

        public static void AddStreamer(string name, string id)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            var cmd1 = new MySqlCommand("INSERT INTO streamers (strimaki) VALUES (@name)", conn, tx);
            cmd1.Parameters.AddWithValue("@name", name);
            cmd1.ExecuteNonQuery();

            var cmd2 = new MySqlCommand("INSERT INTO streamersid (puk) VALUES (@id)", conn, tx);
            cmd2.Parameters.AddWithValue("@id", id);
            cmd2.ExecuteNonQuery();

            tx.Commit();
        }

        public static void DeleteStreamer(string name, string id)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            var cmd1 = new MySqlCommand("DELETE FROM streamers WHERE strimaki = @name", conn, tx);
            cmd1.Parameters.AddWithValue("@name", name);
            cmd1.ExecuteNonQuery();

            var cmd2 = new MySqlCommand("DELETE FROM streamersid WHERE puk = @id", conn, tx);
            cmd2.Parameters.AddWithValue("@id", id);
            cmd2.ExecuteNonQuery();

            tx.Commit();
        }

        public static void AddStatus(string text, string pos, string linkStr, string type)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql = 
                @"INSERT INTO statusbar (StatusText, StatusPos, StatusLink, StatusType)
                VALUES (@text, @pos, @link, @type)";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@text", text);
            cmd.Parameters.AddWithValue("@pos", pos);
            cmd.Parameters.AddWithValue("@link", linkStr);
            cmd.Parameters.AddWithValue("@type", type);
            cmd.ExecuteNonQuery();
        }

        public static void PushStatus()
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql = "SELECT StatusText, StatusPos, StatusLink, StatusType FROM statusbar";
            using var cmd = new MySqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            StatusText.Clear();
            StatusPos.Clear();
            StatusLink.Clear();
            StatusType.Clear();

            while (reader.Read())
            {
                StatusText.Add(reader.GetString("StatusText"));
                StatusPos.Add(reader.GetString("StatusPos"));
                StatusLink.Add(reader.GetString("StatusLink"));
                StatusType.Add(reader.GetString("StatusType"));
            }
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
            var rnd = new Random();
            return all[rnd.Next(all.Count)];
        }

        public static async Task ApplyLastStatusAsync(DiscordSocketClient client)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            await conn.OpenAsync();

            const string sql = 
                @"SELECT StatusText, StatusPos, StatusLink, StatusType
                FROM statusbar
                ORDER BY id DESC
                LIMIT 1";

            using var cmd = new MySqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return;

            var stText = reader.GetString("StatusText");
            var stPos = reader.GetString("StatusPos");
            var stLink = reader.GetString("StatusLink");
            var stType = reader.GetString("StatusType");

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
            await client.SetGameAsync(stText, linkForActivity, activityType);
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
            var last = DateTime.Parse(result.ToString());
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

        public static List<VersionEntry> GetAllVersions()
        {
            var list = new List<VersionEntry>();

            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();

            const string sql = "SELECT `version`, `date` FROM `versions` ORDER BY `id`";
            using var cmd = new MySqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                list.Add(new VersionEntry
                {
                    Version = reader.GetString("version"),
                    Date = reader.GetDateTime("date")
                });
            }

            return list;
        }

        public static List<PackageVersionEntry> GetAllPackageVersions()
        {
            var list = new List<PackageVersionEntry>();

            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();

            const string sql = @"SELECT package_name, package_version, created_at
                                FROM packageVersions
                                ORDER BY id";

            using var cmd = new MySqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                list.Add(new PackageVersionEntry
                {
                    PackageName = reader.GetString("package_name"),
                    PackageVersion = reader.GetString("package_version"),
                    CreatedAt = reader.GetDateTime("created_at")
                });
            }

            return list;
        }

        private static readonly object _exportLock = new();

        public static void ExportBooksJson(string path, Dictionary<string, string> userNames)
        {
            var books = new Dictionary<int, BookExportDto>();

            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql = @"
                SELECT b.id, b.title, b.authors, b.suggested_by, b.season,
                       r.user_id, r.score_plot, r.score_style, r.score_characters,
                       r.score_originality, r.score_vibe, r.final_score
                FROM books b
                LEFT JOIN booksRating r ON r.book_id = b.id
                ORDER BY b.id";

            using var cmd = new MySqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                int id = reader.GetInt32("id");
                if (!books.TryGetValue(id, out var book))
                {
                    book = new BookExportDto
                    {
                        id         = id,
                        title      = reader.GetString("title"),
                        authors    = reader.GetString("authors"),
                        suggestedBy = reader.GetString("suggested_by"),
                        season     = reader.GetInt32("season")
                    };
                    books[id] = book;
                }

                if (!reader.IsDBNull(reader.GetOrdinal("user_id")))
                {
                    string uid  = reader.GetString("user_id");
                    string name = userNames.TryGetValue(uid, out var n) ? n : uid;
                    book.ratings[name] = new RatingExportDto
                    {
                        scores = new[]
                        {
                            reader.GetInt32("score_plot"),
                            reader.GetInt32("score_style"),
                            reader.GetInt32("score_characters"),
                            reader.GetInt32("score_originality"),
                            reader.GetInt32("score_vibe")
                        },
                        final = Math.Round(reader.GetDouble("final_score"), 1)
                    };
                }
            }

            string json = JsonSerializer.Serialize(books.Values.ToList(), AppJsonContext.Default.ListBookExportDto);

            lock (_exportLock)
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
            }
        }

        public static List<string> LoadStreamsOnline()
        {
            var list = new List<string>();
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(
                "SELECT SUBSTRING_INDEX(puk, ':', -1) AS sid FROM streamersid WHERE is_online = 1", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(r.GetString("sid"));
            return list;
        }

        public static void AddStreamOnline(string streamId)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(
                "UPDATE streamersid SET is_online = 1 WHERE SUBSTRING_INDEX(puk, ':', -1) = @id", conn);
            cmd.Parameters.AddWithValue("@id", streamId);
            cmd.ExecuteNonQuery();
        }

        public static void RemoveStreamOnline(string streamId)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(
                "UPDATE streamersid SET is_online = 0 WHERE SUBSTRING_INDEX(puk, ':', -1) = @id", conn);
            cmd.Parameters.AddWithValue("@id", streamId);
            cmd.ExecuteNonQuery();
        }

        public static int InsertPpmMailbox(string email, string password, string ownerId, DateTime? expiresAt, bool isPermanent)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql =
                @"INSERT INTO temp_mailboxes (email, password, owner_id, created_at, expires_at, is_permanent, deleted)
                  VALUES (@e, @p, @o, @c, @x, @perm, 0);
                  SELECT LAST_INSERT_ID();";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@e", email);
            cmd.Parameters.AddWithValue("@p", password);
            cmd.Parameters.AddWithValue("@o", ownerId);
            cmd.Parameters.AddWithValue("@c", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@x", (object)expiresAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@perm", isPermanent ? 1 : 0);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public static List<(int id, string email)> GetExpiredPpmMailboxes()
        {
            var list = new List<(int, string)>();
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql =
                @"SELECT id, email FROM temp_mailboxes
                  WHERE deleted = 0 AND is_permanent = 0
                        AND expires_at IS NOT NULL AND expires_at <= @now";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add((r.GetInt32("id"), r.GetString("email")));
            return list;
        }

        public static void MarkPpmMailboxDeleted(int id)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            using var cmd = new MySqlCommand("UPDATE temp_mailboxes SET deleted = 1 WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public static PpmMailbox GetActivePpmMailbox(string email)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql =
                @"SELECT id, email, password, owner_id, created_at, expires_at, is_permanent
                  FROM temp_mailboxes WHERE email = @e AND deleted = 0 LIMIT 1";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@e", email);
            using var r = cmd.ExecuteReader();
            if (!r.Read())
                return null;
            return ReadPpmMailbox(r);
        }

        public static PpmMailbox GetPpmMailboxById(int id)
        {
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql =
                @"SELECT id, email, password, owner_id, created_at, expires_at, is_permanent
                  FROM temp_mailboxes WHERE id = @id AND deleted = 0 LIMIT 1";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read())
                return null;
            return ReadPpmMailbox(r);
        }

        public static List<PpmMailbox> GetUserPpmMailboxes(string ownerId)
        {
            var list = new List<PpmMailbox>();
            using var conn = new MySqlConnection(Utils.connectionString);
            conn.Open();
            const string sql =
                @"SELECT id, email, password, owner_id, created_at, expires_at, is_permanent
                  FROM temp_mailboxes WHERE owner_id = @o AND deleted = 0 ORDER BY id DESC";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@o", ownerId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(ReadPpmMailbox(r));
            return list;
        }

        private static PpmMailbox ReadPpmMailbox(MySqlDataReader r) => new PpmMailbox
        {
            Id = r.GetInt32("id"),
            Email = r.GetString("email"),
            Password = r.GetString("password"),
            OwnerId = r.GetString("owner_id"),
            CreatedAt = r.GetDateTime("created_at"),
            ExpiresAt = r.IsDBNull(r.GetOrdinal("expires_at")) ? null : r.GetDateTime("expires_at"),
            IsPermanent = r.GetBoolean("is_permanent")
        };

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
