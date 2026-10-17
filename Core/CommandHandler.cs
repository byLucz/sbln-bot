using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using DiscordTelegramFrontier;
using Microsoft.Extensions.DependencyInjection;
using sblngavnav6.Common;
using sblngavnav6.Data;
using sblngavnav6.GVR;
using sblngavnav6.Services;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using static sblngavnav6.Common.CommonUtils.Text;
using Timer = System.Timers.Timer;

namespace sblngavnav6.Core
{
    public sealed class CommandHandler : IDisposable, IAsyncDisposable
    {
        private readonly DiscordSocketClient _client;
        private static readonly Regex ArgLabelRegex = new(@"[<\[][^>\]]+[>\]]", RegexOptions.Compiled);
        private static string[] _commandNames = [];

        private readonly CommandService _commands;
        private readonly IServiceProvider _services;
        private readonly GVRConfig _govorilka;
        private readonly GVRMessagesHandler _gvrMessages;
        private readonly GVRDb _gvrDb;
        private readonly CancellationTokenSource _stopping = new();
        private readonly ConcurrentDictionary<ulong, MailReplyRoute> _mailReplyRoutes = new();
        private readonly object _timerLock = new();

        private readonly Timer _timer = new(GVRConfig.DefaultIntervalMs)
        {
            AutoReset = true,
            Enabled = false
        };

        private Task _timerTask = Task.CompletedTask;

        private bool _eventsHooked;
        private bool _timerStarted;
        private DateTimeOffset? _nextCollection;
        private bool _stopped;
        private bool _disposed;
        private sealed record MailReplyRoute(ulong SenderId, ulong RecipientId, bool IsAnonymous, DateTimeOffset CreatedAt);

        public CommandHandler(IServiceProvider services, GVRConfig govorilka)
        {
            _services = services;
            _govorilka = govorilka;

            _commands = services.GetRequiredService<CommandService>();
            _client = services.GetRequiredService<DiscordSocketClient>();
            _gvrMessages = services.GetRequiredService<GVRMessagesHandler>();
            _gvrDb = services.GetRequiredService<GVRDb>();

            HookEvents();
        }

        public void UpdateTimerInterval(double amount)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "Интервал должен быть больше 0");

