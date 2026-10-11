using MySqlConnector;
using sblngavnav6.Data;
using sblngavnav6.Services;

namespace sblngavnav6.GVR
{
    public sealed record GVRImage(long Id, ulong GuildId, ulong ChannelId, ulong MessageId, ulong ItemId, bool IsEmbed);

    public sealed class GVRDb
    {
        private const string LogSource = "GOVOR";
        private const int CommandTimeoutSeconds = 30;
        private const int BatchSize = 500;

        private int _warned;
        private long _revision;

        public long Revision => Volatile.Read(ref _revision);

        public async Task<bool> LoadSettingsAsync(GVRConfig config, CancellationToken cancellationToken = default)
        {
            if (!await ReadyAsync().ConfigureAwait(false))
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
            if (!await ReadyAsync().ConfigureAwait(false))
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

            if (!await ReadyAsync().ConfigureAwait(false))
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
            if (!await ReadyAsync().ConfigureAwait(false))
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
            if (contents.Count == 0 || !await ReadyAsync().ConfigureAwait(false))
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

            if (added > 0)
                Interlocked.Increment(ref _revision);

            return added;
        }

        public async Task<int> AddImagesAsync(IReadOnlyCollection<GVRImage> images, CancellationToken cancellationToken = default)
        {
            if (images.Count == 0 || !await ReadyAsync().ConfigureAwait(false))
                return 0;

            var added = 0;

            try
            {
                await using var conn = await ConnectAsync(cancellationToken).ConfigureAwait(false);

                foreach (var batch in images.Chunk(BatchSize))
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandTimeout = CommandTimeoutSeconds;

                    var values = new List<string>(batch.Length);

                    for (var index = 0; index < batch.Length; index++)
                    {
                        values.Add($"(@g{index}, @c{index}, @m{index}, @i{index}, @e{index})");
                        cmd.Parameters.AddWithValue($"@g{index}", batch[index].GuildId);
                        cmd.Parameters.AddWithValue($"@c{index}", batch[index].ChannelId);
                        cmd.Parameters.AddWithValue($"@m{index}", batch[index].MessageId);
                        cmd.Parameters.AddWithValue($"@i{index}", batch[index].ItemId);
                        cmd.Parameters.AddWithValue($"@e{index}", batch[index].IsEmbed ? 1 : 0);
                    }

                    cmd.CommandText = $"INSERT IGNORE INTO images (guild_id, channel_id, message_id, item_id, is_embed) VALUES {string.Join(", ", values)}";

                    added += await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (MySqlException ex)
            {
                await LoggingService.LogWarningAsync(LogSource, $"Картинки не сохранены: {ex.Message}");
            }

            return added;
        }

        public async Task<GVRImage> RandomImageAsync(CancellationToken cancellationToken = default)
        {
            if (!await ReadyAsync().ConfigureAwait(false))
                return null;

            try
            {
                await using var conn = await ConnectAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = Command(conn, "SELECT id, guild_id, channel_id, message_id, item_id, is_embed FROM images ORDER BY RAND() LIMIT 1");
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    return null;

                return new GVRImage(reader.GetInt64(0), reader.GetUInt64(1), reader.GetUInt64(2), reader.GetUInt64(3), reader.GetUInt64(4), reader.GetBoolean(5));
            }
            catch (MySqlException ex)
            {
                await LoggingService.LogWarningAsync(LogSource, $"Картинка не загружена: {ex.Message}");
                return null;
            }
        }

        public async Task RemoveImageAsync(long id, CancellationToken cancellationToken = default)
        {
            if (!await ReadyAsync().ConfigureAwait(false))
                return;

            await using var conn = await ConnectAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = Command(conn, "DELETE FROM images WHERE id = @id");
            cmd.Parameters.AddWithValue("@id", id);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> PruneAsync(CancellationToken cancellationToken = default)
        {
            if (!await ReadyAsync().ConfigureAwait(false))
                return 0;

            await using var conn = await ConnectAsync(cancellationToken).ConfigureAwait(false);
            int removed;

            await using (var cmd = Command(conn, """
                DELETE FROM messages
                WHERE TRIM(content) = ''
                   OR content LIKE '%https://%'
                   OR content LIKE CONCAT(@p1, '%')
                   OR content LIKE CONCAT(@p2, '%')
                """))
            {
                cmd.Parameters.AddWithValue("@p1", Global.Vars.Cfg.pref1);
                cmd.Parameters.AddWithValue("@p2", Global.Vars.Cfg.pref2);
                removed = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var drop = new List<ulong>();
            var fix = new List<(ulong Id, string Content)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using (var cmd = Command(conn, "SELECT id, content FROM messages ORDER BY id"))
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var id = reader.GetUInt64(0);
                    var content = reader.GetString(1);
                    var clean = GVRText.Sanitize(content);

                    if (clean is null || !seen.Add(clean))
                        drop.Add(id);
                    else if (clean != content)
                        fix.Add((id, clean));
                }
            }

            foreach (var batch in drop.Chunk(BatchSize))
            {
                await using var cmd = Command(conn, $"DELETE FROM messages WHERE id IN ({string.Join(", ", batch)})");
                removed += await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var (id, content) in fix)
            {
                await using var cmd = Command(conn, "UPDATE IGNORE messages SET content = @c WHERE id = @id");
                cmd.Parameters.AddWithValue("@c", Trim(content));
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            if (removed > 0 || fix.Count > 0)
                Interlocked.Increment(ref _revision);

            return removed;
        }

        private async Task<bool> ReadyAsync()
        {
            if (!string.IsNullOrWhiteSpace(Global.Vars.Cfg.connectionString) && !string.IsNullOrWhiteSpace(Global.Vars.Cfg.gvrBase))
                return true;

            if (Interlocked.Exchange(ref _warned, 1) == 0)
                await LoggingService.LogWarningAsync(LogSource, "не заданы System:DbConnectionString или System:GvrBase, говорилка не работает");

            return false;
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
