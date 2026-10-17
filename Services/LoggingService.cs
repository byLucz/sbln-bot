using Discord;
using Discord.WebSocket;
using sblngavnav6.Data;
using System.Net.WebSockets;
using System.Security;
using System.Text;

namespace sblngavnav6.Services
{
    public static class LoggingService
    {
        private const int LogRetentionDays = 14;

        private static readonly SemaphoreSlim _sync = new(1, 1);
        private static readonly Encoding FileEncoding = new UTF8Encoding(false);
        private static readonly string _logsDirectory =
            Environment.GetEnvironmentVariable("SBLN_LOG_DIR") ?? Path.Combine(AppContext.BaseDirectory, "logs");
        private static readonly Lazy<LogSeverity> _minSeverity = new(ResolveMinSeverity);

        private static DateTime _lastCleanupDateUtc = DateTime.MinValue;
        private static bool _fileSinkFailureReported;

        private static DateTime _writersDate = DateTime.MinValue;
        private static StreamWriter _generalWriter;
        private static StreamWriter _errorWriter;

        public static async Task LogAsync(
            string src,
            LogSeverity severity,
            string? message,
            Exception? exception = null)
        {
            if (severity > _minSeverity.Value)
                return;

            var acquired = false;
            try
            {
                var now = DateTime.Now;
                var severityText = GetSeverityString(severity);
                var sourceText = SourceToString(src);
                var text = BuildMessage(message, exception, src, severity);
                var isError = severity is LogSeverity.Error or LogSeverity.Critical || exception != null;

                await _sync.WaitAsync();
                acquired = true;

                WriteToConsole(severityText, GetConsoleColor(severity), now.ToString("dd.MM | HH:mm:ss"), sourceText, text);

                if (EnsureWriters(now))
                {
                    var line = $"{severityText} {now:yyyy-MM-dd HH:mm:ss.fff} [{sourceText}] {text}";

                    await WriteLineAsync(_generalWriter, line);

                    if (isError)
                        await WriteLineAsync(_errorWriter, line);

                    CleanupOldLogsIfNeeded(DateTime.UtcNow);
                }
            }
            catch (Exception ex)
            {
                WriteFallback(src, severity, message, exception, ex);
            }
            finally
            {
                if (acquired)
                {
                    try { _sync.Release(); }
                    catch (ObjectDisposedException) { }
                    catch (SemaphoreFullException) { }
                }
            }
        }

        public static Task LogCriticalAsync(string source, string message, Exception? exc = null)
            => LogAsync(source, LogSeverity.Critical, message, exc);

        public static Task LogErrorAsync(string source, string message, Exception? exc = null)
            => LogAsync(source, LogSeverity.Error, message, exc);

        public static Task LogWarningAsync(string source, string message, Exception? exc = null)
            => LogAsync(source, LogSeverity.Warning, message, exc);

        public static Task LogInformationAsync(string source, string message)
            => LogAsync(source, LogSeverity.Info, message);

        public static Task LogDebugAsync(string source, string message)
            => LogAsync(source, LogSeverity.Debug, message);

        public static Task LogDiscordAsync(LogMessage log)
        {
            if (BenignReconnect(log) is { } reason)
                return LogAsync("discord", LogSeverity.Info, reason);

            return LogAsync("discord", NormalizeDiscordSeverity(log), log.Message, log.Exception);
        }

        private static LogSeverity ResolveMinSeverity()
        {
            try
            {
                return Enum.TryParse<LogSeverity>(Global.Vars.Cfg.logLevel, ignoreCase: true, out var level)
                    ? level
                    : LogSeverity.Info;
            }
            catch (Exception)
            {
                return LogSeverity.Info;
            }
        }

        private static string BenignReconnect(LogMessage log)
        {
            if (log.Exception is GatewayReconnectException)
                return "Gateway попросил переподключиться";

            if (log.Exception is WebSocketException or IOException &&
                log.Exception.Message.Contains("WebSocket connection was closed", StringComparison.OrdinalIgnoreCase))
            {
                return "Gateway закрыл соединение, переподключаюсь";
            }

            return null;
        }

        private static void WriteToConsole(
            string severityText,
            ConsoleColor severityColor,
            string timeStamp,
            string sourceText,
            string message)
        {
            var colorAvailable = TryGetConsoleColor(out var previousColor);

            try
            {
                if (colorAvailable) TrySetConsoleColor(severityColor);
                Console.Write(severityText);

                if (colorAvailable) TrySetConsoleColor(ConsoleColor.Gray);
                Console.Write($" {timeStamp} [{sourceText}] ");

                if (colorAvailable) TrySetConsoleColor(ConsoleColor.White);
                Console.WriteLine(message);
            }
            catch (IOException) { }
            finally
            {
                if (colorAvailable) TrySetConsoleColor(previousColor);
            }
        }

        private static bool TryGetConsoleColor(out ConsoleColor color)
        {
            try
            {
                color = Console.ForegroundColor;
                return true;
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or SecurityException)
            {
                color = ConsoleColor.Gray;
                return false;
            }
        }

        private static void TrySetConsoleColor(ConsoleColor color)
        {
            try { Console.ForegroundColor = color; }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or SecurityException) { }
        }

        private static bool EnsureWriters(DateTime now)
        {
            if (_writersDate == now.Date && _generalWriter is not null && _errorWriter is not null)
                return true;

            CloseWriters();

            try
            {
                Directory.CreateDirectory(_logsDirectory);
                _generalWriter = OpenWriter(Path.Combine(_logsDirectory, $"bot-{now:yyyy-MM-dd}.log"));
                _errorWriter = OpenWriter(Path.Combine(_logsDirectory, $"errors-{now:yyyy-MM-dd}.log"));
                _writersDate = now.Date;
                _fileSinkFailureReported = false;
                return true;
            }
            catch (Exception ex) when (IsFileSinkFailure(ex))
            {
                CloseWriters();
                ReportFileSinkFailure(_logsDirectory, ex);
                return false;
            }
        }