            lock (_timerLock)
            {
                if (_disposed) return;
                _timer.Interval = amount;
                if (_timerStarted && !_stopped)
                    _nextCollection = DateTimeOffset.UtcNow.AddMilliseconds(amount);
            }
        }

        public DateTimeOffset? NextCollection
        {
            get
            {
                lock (_timerLock)
                    return _stopped ? null : _nextCollection;
            }
        }

        public double GetTimerInterval()
        {
            lock (_timerLock)
            {
                return _disposed ? GVRConfig.DefaultIntervalMs : _timer.Interval;
            }
        }

        public void RegisterMailReplyRoute(ulong sentMessageId, ulong senderId, ulong recipientId, bool isAnonymous)
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-10);
            foreach (var pair in _mailReplyRoutes.ToArray())
            {
                if (pair.Value.CreatedAt < cutoff)
                    _mailReplyRoutes.TryRemove(pair);
            }

            _mailReplyRoutes[sentMessageId] = new MailReplyRoute(senderId, recipientId, isAnonymous, DateTimeOffset.UtcNow);
        }

        public async Task InitializeAsync()
        {
            _commands.AddFrontierUserReaders();

            await _commands.AddModulesAsync(
                assembly: Assembly.GetEntryAssembly(),
                services: _services);

            _commandNames = _commands.Commands
                .SelectMany(command => command.Aliases)
                .Select(alias => alias.ToLowerInvariant())
                .Distinct()
                .ToArray();
        }

        private void HookEvents()
        {
            if (_eventsHooked)
                return;

            _commands.CommandExecuted += CommandExecutedAsync;
            _commands.Log += LogAsync;
            _client.MessageReceived += HandleMessageAsync;
            _client.Ready += OnClientReadyAsync;
            _timer.Elapsed += OnTimedEvent;

            _eventsHooked = true;
        }

        private void UnhookEvents()
        {
            if (!_eventsHooked)
                return;

            _commands.CommandExecuted -= CommandExecutedAsync;
            _commands.Log -= LogAsync;
            _client.MessageReceived -= HandleMessageAsync;
            _client.Ready -= OnClientReadyAsync;
            _timer.Elapsed -= OnTimedEvent;

            _eventsHooked = false;
        }

        private async Task HandleMessageAsync(SocketMessage socketMessage)
        {
            if (socketMessage is not SocketUserMessage message)
                return;

            if (message.Author.IsBot)
                return;

            if (await TryHandleMailReplyAsync(message))
                return;

            var context = new SocketCommandContext(_client, message);

            int characterPos = 0;
            var hasPrefix =
                message.HasStringPrefix(Global.Vars.Cfg.pref1, ref characterPos) ||
                message.HasStringPrefix(Global.Vars.Cfg.pref2, ref characterPos);

            if (hasPrefix)
            {
                var result = await _commands.ExecuteAsync(context, characterPos, _services);

                if (!result.IsSuccess)
                {
                    await LoggingService.LogInformationAsync(
                        "COMND",
                        $"Команда завершилась с ошибкой. User={message.Author.Id}, Error={result.Error}, Reason={result.ErrorReason}");
                }

                return;
            }

            if (DevGuildGate.IsDevGuild(context.Guild))
                _ = Task.Run(() => TrySendGeneratedAsync(context));
        }

        private async Task TrySendGeneratedAsync(SocketCommandContext context)
        {
            try
            {
                await _gvrMessages.TrySendGeneratedMessageAsync(context);
            }
            catch (Exception ex)
            {
                await LoggingService.LogErrorAsync("GOVOR", "Не удалось сгенерировать сообщение", ex);
            }
        }

        private async Task<bool> TryHandleMailReplyAsync(SocketUserMessage message)
        {
            if (message.Channel is not IDMChannel)
                return false;

            if (message.Reference?.MessageId.IsSpecified != true)
                return false;

            var referencedMessageId = message.Reference.MessageId.Value;
            if (!_mailReplyRoutes.TryGetValue(referencedMessageId, out var route))
                return false;

            if (message.Author.Id != route.RecipientId)
                return false;

            var sender = _client.GetUser(route.SenderId);
            if (sender == null)
                return false;

            var text = string.IsNullOrWhiteSpace(message.Content) ? "*пустое сообщение*" : message.Content;
            var attachmentLinks = message.Attachments.Any()
                ? string.Join('\n', message.Attachments.Select(a => a.Url))
                : null;

            const string footer = "sbln внутренняя почта📧";

            var fields = new List<EmbedFieldSpec>();

            if (route.IsAnonymous)
            {
                fields.Add(new EmbedFieldSpec("Отправитель:", "Анон", true));
            }
            else
            {
                fields.Add(new EmbedFieldSpec("Отправитель:", message.Author.Username, true));
                fields.Add(new EmbedFieldSpec("Получатель:", sender.Username, true));
            }

            if (!string.IsNullOrWhiteSpace(attachmentLinks))
                fields.Add(new EmbedFieldSpec("Вложения", attachmentLinks));

            var delivered = true;
            try
            {
                await sender.SendMessageAsync(embed: EmbedHandler.Build(new EmbedSpec
                {
                    Title = "📬 Ответ на почту",
                    Description = text,
                    Color = route.IsAnonymous ? Color.DarkGrey : Color.Blue,
                    Fields = fields,
                    Footer = footer
                }));
            }
            catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
            {
                delivered = false;
            }

            await message.Channel.SendMessageAsync(embed: EmbedHandler.Build(new EmbedSpec
            {
                Description = delivered ? "✅ Ответ отправлен" : "❌ Не доставлено, у отправителя закрыты ЛС",
                Color = delivered ? Color.Green : Color.Red,
                Footer = footer
            }));

            await LoggingService.LogInformationAsync(
                "XMAIL",
                $"REPLY delivered={delivered} anonymous={route.IsAnonymous} sender={route.SenderId} recipient={route.RecipientId} replier={message.Author.Id} contentLength={message.Content.Length} attachments={message.Attachments.Count}");

            return true;
        }

        private Task CommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
            => context.Channel is FrontierProxyChannel ? Task.CompletedTask : ReportFailureAsync(command, context, result);

        public static async Task ReportFailureAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
        {
            if (result.IsSuccess)
                return;

            var prefix = UsedPrefix(context);

            if (!command.IsSpecified)
            {
                if (result.Error == CommandError.UnknownCommand && Suggest(context.Message?.Content, prefix) is { } guess)
                    await context.Channel.SendMessageAsync($"🤔 такой команды нет, может `{prefix}{guess}`?", allowedMentions: AllowedMentions.None);
                return;
            }

            var cmd = command.Value;
            var help = HelpEmbedService.Find(cmd);

            var reason = result.Error switch
            {
                CommandError.BadArgCount or CommandError.ParseFailed or CommandError.ObjectNotFound =>
                    ArgumentProblem(cmd, help, context, result) + UsageHint(cmd, help, prefix),

                CommandError.UnmetPrecondition when !string.IsNullOrWhiteSpace(result.ErrorReason) =>
                    result.ErrorReason,

                CommandError.Exception =>
                    await ExceptionReasonAsync(cmd, result),

                CommandError.Unsuccessful =>
                    "команда не выполнилась",

                _ => string.IsNullOrWhiteSpace(result.ErrorReason)
                    ? result.Error.ToString()
                    : FirstLine(result.ErrorReason)
            };

            await context.Channel.SendMessageAsync(embed: await EmbedHandler.CreateErrorEmbed(cmd.Name, reason));
        }

        private static string UsedPrefix(ICommandContext context)
        {
            if (context.Channel is FrontierProxyChannel)
                return "/";

            var content = context.Message?.Content ?? "";
            return new[] { Global.Vars.Cfg.pref1, Global.Vars.Cfg.pref2 }
                .FirstOrDefault(prefix => !string.IsNullOrEmpty(prefix) && content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                ?? Global.Vars.Cfg.pref2;
        }

        private static string Suggest(string content, string prefix)
        {
            if (string.IsNullOrWhiteSpace(content) || !content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;

            var words = content[prefix.Length..].ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return null;

            string best = null;
            var bestDistance = int.MaxValue;

            foreach (var name in _commandNames)
            {
                var size = name.Count(symbol => symbol == ' ') + 1;
                if (size > words.Length)
                    continue;

                var head = string.Join(' ', words.Take(size));
                var distance = ToRuLayout(head) == name ? 0 : Distance(head, name);
                var allowed = name.Length <= 3 ? 0 : name.Length <= 5 ? 1 : 2;

                if (distance <= allowed && distance < bestDistance)
                {
                    best = name;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static string ArgumentProblem(CommandInfo cmd, HelpCommand help, ICommandContext context, IResult result)
        {
            var parameters = cmd.Parameters;
            var labels = ArgLabels(help, parameters);
            var tokens = Arguments(cmd, context.Message?.Content);

            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (parameter.IsRemainder || parameter.IsMultiple)
                    break;

                if (i >= tokens.Length)
                {
                    if (parameter.IsOptional)
                        continue;
                    return $"не хватает: {labels[i]}";
                }

                var type = Nullable.GetUnderlyingType(parameter.Type) ?? parameter.Type;
                if (IsNumber(type) && !double.TryParse(tokens[i].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return $"ожидал число вместо `{tokens[i]}`";

                if (result.Error == CommandError.ObjectNotFound && typeof(IUser).IsAssignableFrom(type))
                    return $"не нашёл участника `{tokens[i]}`";
            }

            return result.Error switch
            {
                CommandError.BadArgCount when tokens.Length > parameters.Count && !parameters.Any(p => p.IsRemainder || p.IsMultiple) => "лишние аргументы",
                CommandError.BadArgCount => "не хватает аргументов",
                CommandError.ObjectNotFound => "не нашёл, кого ты имел в виду",
                _ => "не смог разобрать аргументы"
            };
        }

        private static string[] Arguments(CommandInfo cmd, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return [];

            var text = content.TrimStart();
            var prefix = new[] { Global.Vars.Cfg.pref1, Global.Vars.Cfg.pref2 }
                .FirstOrDefault(value => !string.IsNullOrEmpty(value) && text.StartsWith(value, StringComparison.OrdinalIgnoreCase));
            if (prefix is not null)
                text = text[prefix.Length..];

            var alias = cmd.Aliases.Prepend(cmd.Name)
                .OrderByDescending(value => value.Length)
                .FirstOrDefault(value => text.StartsWith(value, StringComparison.OrdinalIgnoreCase));
            if (alias is not null)
                text = text[alias.Length..];

            return text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        private static string[] ArgLabels(HelpCommand help, IReadOnlyList<Discord.Commands.ParameterInfo> parameters)
        {
            var fromHelp = help?.Args is { } args
                ? ArgLabelRegex.Matches(args).Select(match => match.Value).ToArray()
                : [];

            return parameters
                .Select((parameter, i) => fromHelp.Length == parameters.Count ? fromHelp[i] : $"<{FriendlyTypeName(parameter.Type)}>")
                .ToArray();
        }

        private static string UsageHint(CommandInfo cmd, HelpCommand help, string prefix)
        {
            if (help is not null)
                return $"\nкак надо: `{prefix}{help.Usage}`" + (help.Example is null ? "" : $"\nпример: `{help.Example}`");

            if (!cmd.Parameters.Any())
                return string.Empty;

            var args = string.Join(" ", cmd.Parameters.Select(p => p.IsOptional ? $"[{FriendlyTypeName(p.Type)}]" : $"<{FriendlyTypeName(p.Type)}>"));
            return $"\nкак надо: `{prefix}{cmd.Name} {args}`";
        }

        private static async Task<string> ExceptionReasonAsync(CommandInfo cmd, IResult result)
        {
            var code = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
            var exception = result is ExecuteResult execute ? execute.Exception : null;
            await LoggingService.LogErrorAsync("COMND", $"[{code}] команда {cmd.Name} упала", exception);
            return $"внутренняя ошибка, код `{code}`";
        }

        private static bool IsNumber(Type type) =>
            type == typeof(int) || type == typeof(long) || type == typeof(uint) || type == typeof(ulong) ||
            type == typeof(float) || type == typeof(double) || type == typeof(decimal);

        private static string FriendlyTypeName(Type t)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            if (IsNumber(u)) return "число";
            if (u == typeof(string)) return "текст";
            if (u == typeof(bool)) return "да/нет";
            if (typeof(IUser).IsAssignableFrom(u)) return "@юзер";
            if (u.Name.Contains("Role")) return "@роль";
            if (u.Name.Contains("Channel")) return "#канал";
            return u.Name.ToLower();
        }

        private Task LogAsync(LogMessage log)
        {
            if (log.Exception is CommandException)
                return Task.CompletedTask;

            return LoggingService.LogAsync("COMND", log.Severity, log.Message, log.Exception);
        }

        private Task OnClientReadyAsync() => StartTimerAsync();

        private async Task StartTimerAsync()
        {
            lock (_timerLock)
            {
                if (_timerStarted || _stopped) return;
                _timerStarted = true;
                _timer.Start();
                _nextCollection = DateTimeOffset.UtcNow.AddMilliseconds(_timer.Interval);
            }

            await LoggingService.LogInformationAsync("GOVOR", "Таймер сбора сообщений запущен");
        }


        private void OnTimedEvent(object? sender, System.Timers.ElapsedEventArgs e)
        {
            lock (_timerLock)
            {
                _nextCollection = DateTimeOffset.UtcNow.AddMilliseconds(_timer.Interval);
                if (_stopped || !_timerTask.IsCompleted) return;
                _timerTask = Task.Run(() => ProcessTimedEventAsync(_stopping.Token));
            }
        }

        private async Task ProcessTimedEventAsync(CancellationToken cancellationToken)
        {
            try
            {
                var channel = _client.GetChannel(Global.Vars.Cfg.messageSourceChannelId) as IMessageChannel;
                if (channel == null)
                {
                    await LoggingService.LogInformationAsync("GOVOR", $"Не удалось получить канал с ID {Global.Vars.Cfg.messageSourceChannelId}");
                    return;
                }

                var (added, images, scanned, removed, total) = await _gvrMessages.CollectAsync(channel, (int)_govorilka.Collection, cancellationToken);

                await LoggingService.LogInformationAsync("GOVOR",
                    $"Сбор: просмотрено {scanned}, новых {added}, картинок {images}, вычищено {removed}, всего в базе {total}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync("GOVOR", "Произошла ошибка в обработке таймера", ex);
            }
        }

        public async Task StopAsync()
        {
            if (_disposed) return;
            Task timerTask;
            lock (_timerLock)
            {
                _stopped = true;
                _timer.Stop();
                UnhookEvents();
                timerTask = _timerTask;
            }
            await _stopping.CancelAsync();
            await timerTask;
        }

        public async ValueTask DisposeAsync()
        {
            try { await StopAsync(); }
            finally { Dispose(); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_timerLock)
            {
                _stopped = true;
                _timer.Stop();
                _timer.Dispose();
                UnhookEvents();
            }
            _stopping.Cancel();
            _stopping.Dispose();
        }
    }
}
