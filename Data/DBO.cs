using Discord;
using Discord.WebSocket;
using MySqlConnector;
using sblngavnav6.Common;
using System.Collections.Concurrent;
using System.Text.Json;
using static sblngavnav6.Data.DataRoots;

namespace sblngavnav6.Data
{
    public static class DataBase
    {
        private const int CommandTimeoutSeconds = 15;
        private const int RetryAttempts = 2;

        private static readonly int[] TransientErrors = [1205, 1213, 1040];
        private static readonly int[] ConnectionErrors = [2006, 2013];

        private static async Task<MySqlConnection> DbAsync(CancellationToken cancellationToken = default)
        {
            var conn = new MySqlConnection(Global.Vars.Cfg.connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            return conn;
        }

        public static async Task<bool> CanConnect()
        {
            try
            {
                await using var conn = await DbAsync().ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static MySqlCommand Command(MySqlConnection conn, string sql, Action<MySqlCommand> bind, MySqlTransaction tx = null)
        {
            var cmd = new MySqlCommand(sql, conn, tx) { CommandTimeout = CommandTimeoutSeconds };
            bind?.Invoke(cmd);
            return cmd;
        }

        private static async Task<T> RetriedAsync<T>(Func<Task<T>> work, bool readOnly = false)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return await work().ConfigureAwait(false);
                }
                catch (MySqlException ex) when (attempt < RetryAttempts &&
                    (TransientErrors.Contains(ex.Number) || (readOnly && ConnectionErrors.Contains(ex.Number))))
                {
                    await Task.Delay(50 * (attempt + 1)).ConfigureAwait(false);
                }
            }
        }

        private static Task<int> ExecAsync(string sql, Action<MySqlCommand> bind = null) => RetriedAsync(async () =>
        {
            await using var conn = await DbAsync().ConfigureAwait(false);
            await using var cmd = Command(conn, sql, bind);
            return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        });

        private static Task<T> ScalarAsync<T>(string sql, Action<MySqlCommand> bind, Func<object, T> map) => RetriedAsync(async () =>
        {
            await using var conn = await DbAsync().ConfigureAwait(false);
            await using var cmd = Command(conn, sql, bind);
            return map(await cmd.ExecuteScalarAsync().ConfigureAwait(false));
        });

        private static Task<List<T>> RowsAsync<T>(string sql, Action<MySqlCommand> bind, Func<MySqlDataReader, T> map) => RetriedAsync(async () =>
        {
            await using var conn = await DbAsync().ConfigureAwait(false);
            await using var cmd = Command(conn, sql, bind);
            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

            var list = new List<T>();
            while (await reader.ReadAsync().ConfigureAwait(false))
                list.Add(map(reader));

            return list;
        }, readOnly: true);

        private static Task<T> RowAsync<T>(string sql, Action<MySqlCommand> bind, Func<MySqlDataReader, T> map, T fallback = default) => RetriedAsync(async () =>
        {
            await using var conn = await DbAsync().ConfigureAwait(false);
            await using var cmd = Command(conn, sql, bind);
            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

            return await reader.ReadAsync().ConfigureAwait(false) ? map(reader) : fallback;
        }, readOnly: true);


        private static readonly ConcurrentDictionary<ulong, GuildSettings> _guildCache = new();

        public static async Task<GuildSettings> GetGuildSettings(ulong guildId)
        {
            if (_guildCache.TryGetValue(guildId, out var cached)) return cached;
            var gs = await LoadGuildSettings(guildId).ConfigureAwait(false) ?? new GuildSettings { GuildId = guildId };
            _guildCache[guildId] = gs;
            return gs;
        }

        private static Task<GuildSettings> LoadGuildSettings(ulong guildId)
        {
            return RowAsync<GuildSettings>(
                "SELECT superuser_role_id, welcome_channel_id, welcome_message, welcome_role_id, stream_notif_channel_id FROM guild_settings WHERE guild_id=@g LIMIT 1",
                cmd => cmd.Parameters.AddWithValue("@g", guildId),
                r => new GuildSettings
            {
                GuildId = guildId,
                SuperuserRoleId = r.IsDBNull(0) ? null : r.GetUInt64(0),
                WelcomeChannelId = r.IsDBNull(1) ? null : r.GetUInt64(1),
                WelcomeMessage = r.IsDBNull(2) ? null : r.GetString(2),
                WelcomeRoleId = r.IsDBNull(3) ? null : r.GetUInt64(3),
                StreamNotifChannelId = r.IsDBNull(4) ? null : r.GetUInt64(4),
            });
        }

