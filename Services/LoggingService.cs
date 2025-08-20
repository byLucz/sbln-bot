using Discord;

namespace sblngavnav5X.Services
{
    public static class LoggingService
    {
        public static async Task LogAsync(string src, LogSeverity severity, string message, Exception exception = null)
        {

            var timeStamp = DateTime.Now.ToString("dd/MM | HH:mm:ss");
            if (severity.Equals(null))
            {
                severity = LogSeverity.Warning;
            }
            await Append($"{GetSeverityString(severity)}", GetConsoleColor(severity));
            await Append($" {timeStamp} [{SourceToString(src)}] ", ConsoleColor.DarkGray);

            if (exception != null)
            {
                var exType = exception.GetType().Name;
                var exMessage = exception.Message ?? "(сообщение отсутствует)";
                var exStack = exception.StackTrace ?? "(стек вызовов отсутствует)";

                await Append($"{exType}: {exMessage}\n{exStack}\n", GetConsoleColor(severity));
            }
            else if (!string.IsNullOrWhiteSpace(message))
            {
                await Append($"{message}\n", ConsoleColor.White);
            }
            else
            {
                await Append("[TOTAL] Что-то умерло\n", ConsoleColor.DarkRed);
            }
        }


        public static async Task LogCriticalAsync(string source, string message, Exception exc = null)
            => await LogAsync(source, LogSeverity.Critical, message, exc);


        public static async Task LogInformationAsync(string source, string message)
            => await LogAsync(source, LogSeverity.Info, message);

        private static async Task Append(string message, ConsoleColor color)
        {
            await Task.Run(() => {
                Console.ForegroundColor = color;
                Console.Write(message);
            });
        }

        private static string SourceToString(string src)
        {
            switch (src.ToLower())
            {
                case "discord":
                    return "DSCRD";
                case "victoria":
                    return "VTORI";
                case "audio":
                    return "AUDIO";
                case "admin":
                    return "ADMIN";
                case "gateway":
                    return "GTWAY";
                case "lavanode_0_socket":
                    return "LVSOC";
                case "lavanode_0":
                    return "LVNOD";
                case "bot":
                    return "BOTWN";
                default:
                    return src;
            }
        }

        private static string GetSeverityString(LogSeverity severity)
        {
            switch (severity)
            {
                case LogSeverity.Critical:
                    return "CRIT";
                case LogSeverity.Debug:
                    return "DBUG";
                case LogSeverity.Error:
                    return "UERR";
                case LogSeverity.Info:
                    return "INFO";
                case LogSeverity.Verbose:
                    return "VERB";
                case LogSeverity.Warning:
                    return "WARN";
                default: return "UNKN";
            }
        }

        private static ConsoleColor GetConsoleColor(LogSeverity severity)
        {
            switch (severity)
            {
                case LogSeverity.Critical:
                    return ConsoleColor.Red;
                case LogSeverity.Debug:
                    return ConsoleColor.Magenta;
                case LogSeverity.Error:
                    return ConsoleColor.DarkRed;
                case LogSeverity.Info:
                    return ConsoleColor.Green;
                case LogSeverity.Verbose:
                    return ConsoleColor.DarkCyan;
                case LogSeverity.Warning:
                    return ConsoleColor.Yellow;
                default: return ConsoleColor.White;
            }
        }
    }
}
