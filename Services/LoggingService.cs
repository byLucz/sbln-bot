using Discord;
using Discord.WebSocket;
using System.Security;
using System.Text;

namespace sblngavnav6.Services
{
    public static class LoggingService
    {
        private static readonly SemaphoreSlim _sync = new(1, 1);
        private static readonly string _logsDirectory =
            Environment.GetEnvironmentVariable("SBLN_LOG_DIR") ?? Path.Combine(AppContext.BaseDirectory, "logs");

        private const int LogRetentionDays = 14;
        private static DateTime _lastCleanupDateUtc = DateTime.MinValue;
        private static bool _fileSinkFailureReported;

        public static async Task LogAsync(
            string src,
            LogSeverity severity,
            string? message,
            Exception? exception = null)
        {
            var acquired = false;
            try
            {
                var now = DateTime.Now;
                var utcNow = DateTime.UtcNow;

                var consoleTimeStamp = now.ToString("dd.MM | HH:mm:ss");
                var fileTimeStamp = now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                var severityText = GetSeverityString(severity);
                var severityColor = GetConsoleColor(severity);
                var sourceText = SourceToString(src);

                var consoleMessage = BuildConsoleMessage(message, exception, src, severity);
                var fileMessage = BuildFileMessage(message, exception, src, severity);

                var generalLogPath = GetGeneralLogFilePath(now);
                var errorLogPath = GetErrorLogFilePath(now);
                var isError = severity is LogSeverity.Error or LogSeverity.Critical || exception != null;

                await _sync.WaitAsync();
                acquired = true;

                WriteToConsole(severityText, severityColor, consoleTimeStamp, sourceText, consoleMessage);

                if (EnsureLogsDirectory())
                {
                    await WriteToFileAsync(generalLogPath, severityText, fileTimeStamp, sourceText, fileMessage);

                    if (isError)
                        await WriteToFileAsync(errorLogPath, severityText, fileTimeStamp, sourceText, fileMessage);

                    CleanupOldLogsIfNeeded(utcNow);
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
            var severity = NormalizeDiscordSeverity(log);
            return LogAsync("discord", severity, log.Message, log.Exception);
        }

        public static Task LogExceptionAsync(string source, Exception exc)
            => LogAsync(source, LogSeverity.Error, "Что-то умерло...", exc);

        private static void WriteToConsole(
            string severityText,
            ConsoleColor severityColor,
            string timeStamp,
            string sourceText,
            string message)
        {
            var previousColor = ConsoleColor.Gray;
            var colorAvailable = TryGetConsoleColor(out previousColor);

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

        private static bool EnsureLogsDirectory()
        {
            try
            {
                Directory.CreateDirectory(_logsDirectory);
                return true;
            }
            catch (Exception ex) when (IsFileSinkFailure(ex))
            {
                ReportFileSinkFailure(_logsDirectory, ex);
                return false;
            }
        }

        private static async Task WriteToFileAsync(
            string filePath,
            string severityText,
            string timeStamp,
            string sourceText,
            string message)
        {
            var line = $"{severityText} {timeStamp} [{sourceText}] {message}{Environment.NewLine}";

            try
            {
                await File.AppendAllTextAsync(filePath, line, Encoding.UTF8);
                _fileSinkFailureReported = false;
            }
            catch (Exception ex) when (IsFileSinkFailure(ex))
            {
                ReportFileSinkFailure(filePath, ex);
            }
        }

        private static bool IsFileSinkFailure(Exception ex)
            => ex is IOException
                or UnauthorizedAccessException
                or SecurityException
                or NotSupportedException
                or ArgumentException;

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

        private static string GetGeneralLogFilePath(DateTime now)
        {
            return Path.Combine(_logsDirectory, $"bot-{now:yyyy-MM-dd}.log");
        }

        private static string GetErrorLogFilePath(DateTime now)
        {
            return Path.Combine(_logsDirectory, $"errors-{now:yyyy-MM-dd}.log");
        }

        private static void CleanupOldLogsIfNeeded(DateTime utcNow)
        {
            if (_lastCleanupDateUtc.Date == utcNow.Date)
                return;

            _lastCleanupDateUtc = utcNow.Date;

            string[] files;
            try
            {
                if (!Directory.Exists(_logsDirectory))
                    return;

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

        private static string BuildConsoleMessage(
            string? message,
            Exception? exception,
            string? src,
            LogSeverity severity)
        {
            if (exception != null)
                return BuildExceptionText(exception, message, singleLine: false);

            if (!string.IsNullOrWhiteSpace(message))
                return message.Trim();

            return
                $"Пустая запись лога. Source='{src ?? "(null)"}', Severity='{severity}', " +
                "Message не передан, Exception отсутствует.";
        }

        private static string BuildFileMessage(
            string? message,
            Exception? exception,
            string? src,
            LogSeverity severity)
        {
            if (exception != null)
                return BuildExceptionText(exception, message, singleLine: false);

            if (!string.IsNullOrWhiteSpace(message))
                return message.Trim();

            return
                $"Пустая запись лога. Source='{src ?? "(null)"}', Severity='{severity}', " +
                "Message is null/empty, Exception is null.";
        }

        private static string BuildExceptionText(Exception exception, string? message, bool singleLine)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(message))
            {
                sb.Append(message.Trim());
                sb.Append(singleLine ? " | " : Environment.NewLine);
            }

            AppendException(sb, exception, "Exception", singleLine);

            return sb.ToString();
        }

        private static LogSeverity NormalizeDiscordSeverity(LogMessage log)
        {
            if (log.Exception is GatewayReconnectException)
                return LogSeverity.Info;

            if (log.Exception is not null &&
                log.Exception.Message.Contains("Server requested a reconnect", StringComparison.OrdinalIgnoreCase))
            {
                return LogSeverity.Info;
            }

            if (!string.IsNullOrWhiteSpace(log.Message) &&
                log.Message.Contains("Server requested a reconnect", StringComparison.OrdinalIgnoreCase))
            {
                return LogSeverity.Info;
            }

            return log.Severity;
        }

        private static void AppendException(
            StringBuilder sb,
            Exception exception,
            string label,
            bool singleLine)
        {
            sb.Append($"{label}Type: {exception.GetType().FullName}");
            sb.Append(singleLine ? " | " : Environment.NewLine);

            sb.Append($"{label}Message: {exception.Message}");
            sb.Append(singleLine ? " | " : Environment.NewLine);

            if (!string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                sb.Append($"{label}StackTrace:");
                sb.Append(singleLine ? " | " : Environment.NewLine);
                sb.Append(exception.StackTrace);
                sb.Append(singleLine ? " | " : Environment.NewLine);
            }
            else
            {
                sb.Append($"{label}StackTrace: (отсутствует)");
                sb.Append(singleLine ? " | " : Environment.NewLine);
            }

            if (exception.InnerException != null)
            {
                AppendException(sb, exception.InnerException, "Inner", singleLine);
            }
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

        private static string GetSeverityString(LogSeverity severity)
        {
            return severity switch
            {
                LogSeverity.Critical => "CRTIC",
                LogSeverity.Debug => "D-BUG",
                LogSeverity.Error => "ERROR",
                LogSeverity.Info => "IN-FO",
                LogSeverity.Verbose => "VRBSE",
                LogSeverity.Warning => "WR-NG",
                _ => "UNKWN"
            };
        }

        private static ConsoleColor GetConsoleColor(LogSeverity severity)
        {
            return severity switch
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
}