        public static Task SetSuperuserRole(ulong guildId, ulong? roleId)
            => UpsertGuild(guildId, "superuser_role_id", roleId, s => s.SuperuserRoleId = roleId);

        public static Task SetWelcomeChannel(ulong guildId, ulong? channelId)
            => UpsertGuild(guildId, "welcome_channel_id", channelId, s => s.WelcomeChannelId = channelId);

        public static Task SetWelcomeRole(ulong guildId, ulong? roleId)
            => UpsertGuild(guildId, "welcome_role_id", roleId, s => s.WelcomeRoleId = roleId);

        public static Task SetWelcomeMessage(ulong guildId, string message)
            => UpsertGuild(guildId, "welcome_message", message, s => s.WelcomeMessage = message);

        public static Task SetStreamNotifChannel(ulong guildId, ulong? channelId)
            => UpsertGuild(guildId, "stream_notif_channel_id", channelId, s => s.StreamNotifChannelId = channelId);

        private static async Task UpsertGuild(ulong guildId, string column, object value, Action<GuildSettings> apply)
        {
            await ExecAsync(
                $"INSERT INTO guild_settings (guild_id, {column}) VALUES (@g, @v) ON DUPLICATE KEY UPDATE {column}=@v",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@g", guildId);
                    cmd.Parameters.AddWithValue("@v", value ?? DBNull.Value);
                });

            var gs = await GetGuildSettings(guildId).ConfigureAwait(false);
            apply(gs);
            _guildCache[guildId] = gs;
        }

        public static readonly string[] MemeCategories =
            ["wolfs", "quotes", "pat", "hug", "kiss", "pressf", "bite", "drunk", "stfu"];

        public static Task<string> GetRandomMeme(string category)
        {
            category = category?.Trim().ToLowerInvariant();

            if (!MemeCategories.Contains(category))
                throw new ArgumentException($"Недопустимая категория мемов: {category}", nameof(category));

            return ScalarAsync(
                "SELECT url FROM memes WHERE category = @c ORDER BY RAND() LIMIT 1",
                cmd => cmd.Parameters.AddWithValue("@c", category),
                value => value?.ToString()?.Trim());
        }