        private static StreamWriter OpenWriter(string path)
        {
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return new StreamWriter(stream, FileEncoding) { AutoFlush = true };
        }

        private static void CloseWriters()
        {
            _generalWriter?.Dispose();
            _errorWriter?.Dispose();
            _generalWriter = null;
            _errorWriter = null;
            _writersDate = DateTime.MinValue;
        }

        private static async Task WriteLineAsync(StreamWriter writer, string line)
        {
            if (writer is null)
                return;

            try
            {
                await writer.WriteLineAsync(line);
            }
            catch (Exception ex) when (IsFileSinkFailure(ex))
            {
                CloseWriters();
                ReportFileSinkFailure(_logsDirectory, ex);
            }
        }

        private static bool IsFileSinkFailure(Exception ex)
            => ex is IOException
                or UnauthorizedAccessException
                or SecurityException
                or NotSupportedException
                or ArgumentException
                or ObjectDisposedException;

        private static void ReportFileSinkFailure(string path, Exception ex)
        {
            if (_fileSinkFailureReported) return;
            _fileSinkFailureReported = true;

            try { Console.Error.WriteLine($"Лог-файл недоступен ({path}): {ex.GetType().Name}: {ex.Message}"); }
            catch (IOException) { }
        }

        private static void WriteFallback(
            string? src,
            LogSeverity severity,
            string? message,
            Exception? exception,
            Exception loggingFailure)
        {
            try
            {
                Console.Error.WriteLine(
                    $"[{GetSeverityString(severity)}] [{src ?? "(null)"}] {message}" +
                    $"{(exception != null ? Environment.NewLine + exception : string.Empty)}");
                Console.Error.WriteLine($"Сбой логгера: {loggingFailure}");
            }
            catch (IOException) { }
        }

        private static void CleanupOldLogsIfNeeded(DateTime utcNow)
        {
            if (_lastCleanupDateUtc.Date == utcNow.Date)
                return;

            _lastCleanupDateUtc = utcNow.Date;

            string[] files;
            try
            {
                files = Directory.GetFiles(_logsDirectory, "*.log", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (IsFileSinkFailure(ex))
            {
                ReportFileSinkFailure(_logsDirectory, ex);
                return;
            }

            var threshold = utcNow.AddDays(-LogRetentionDays);

            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTimeUtc < threshold)
                        fileInfo.Delete();
                }
                catch (Exception ex) when (IsFileSinkFailure(ex)) { }
            }
        }

        private static string BuildMessage(string? message, Exception? exception, string? src, LogSeverity severity)
        {
            if (exception != null)
                return BuildExceptionText(exception, message);

            if (!string.IsNullOrWhiteSpace(message))
                return message.Trim();

            return $"Пустая запись лога. Source='{src ?? "(null)"}', Severity='{severity}'";
        }

        private static string BuildExceptionText(Exception exception, string? message)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(message))
                sb.AppendLine(message.Trim());

            AppendException(sb, exception, "Exception");

            return sb.ToString().TrimEnd();
        }

        private static void AppendException(StringBuilder sb, Exception exception, string label)
        {
            sb.AppendLine($"{label}Type: {exception.GetType().FullName}");
            sb.AppendLine($"{label}Message: {exception.Message}");
            sb.AppendLine(string.IsNullOrWhiteSpace(exception.StackTrace)
                ? $"{label}StackTrace: (отсутствует)"
                : $"{label}StackTrace:{Environment.NewLine}{exception.StackTrace}");

            if (exception.InnerException != null)
                AppendException(sb, exception.InnerException, "Inner");
        }

        private static LogSeverity NormalizeDiscordSeverity(LogMessage log)
        {
            if (log.Exception is GatewayReconnectException)
                return LogSeverity.Info;

            if (log.Exception?.Message.Contains("Server requested a reconnect", StringComparison.OrdinalIgnoreCase) == true)
                return LogSeverity.Info;

            if (log.Message?.Contains("Server requested a reconnect", StringComparison.OrdinalIgnoreCase) == true)
                return LogSeverity.Info;

            return log.Severity;
        }

        private static string SourceToString(string? src)
        {
            if (string.IsNullOrWhiteSpace(src))
                return "UNKWN";

            return src.ToLowerInvariant() switch
            {
                "discord" => "DSCRD",
                "interactions" => "INTRA",
                "ppm" => "PPMGR",
                _ => src.Length > 5 ? src[..5].ToUpperInvariant() : src.ToUpperInvariant()
            };
        }

        private static string GetSeverityString(LogSeverity severity) => severity switch
        {
            LogSeverity.Critical => "CRTIC",
            LogSeverity.Debug => "D-BUG",
            LogSeverity.Error => "ERROR",
            LogSeverity.Info => "IN-FO",
            LogSeverity.Verbose => "VRBSE",
            LogSeverity.Warning => "WR-NG",
            _ => "UNKWN"
        };

        private static ConsoleColor GetConsoleColor(LogSeverity severity) => severity switch
        {
            LogSeverity.Critical => ConsoleColor.Red,
            LogSeverity.Debug => ConsoleColor.Magenta,
            LogSeverity.Error => ConsoleColor.DarkRed,
            LogSeverity.Info => ConsoleColor.Green,
            LogSeverity.Verbose => ConsoleColor.DarkCyan,
            LogSeverity.Warning => ConsoleColor.Yellow,
            _ => ConsoleColor.White
        };
    }
}
