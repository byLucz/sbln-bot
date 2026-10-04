using MySqlConnector;
using sblngavnav6.Data;
using sblngavnav6.Services;

namespace sblngavnav6.GVR
{
    public sealed class GVRDb
    {
        private const string LogSource = "GOVOR";
        private const int CommandTimeoutSeconds = 30;
        private const int BatchSize = 500;

        private int _ready;

        public async Task<bool> EnsureSchemaAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _ready) == 1)
                return true;

            if (string.IsNullOrWhiteSpace(Global.Vars.Cfg.connectionString) || string.IsNullOrWhiteSpace(Global.Vars.Cfg.gvrBase))
            {
                await LoggingService.LogWarningAsync(LogSource, "не заданы System:DbConnectionString или System:GvrBase, говорилка не работает");
                return false;
            }

            try
            {
                var database = Global.Vars.Cfg.gvrBase;
                var builder = new MySqlConnectionStringBuilder(Global.Vars.Cfg.connectionString) { Database = string.Empty };

                await using (var server = new MySqlConnection(builder.ConnectionString))
                {
                    await server.OpenAsync(cancellationToken).ConfigureAwait(false);
                    await using var create = Command(server, $"CREATE DATABASE IF NOT EXISTS `{database}` CHARACTER SET utf8mb4");
                    await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await using var conn = await ConnectAsync(cancellationToken).ConfigureAwait(false);
                await using var table = Command(conn, """
                    CREATE TABLE IF NOT EXISTS messages (
                        id        BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                        guild_id  BIGINT UNSIGNED NOT NULL DEFAULT 0,
                        content   VARCHAR(1024)   NOT NULL,
                        digest    BINARY(16)      AS (UNHEX(MD5(content))) STORED,
                        added_at  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        PRIMARY KEY (id),
                        UNIQUE KEY ux_messages_digest (digest),
                        KEY ix_messages_guild (guild_id)
                    ) ENGINE = InnoDB CHARACTER SET utf8mb4
                    """);

                await table.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                await using var settings = Command(conn, """
                    CREATE TABLE IF NOT EXISTS settings (
                        id            TINYINT UNSIGNED NOT NULL DEFAULT 1,
                        step          INT        NOT NULL DEFAULT 1,
                        word_count    INT        NOT NULL DEFAULT 10,
                        collection    INT        NOT NULL DEFAULT 100,
                        chance        INT        NOT NULL DEFAULT 5,
                        random_words  TINYINT(1) NOT NULL DEFAULT 1,
                        verbal_abuse  TINYINT(1) NOT NULL DEFAULT 0,
                        interval_ms   INT        NOT NULL DEFAULT 86400000,
                        updated_at    DATETIME   NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                        PRIMARY KEY (id)
                    ) ENGINE = InnoDB
                    """);

                await settings.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                await using var seed = Command(conn, "INSERT IGNORE INTO settings (id) VALUES (1)");
                await seed.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                Volatile.Write(ref _ready, 1);
                return true;
            }
            catch (Exception ex)
            {
                await LoggingService.LogErrorAsync(LogSource, "Не удалось подготовить базу говорилки", ex);
                return false;
            }
        }

        public async Task<bool> LoadSettingsAsync(GVRConfig config, CancellationToken cancellationToken = default)
        {
            if (!await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false))
                return false;

            try
            {
                await using var conn = await ConnectAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = Command(conn, "SELECT step, word_count, collection, chance, random_words, verbal_abuse, interval_ms FROM settings WHERE id = 1");
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    return false;

                config.Step = (uint)reader.GetInt32(0);
                config.Count = reader.GetInt32(1);
                config.Collection = (uint)reader.GetInt32(2);
                config.Chance = (uint)reader.GetInt32(3);
                config.Rand = reader.GetBoolean(4);
                config.VerbalAbuseBySheff = reader.GetBoolean(5);
                config.IntervalMs = reader.GetInt32(6);

                return true;
            }
            catch (Exception ex)
            {
                await LoggingService.LogWarningAsync(LogSource, $"Настройки не загружены: {ex.Message}");
                return false;
            }
        }

        public async Task SaveSettingsAsync(GVRConfig config, CancellationToken cancellationToken = default)
        {
            if (!await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false))
                return;

            try
            {
                await using var conn = await ConnectAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = Command(conn, """
                    UPDATE settings
                       SET step = @step, word_count = @count, collection = @collection,
                           chance = @chance, random_words = @rand, verbal_abuse = @abuse, interval_ms = @interval
                     WHERE id = 1
                    """);

                cmd.Parameters.AddWithValue("@step", (int)config.Step);
                cmd.Parameters.AddWithValue("@count", config.Count);
                cmd.Parameters.AddWithValue("@collection", (int)config.Collection);
                cmd.Parameters.AddWithValue("@chance", (int)config.Chance);
                cmd.Parameters.AddWithValue("@rand", config.Rand ? 1 : 0);
                cmd.Parameters.AddWithValue("@abuse", config.VerbalAbuseBySheff ? 1 : 0);
                cmd.Parameters.AddWithValue("@interval", config.IntervalMs);

                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await LoggingService.LogWarningAsync(LogSource, $"Настройки не сохранены: {ex.Message}");
            }
        }

        public async Task<List<string>> LoadAsync(CancellationToken cancellationToken = default)
        {
            var lines = new List<string>();

            if (!await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false))
                return lines;

            await using var conn = await ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = Command(conn, "SELECT content FROM messages");
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                lines.Add(reader.GetString(0));

            return lines;
        }

        public async Task<(int Count, long MaxId)> StampAsync(CancellationToken cancellationToken = default)
        {
            if (!await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false))
                return (0, 0);

            await using var conn = await ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = Command(conn, "SELECT COUNT(*), COALESCE(MAX(id), 0) FROM messages");
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return (0, 0);

            return (Convert.ToInt32(reader.GetValue(0)), Convert.ToInt64(reader.GetValue(1)));
        }

        public async Task<int> AddAsync(ulong guildId, IReadOnlyCollection<string> contents, CancellationToken cancellationToken = default)
        {
            if (contents.Count == 0 || !await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false))
                return 0;

            await using var conn = await ConnectAsync(cancellationToken).ConfigureAwait(false);
            var added = 0;

            foreach (var batch in contents.Chunk(BatchSize))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandTimeout = CommandTimeoutSeconds;

                var values = new List<string>(batch.Length);

                for (var index = 0; index < batch.Length; index++)
                {
                    values.Add($"(@g, @c{index})");
                    cmd.Parameters.AddWithValue($"@c{index}", Trim(batch[index]));
                }

                cmd.Parameters.AddWithValue("@g", guildId);
                cmd.CommandText = $"INSERT IGNORE INTO messages (guild_id, content) VALUES {string.Join(", ", values)}";

                added += await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            return added;
        }

        public async Task<int> ReplaceAsync(ulong guildId, IReadOnlyCollection<string> contents, CancellationToken cancellationToken = default)
        {
            if (!await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false))
                return 0;

            await using (var conn = await ConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                await using var cmd = Command(conn, "TRUNCATE TABLE messages");
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            return await AddAsync(guildId, contents, cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> CleanupAsync(CancellationToken cancellationToken = default)
        {
            if (!await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false))
                return 0;

            await using var conn = await ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = Command(conn, """
                DELETE FROM messages
                WHERE TRIM(content) = ''
                   OR content LIKE '%https://%'
                   OR content LIKE CONCAT(@p1, '%')
                   OR content LIKE CONCAT(@p2, '%')
                """);

            cmd.Parameters.AddWithValue("@p1", Global.Vars.Cfg.pref1);
            cmd.Parameters.AddWithValue("@p2", Global.Vars.Cfg.pref2);

            return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> ImportFileAsync(string path, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return 0;

            var (count, _) = await StampAsync(cancellationToken).ConfigureAwait(false);

            if (count > 0)
                return 0;

            var lines = (await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false))
                .Select(Trim)
                .Where(line => line.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (lines.Length == 0)
                return 0;

            var added = await AddAsync(0, lines, cancellationToken).ConfigureAwait(false);

            await LoggingService.LogInformationAsync(
                LogSource,
                $"Корпус перенесён из {Path.GetFileName(path)}: строк в файле {lines.Length}, добавлено {added}");

            return added;
        }

        private static string Trim(string content)
        {
            content = content?.Trim() ?? string.Empty;

            return content.Length > 1024 ? content[..1024] : content;
        }

        private static async Task<MySqlConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            var builder = new MySqlConnectionStringBuilder(Global.Vars.Cfg.connectionString)
            {
                Database = Global.Vars.Cfg.gvrBase
            };

            var conn = new MySqlConnection(builder.ConnectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            return conn;
        }

        private static MySqlCommand Command(MySqlConnection conn, string sql) =>
            new(sql, conn) { CommandTimeout = CommandTimeoutSeconds };
    }
}
