using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using sblngavnav6.Common;
using static sblngavnav6.Common.CommonUtils.Text;
using sblngavnav6.Data;
using sblngavnav6.GVR;
using sblngavnav6.Services;
using System.Collections.Concurrent;
using System.Reflection;
using Timer = System.Timers.Timer;

namespace sblngavnav6.Core
{
    public sealed class CommandHandler : IDisposable, IAsyncDisposable
    {
        private readonly DiscordSocketClient _client;
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
            await _commands.AddModulesAsync(
                assembly: Assembly.GetEntryAssembly(),
                services: _services);
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

            await _gvrMessages.TrySendGeneratedMessageAsync(context);
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

            await sender.SendMessageAsync(embed: EmbedHandler.Build(new EmbedSpec
            {
                Title = "📬 Ответ на почту",
                Description = text,
                Color = route.IsAnonymous ? Color.DarkGrey : Color.Blue,
                Fields = fields,
                Footer = footer
            }));

            await message.Channel.SendMessageAsync(embed: EmbedHandler.Build(new EmbedSpec
            {
                Description = "✅ Ответ отправлен",
                Color = Color.Green,
                Footer = footer
            }));

            await LoggingService.LogInformationAsync(
                "XMAIL",
                $"REPLY anonymous={route.IsAnonymous} sender={route.SenderId} recipient={route.RecipientId} replier={message.Author.Id} contentLength={message.Content.Length} attachments={message.Attachments.Count}");

            return true;
        }

        private async Task CommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
        {
            if (!command.IsSpecified || result.IsSuccess)
                return;

            var cmd = command.Value;
            string reason = result.Error switch
            {
                CommandError.BadArgCount =>
                    $"не хватает аргументов{BuildUsageHint(cmd)}",

                CommandError.ParseFailed =>
                    $"неверный тип аргумента{BuildUsageHint(cmd)}",

                CommandError.ObjectNotFound =>
                    $"не найден объект: {result.ErrorReason}{BuildUsageHint(cmd)}",

                CommandError.UnmetPrecondition when !string.IsNullOrWhiteSpace(result.ErrorReason) =>
                    result.ErrorReason,

                CommandError.Exception =>
                    "внутренняя ошибка, детали в логах",

                CommandError.Unsuccessful =>
                    "команда не выполнилась",

                _ => string.IsNullOrWhiteSpace(result.ErrorReason)
                    ? result.Error.ToString()
                    : FirstLine(result.ErrorReason)
            };

            await context.Channel.SendMessageAsync(embed: await EmbedHandler.CreateErrorEmbed(cmd.Name, reason));
        }

        private static string BuildUsageHint(CommandInfo cmd)
        {
            if (!cmd.Parameters.Any())
                return string.Empty;

            var paramStr = string.Join(" ", cmd.Parameters.Select(p =>
            {
                var typeName = FriendlyTypeName(p.Type);
                var label = string.IsNullOrWhiteSpace(p.Summary) ? $"{p.Name}:{typeName}" : p.Summary;
                return p.IsOptional ? $"[{label}]" : $"<{label}>";
            }));

            var prefix = Global.Vars.Cfg.pref1;
            var aliases = cmd.Aliases.Count > 0 ? $" ({string.Join("/", cmd.Aliases)})" : "";
            return $"\nИспользование: `{prefix} {cmd.Name}{aliases} {paramStr}`";
        }

        private static string FriendlyTypeName(Type t)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            if (u == typeof(int) || u == typeof(long) || u == typeof(uint)) return "число";
            if (u == typeof(float) || u == typeof(double) || u == typeof(decimal)) return "число";
            if (u == typeof(string)) return "текст";
            if (u == typeof(bool)) return "да/нет";
            if (u.Name.Contains("GuildUser") || u.Name.Contains("SocketUser")) return "@пользователь";
            if (u.Name.Contains("Role")) return "@роль";
            if (u.Name.Contains("Channel")) return "#канал";
            return u.Name.ToLower();
        }

        private Task LogAsync(LogMessage log)
        {
            return LoggingService.LogInformationAsync("COMND", log.ToString());
        }

        private Task OnClientReadyAsync() => StartTimerAsync();

        private async Task StartTimerAsync()
        {
            lock (_timerLock)
            {
                if (_timerStarted || _stopped) return;
                _timerStarted = true;
                _timer.Start();
            }

            await LoggingService.LogInformationAsync("GOVOR", "Таймер сбора сообщений запущен");
        }


        private void OnTimedEvent(object? sender, System.Timers.ElapsedEventArgs e)
        {
            lock (_timerLock)
            {
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

                var newLines = new List<string>();
                var options = new RequestOptions { CancelToken = cancellationToken };

                await foreach (var message in channel.GetMessagesAsync((int)_govorilka.Collection, options: options).Flatten())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (message == null ||
                        string.IsNullOrWhiteSpace(message.Content) ||
                        message.Author.IsBot ||
                        message.Attachments.Any() ||
                        message.Embeds.Any())
                    {
                        continue;
                    }

                    var content = message.Content.Trim();

                    if (content.Contains("https://", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var clean = GVRText.Sanitize(content);

                    if (clean is not null)
                        newLines.Add(clean);
                }

                if (newLines.Count > 0)
                {
                    var stored = await _gvrDb.AddAsync(0, newLines, cancellationToken);
                    var (total, _) = await _gvrDb.StampAsync(cancellationToken);

                    await LoggingService.LogInformationAsync("GOVOR", $"Добавлено новых: {stored} из {newLines.Count}, всего в базе: {total}");
                }
                else
                {
                    await LoggingService.LogInformationAsync("GOVOR", "Подходящих сообщений не набралось");
                }
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
