using sblngavnav5X.Data;
using sblngavnav5X.Services;
using System.Text;

namespace sblngavnav5X.GVR
{
    public static class GVRStore
    {
        private static readonly SemaphoreSlim _lock = new(1, 1);
        private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

        private static List<string> _cache;
        private static (DateTime, long) _stamp;

        public static string FilePath => Utils.messagesFilePath;

        public static string Clean(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var sb = new StringBuilder(content.Length);
            foreach (var ch in content)
            {
                if (ch == '\0' || ch == '\uFEFF') continue;
                sb.Append(ch is '\r' or '\n' or '\t' ? ' ' : ch);
            }

            var line = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return line.Length == 0 ? null : line;
        }

        public static bool Accepts(string content) =>
            !string.IsNullOrWhiteSpace(content) &&
            !content.StartsWith(Utils.pref1, StringComparison.OrdinalIgnoreCase) &&
            !content.StartsWith(Utils.pref2, StringComparison.OrdinalIgnoreCase) &&
            !content.Contains("https://", StringComparison.OrdinalIgnoreCase);

        public static async Task<IReadOnlyList<string>> ReadAsync()
        {
            await _lock.WaitAsync();
            try
            {
                return await LoadAsync();
            }
            finally
            {
                _lock.Release();
            }
        }

        public static async Task<(int added, int total)> AppendAsync(IEnumerable<string> contents)
        {
            await _lock.WaitAsync();
            try
            {
                var lines = new List<string>(await LoadAsync());
                var seen = new HashSet<string>(lines);
                var added = 0;

                foreach (var raw in contents)
                {
                    var line = Clean(raw);
                    if (line != null && Accepts(line) && seen.Add(line))
                    {
                        lines.Add(line);
                        added++;
                    }
                }

                if (added > 0)
                    await SaveAsync(lines);

                return (added, lines.Count);
            }
            finally
            {
                _lock.Release();
            }
        }

        public static async Task<int> ReplaceAsync(IEnumerable<string> contents)
        {
            await _lock.WaitAsync();
            try
            {
                var lines = Normalize(contents);
                await SaveAsync(lines);
                return lines.Count;
            }
            finally
            {
                _lock.Release();
            }
        }

        public static async Task<(int before, int after)> PruneAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(FilePath))
                    return (0, 0);

                var raw = await ReadRawAsync();
                var lines = Normalize(raw);
                await SaveAsync(lines);
                return (raw.Count, lines.Count);
            }
            finally
            {
                _lock.Release();
            }
        }

        private static List<string> Normalize(IEnumerable<string> contents)
        {
            var seen = new HashSet<string>();
            var lines = new List<string>();

            foreach (var raw in contents)
            {
                var line = Clean(raw);
                if (line != null && Accepts(line) && seen.Add(line))
                    lines.Add(line);
            }

            return lines;
        }

        private static async Task<List<string>> LoadAsync()
        {
            if (!File.Exists(FilePath))
                return _cache = new List<string>();

            var info = new FileInfo(FilePath);
            var stamp = (info.LastWriteTimeUtc, info.Length);
            if (_cache != null && stamp == _stamp)
                return _cache;

            var bytes = await File.ReadAllBytesAsync(FilePath);
            var hasNul = Array.IndexOf(bytes, (byte)0) >= 0;
            var damaged = hasNul || Array.IndexOf(bytes, (byte)'\r') >= 0 || bytes.Length < 3 || bytes[0] != 0xEF;
            var lines = Normalize(SplitLines(bytes));

            if (damaged)
            {
                await SaveAsync(lines);
                if (hasNul)
                    await LoggingService.LogWarningAsync("GOVOR", $"{FilePath} был поврежден (NUL-байты), восстановлено строк: {lines.Count}");
            }
            else
            {
                _cache = lines;
                _stamp = stamp;
            }

            return _cache;
        }

        private static async Task<List<string>> ReadRawAsync() =>
            SplitLines(await File.ReadAllBytesAsync(FilePath)).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();

        private static List<string> SplitLines(byte[] bytes) => Encoding.UTF8.GetString(bytes).Split('\n').ToList();

        private static async Task SaveAsync(List<string> lines)
        {
            var path = Path.GetFullPath(FilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var text = lines.Count == 0 ? string.Empty : string.Join('\n', lines) + "\n";
            var tmp = path + ".tmp";

            await File.WriteAllTextAsync(tmp, text, Utf8Bom);
            try
            {
                File.Move(tmp, path, overwrite: true);
            }
            catch (IOException)
            {
                await File.WriteAllTextAsync(path, text, Utf8Bom);
                File.Delete(tmp);
            }

            var info = new FileInfo(path);
            _cache = lines;
            _stamp = (info.LastWriteTimeUtc, info.Length);
        }
    }
}