        public static Task<int> AddMeme(string category, string url) =>
            ExecAsync(
                "INSERT IGNORE INTO memes (category, url) VALUES (@c, @u)",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@c", category?.Trim().ToLowerInvariant());
                    cmd.Parameters.AddWithValue("@u", url?.Trim());
                });

        public static Task<int> CountMemes(string category) =>
            ScalarAsync(
                "SELECT COUNT(*) FROM memes WHERE category = @c",
                cmd => cmd.Parameters.AddWithValue("@c", category),
                value => Convert.ToInt32(value));

        public static async Task DownloadStreamers()
        {
            var rows = await RowsAsync(
                "SELECT twitch_id, login FROM streamers ORDER BY login",
                null,
                reader => (Id: reader.GetString("twitch_id"), Login: reader.GetString("login"))).ConfigureAwait(false);

            var streamers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (id, login) in rows)
                streamers[login] = id;

            States.SetStreamers(streamers);
        }

        public static Task AddStreamer(string login, string twitchId) =>
            ExecAsync(
                @"INSERT INTO streamers (twitch_id, login) VALUES (@id, @login)
                  ON DUPLICATE KEY UPDATE login = @login",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@id", twitchId);
                    cmd.Parameters.AddWithValue("@login", login);
                });

        public static Task DeleteStreamer(string twitchId) =>
            ExecAsync(
                "DELETE FROM streamers WHERE twitch_id = @id",
                cmd => cmd.Parameters.AddWithValue("@id", twitchId));

        public static Task AddStatus(string text, string pos, string linkStr, string type)
        {
            return ExecAsync(
                @"INSERT INTO statusbar (StatusText, StatusPos, StatusLink, StatusType)
                VALUES (@text, @pos, @link, @type)",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@text", text);
                    cmd.Parameters.AddWithValue("@pos", pos);
                    cmd.Parameters.AddWithValue("@link", linkStr);
                    cmd.Parameters.AddWithValue("@type", type);
                });
        }

        public static async Task<List<string>> GetAllEmotes()
        {
            var lines = await RowsAsync("SELECT raw_line FROM emotes", null, reader => reader.GetString(0)?.Trim()).ConfigureAwait(false);

            return lines.Where(line => !string.IsNullOrEmpty(line)).ToList();
        }

        public static async Task<string> GetRandomEmote()
        {
            var all = await GetAllEmotes().ConfigureAwait(false);
            if (all.Count == 0)
                return null;
            return all.RandomList();
        }

        public static async Task ApplyLastStatusAsync(DiscordSocketClient client)
        {
            using var conn = new MySqlConnection(Global.Vars.Cfg.connectionString);
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

        public static Task AddBook(string title, string authors, string imageUrl, string user)
        {
            return ExecAsync(
                @"INSERT INTO books (title, authors, image, selected_date, suggested_by, season)
                VALUES (@t, @a, @i, @d, @u, @s)",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@t", title);
                    cmd.Parameters.AddWithValue("@a", authors);
                    cmd.Parameters.AddWithValue("@i", imageUrl);
                    cmd.Parameters.AddWithValue("@d", DateTime.UtcNow.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@u", user);
                    cmd.Parameters.AddWithValue("@s", Global.Vars.Cfg.booksSeason);
                });
        }

        public static Task<(int id, string title, string authors, string image, DateTime selectedDate, string suggestedBy)> GetLastBook()
        {
            return RowAsync(
                @"SELECT id, title, authors, image, selected_date, suggested_by FROM books ORDER BY selected_date DESC LIMIT 1",
                null,
                r => (
                    r.GetInt32("id"),
                    r.GetString("title"),
                    r.GetString("authors"),
                    r.GetString("image"),
                    r.GetDateTime("selected_date"),
                    r.GetString("suggested_by")),
                (0, string.Empty, string.Empty, string.Empty, DateTime.MinValue, string.Empty));
        }

        public static async Task<bool> CanSelectNewBook()
        {
            var last = await ScalarAsync(
                "SELECT MAX(selected_date) FROM books",
                null,
                value => value is null || value == DBNull.Value ? (DateTime?)null : DateTime.Parse(value.ToString())).ConfigureAwait(false);

            return last is null || (DateTime.UtcNow - last.Value).TotalDays >= 7;
        }

        public static Task RemoveLastBook()
        {
            return ExecAsync(@"DELETE FROM books WHERE id = (SELECT id FROM books ORDER BY selected_date DESC LIMIT 1)");
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

        public static Task SaveRating(string userId, int bookId, int[] s, double final)
        {
            return ExecAsync(
                @"INSERT INTO booksRating 
                  (user_id, book_id, score_plot, score_style, score_characters, score_originality, score_vibe, final_score, rated_at)
                VALUES
                  (@u, @b, @s1, @s2, @s3, @s4, @s5, @f, @d)",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@u", userId);
                    cmd.Parameters.AddWithValue("@b", bookId);
                    cmd.Parameters.AddWithValue("@s1", s[0]);
                    cmd.Parameters.AddWithValue("@s2", s[1]);
                    cmd.Parameters.AddWithValue("@s3", s[2]);
                    cmd.Parameters.AddWithValue("@s4", s[3]);
                    cmd.Parameters.AddWithValue("@s5", s[4]);
                    cmd.Parameters.AddWithValue("@f", final);
                    cmd.Parameters.AddWithValue("@d", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                });
        }

        public static Task<bool> UserHasRated(string userId, int bookId)
        {
            return ScalarAsync(
                "SELECT COUNT(*) FROM booksRating WHERE user_id = @u AND book_id = @b",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@u", userId);
                    cmd.Parameters.AddWithValue("@b", bookId);
                },
                value => Convert.ToInt32(value) > 0);
        }

        public static Task<bool> BookHasRatings(int bookId)
        {
            return ScalarAsync(
                "SELECT COUNT(*) FROM booksRating WHERE book_id = @b",
                cmd => cmd.Parameters.AddWithValue("@b", bookId),
                value => Convert.ToInt32(value) > 0);
        }

        public static async Task<List<BookWithRating>> GetBooksWithRatings(int? season)
        {
            var list = new List<BookWithRating>();
            await using var conn = await DbAsync().ConfigureAwait(false);

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

            await using var cmd = Command(
                conn,
                sql.Replace("/**where**/", season.HasValue ? "WHERE b.season = @season" : string.Empty),
                null);

            if (season.HasValue)
                cmd.Parameters.AddWithValue("@season", season.Value);

            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

            int idIdx = reader.GetOrdinal("id");
            int titleIdx = reader.GetOrdinal("title");
            int authorsIdx = reader.GetOrdinal("authors");
            int suggestedIdx = reader.GetOrdinal("suggested_by");
            int imageIdx = reader.GetOrdinal("image");
            int avgScoreIdx = reader.GetOrdinal("avg_score");
            int votesIdx = reader.GetOrdinal("votes");

            while (await reader.ReadAsync().ConfigureAwait(false))
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

        public static Task<int> GetMaxSeason()
        {
            return ScalarAsync(
                "SELECT COALESCE(MAX(season), 0) FROM books",
                null,
                value => value is null || value == DBNull.Value ? 0 : Convert.ToInt32(value));
        }

        public static async Task<Dictionary<int, string>> GetBookSuggesters()
        {
            var rows = await RowsAsync(
                "SELECT id, suggested_by FROM books",
                null,
                reader => (Id: reader.GetInt32("id"), Suggested: reader.GetString("suggested_by"))).ConfigureAwait(false);

            return rows.ToDictionary(row => row.Id, row => row.Suggested);
        }

        public static Task<List<VersionEntry>> GetAllVersions()
        {
            return RowsAsync(
                "SELECT `version`, `date` FROM `versions` ORDER BY `id`",
                null,
                reader => new VersionEntry
                {
                    Version = reader.GetString("version"),
                    Date = reader.GetDateTime("date")
                });
        }

        public static Task<List<PackageVersionEntry>> GetAllPackageVersions()
        {
            return RowsAsync(
                @"SELECT package_name, package_version, created_at
                  FROM packageVersions
                  ORDER BY id",
                null,
                reader => new PackageVersionEntry
                {
                    PackageName = reader.GetString("package_name"),
                    PackageVersion = reader.GetString("package_version"),
                    CreatedAt = reader.GetDateTime("created_at")
                });
        }

        private static readonly object _exportLock = new();

        public static async Task ExportBooksJson(string path, Dictionary<string, string> userNames)
        {
            var books = new Dictionary<int, BookExportDto>();

            await using var conn = await DbAsync().ConfigureAwait(false);
            const string sql = @"
                SELECT b.id, b.title, b.authors, b.suggested_by, b.season,
                       r.user_id, r.score_plot, r.score_style, r.score_characters,
                       r.score_originality, r.score_vibe, r.final_score
                FROM books b
                LEFT JOIN booksRating r ON r.book_id = b.id
                ORDER BY b.id";

            await using var cmd = Command(conn, sql, null);
            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

            while (await reader.ReadAsync().ConfigureAwait(false))
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

        public static Task<List<string>> LoadStreamsOnline()
        {
            return RowsAsync(
                "SELECT twitch_id FROM streamers WHERE is_online = 1",
                null,
                reader => reader.GetString("twitch_id"));
        }

        public static Task AddStreamOnline(string streamId)
        {
            return ExecAsync(
                "UPDATE streamers SET is_online = 1 WHERE twitch_id = @id",
                cmd => cmd.Parameters.AddWithValue("@id", streamId));
        }

        public static Task RemoveStreamOnline(string streamId)
        {
            return ExecAsync(
                "UPDATE streamers SET is_online = 0 WHERE twitch_id = @id",
                cmd => cmd.Parameters.AddWithValue("@id", streamId));
        }

        public static Task<int> InsertPpmMailbox(string email, string password, string ownerId, DateTime? expiresAt, bool isPermanent)
        {
            return ScalarAsync(
                @"INSERT INTO temp_mailboxes (email, password, owner_id, created_at, expires_at, is_permanent, deleted)
                  VALUES (@e, @p, @o, @c, @x, @perm, 0);
                  SELECT LAST_INSERT_ID();",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@e", email);
                    cmd.Parameters.AddWithValue("@p", password);
                    cmd.Parameters.AddWithValue("@o", ownerId);
                    cmd.Parameters.AddWithValue("@c", DateTime.UtcNow);
                    cmd.Parameters.AddWithValue("@x", (object)expiresAt ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@perm", isPermanent ? 1 : 0);
                },
                value => Convert.ToInt32(value));
        }

        public static Task<List<(int id, string email, string ownerId)>> GetExpiredPpmMailboxes()
        {
            return RowsAsync(
                @"SELECT id, email, owner_id FROM temp_mailboxes
                  WHERE deleted = 0 AND is_permanent = 0
                        AND expires_at IS NOT NULL AND expires_at <= @now",
                cmd => cmd.Parameters.AddWithValue("@now", DateTime.UtcNow),
                reader => (reader.GetInt32("id"), reader.GetString("email"), reader.GetString("owner_id")));
        }

        public static Task<DateTime?> GetNextPpmExpiry() =>
            ScalarAsync(
                @"SELECT MIN(expires_at) FROM temp_mailboxes
                  WHERE deleted = 0 AND is_permanent = 0 AND expires_at IS NOT NULL",
                null,
                value => value is null || value == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(value));

        public static Task MarkPpmMailboxDeleted(int id)
        {
            return ExecAsync(
                "UPDATE temp_mailboxes SET deleted = 1 WHERE id = @id",
                cmd => cmd.Parameters.AddWithValue("@id", id));
        }

        private const string PpmLiveFilter = "deleted = 0 AND (expires_at IS NULL OR expires_at > @now)";

        public static Task<PpmMailbox> GetActivePpmMailbox(string email)
        {
            return RowAsync<PpmMailbox>(
                @"SELECT id, email, password, owner_id, created_at, expires_at, is_permanent
                  FROM temp_mailboxes WHERE email = @e AND " + PpmLiveFilter + " LIMIT 1",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@e", email);
                    cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
                },
                ReadPpmMailbox);
        }

        public static Task<PpmMailbox> GetPpmMailboxById(int id)
        {
            return RowAsync<PpmMailbox>(
                @"SELECT id, email, password, owner_id, created_at, expires_at, is_permanent
                  FROM temp_mailboxes WHERE id = @id AND " + PpmLiveFilter + " LIMIT 1",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
                },
                ReadPpmMailbox);
        }

        public static Task<(int count, DateTime? oldest)> CountPpmTempCreated(string ownerId, DateTime sinceUtc)
        {
            return RowAsync(
                @"SELECT COUNT(*) AS c, MIN(created_at) AS m FROM temp_mailboxes
                  WHERE owner_id = @o AND is_permanent = 0 AND created_at >= @s",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@o", ownerId);
                    cmd.Parameters.AddWithValue("@s", sinceUtc);
                },
                reader => (
                    reader.GetInt32("c"),
                    reader.IsDBNull(reader.GetOrdinal("m")) ? (DateTime?)null : reader.GetDateTime("m")),
                (0, null));
        }

        public static Task<int> CountPpmPermanent(string ownerId)
        {
            return ScalarAsync(
                @"SELECT COUNT(*) FROM temp_mailboxes
                  WHERE owner_id = @o AND is_permanent = 1 AND deleted = 0",
                cmd => cmd.Parameters.AddWithValue("@o", ownerId),
                value => Convert.ToInt32(value));
        }

        public static Task<List<PpmMailbox>> GetUserPpmMailboxes(string ownerId)
        {
            return RowsAsync(
                @"SELECT id, email, password, owner_id, created_at, expires_at, is_permanent
                  FROM temp_mailboxes WHERE owner_id = @o AND " + PpmLiveFilter + " ORDER BY id DESC",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@o", ownerId);
                    cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
                },
                ReadPpmMailbox);
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

        public static Task<List<RatingEntry>> GetAllRatings()
        {
            return RowsAsync(
                @"SELECT user_id, book_id,
                       score_plot, score_style, score_characters,
                       score_originality, score_vibe, final_score
                FROM booksRating",
                null,
                r => new RatingEntry
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
    }
}
