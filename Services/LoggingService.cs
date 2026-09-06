using Discord;
using Discord.WebSocket;
using System.Text;

namespace sblngavnav5X.Services
{
    public static class LoggingService
    {
        private static readonly SemaphoreSlim _sync = new(1, 1);
        private static readonly string _logsDirectory =
            Environment.GetEnvironmentVariable("SBLN_LOG_DIR") ?? Path.Combine(AppContext.BaseDirectory, "logs");

        private const int LogRetentionDays = 14;
        private static DateTime _lastCleanupDateUtc = DateTime.MinValue;

        public static async Task LogAsync(
            string src,
            LogSeverity severity,
            string? message,
            Exception? exception = null)
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

            await _sync.WaitAsync();
            try
            {
                Directory.CreateDirectory(_logsDirectory);

                WriteToConsole(severityText, severityColor, consoleTimeStamp, sourceText, consoleMessage);
                await WriteToFileAsync(generalLogPath, severityText, fileTimeStamp, sourceText, fileMessage);

                if (severity == LogSeverity.Error || severity == LogSeverity.Critical || exception != null)
                {
                    await WriteToFileAsync(errorLogPath, severityText, fileTimeStamp, sourceText, fileMessage);
                }

                await CleanupOldLogsIfNeededAsync(utcNow);
            }
            finally
            {
                _sync.Release();
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
            var previousColor = Console.ForegroundColor;

            try
            {
                Console.ForegroundColor = severityColor;
                Console.Write(severityText);

                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write($" {timeStamp} [{sourceText}] ");

                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(message);
            }
            finally
            {
                Console.ForegroundColor = previousColor;
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
            await File.AppendAllTextAsync(filePath, line, Encoding.UTF8);
        }

        private static string GetGeneralLogFilePath(DateTime now)
        {
            return Path.Combine(_logsDirectory, $"bot-{now:yyyy-MM-dd}.log");
        }

        private static string GetErrorLogFilePath(DateTime now)
        {
            return Path.Combine(_logsDirectory, $"errors-{now:yyyy-MM-dd}.log");
        }

        private static async Task CleanupOldLogsIfNeededAsync(DateTime utcNow)
        {
            if (_lastCleanupDateUtc.Date == utcNow.Date)
                return;

            _lastCleanupDateUtc = utcNow.Date;

            if (!Directory.Exists(_logsDirectory))
                return;

            var files = Directory.GetFiles(_logsDirectory, "*.log", SearchOption.TopDirectoryOnly);
            var threshold = utcNow.AddDays(-LogRetentionDays);

            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTimeUtc < threshold)
                    {
                        fileInfo.Delete();
                    }
                }
                catch {}
            }

            await Task.CompletedTask;
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
                "victoria" => "VI-KA",
                "audio" => "AUDIO",
                "admin" => "ADMIN",
                "gateway" => "GTWAY",
                "lavanode_0_socket" => "LVSOC",
                "lavanode_0" => "LVNOD",
                "bot" => "BOTWN",
                "comnd" => "COMND",
                "govor" => "GOVOR",
                "vi-ka" => "VI-KA",
                "ppm" => "PPMGR",
                _ => src.ToUpperInvariant()
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
