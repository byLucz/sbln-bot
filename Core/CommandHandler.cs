using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using sblngavnav5X.Data;
using sblngavnav5X.GVR;
using sblngavnav5X.Services;
using System.Collections.Concurrent;
using System.Reflection;
using Victoria;
using Timer = System.Timers.Timer;

namespace sblngavnav5X.Core
{
    public sealed class CommandHandler : IDisposable
    {
        private readonly DiscordSocketClient _client;
        private readonly CommandService _commands;
        private readonly IServiceProvider _services;
        private readonly GovorConfig _govorilka;
        private readonly LavaNode<LavaPlayer<LavaTrack>, LavaTrack> _lavaNode;
        private readonly GVRMessagesHandler _GVRMessagesHandler;
        private readonly SemaphoreSlim _lavaReconnectLock = new(1, 1);
        private static readonly ConcurrentDictionary<ulong, MailReplyRoute> _mailReplyRoutes = new();

        private static readonly Timer _timer = new(Global.Vars.BuiltIn.govorUpdTime)
        {
            AutoReset = true,
            Enabled = false
        };

        private static readonly object _timerLock = new();

        private bool _eventsHooked;
        private bool _timerStarted;
        private int _timerBusy;
        private DateTime _lastLavaReconnectAttemptUtc = DateTime.MinValue;
        private bool _disposed;
        private sealed record MailReplyRoute(ulong SenderId, ulong RecipientId, bool IsAnonymous, DateTimeOffset CreatedAt);

        public CommandHandler(IServiceProvider services, GovorConfig govorilka)
        {
            _services = services;
            _govorilka = govorilka;

            _commands = services.GetRequiredService<CommandService>();
            _client = services.GetRequiredService<DiscordSocketClient>();
            _lavaNode = services.GetRequiredService<LavaNode<LavaPlayer<LavaTrack>, LavaTrack>>();
            _GVRMessagesHandler = services.GetRequiredService<GVRMessagesHandler>();

            HookEvents();
        }

        public static void UpdateTimerInterval(double amount)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "Интервал должен быть больше 0");

            lock (_timerLock)
            {
                _timer.Interval = amount;
                Global.Vars.BuiltIn.govorUpdTime = (int)amount;
            }
        }

        public static double GetTimerInterval()
        {
            lock (_timerLock)
            {
                return _timer.Interval;
            }
        }

        public static void RegisterMailReplyRoute(ulong sentMessageId, ulong senderId, ulong recipientId, bool isAnonymous)
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-10);
            foreach (var key in _mailReplyRoutes.Keys.ToArray())
            {
                if (_mailReplyRoutes.TryGetValue(key, out var r) && r.CreatedAt < cutoff)
                    _mailReplyRoutes.TryRemove(key, out _);
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
            _client.Connected += OnClientConnectedAsync;
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
            _client.Connected -= OnClientConnectedAsync;
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

            await _GVRMessagesHandler.TrySendGeneratedMessageAsync(context);
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

            var forwardedEmbed = new EmbedBuilder()
                .WithColor(route.IsAnonymous ? Color.DarkGrey : Color.Blue)
                .WithTitle("📬 Ответ на почту")
                .WithDescription(text)
                .WithFooter("sbln почта📧");

            if (route.IsAnonymous)
            {
                forwardedEmbed.AddField("Отправитель:", "Анон", true);
            }
            else
            {
                forwardedEmbed
                    .AddField("Отправитель:", $"{message.Author.Username}", true)
                    .AddField("Получатель:", $"{sender.Username}", true);
            }

            if (!string.IsNullOrWhiteSpace(attachmentLinks))
                forwardedEmbed.AddField("Вложения", attachmentLinks);

            await sender.SendMessageAsync(embed: forwardedEmbed.Build());
            await message.Channel.SendMessageAsync(embed: new EmbedBuilder()
                .WithColor(Color.Green)
                .WithDescription("✅ Ответ отправлен")
                .WithFooter("sbln почта📧")
                .Build());

            await LoggingService.LogInformationAsync(
                "XMAIL",
                $"REPLY anonymous={route.IsAnonymous} sender={route.SenderId} recipient={route.RecipientId} replier={message.Author.Id} contentLength={message.Content} attachments={message.Attachments.Count}");

            return true;
        }

        private async Task CommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
        {
            if (!command.IsSpecified || result.IsSuccess)
                return;

            var cmd = command.Value;
            string reply = result.Error switch
            {
                CommandError.BadArgCount =>
                    $"🔴 Не хватает аргументов\n{BuildUsageHint(cmd)}",

                CommandError.ParseFailed =>
                    $"🔴 Неверный тип аргумента\n{BuildUsageHint(cmd)}",

                CommandError.ObjectNotFound =>
                    $"🔴 Не найден объект: {result.ErrorReason}\n{BuildUsageHint(cmd)}",

                CommandError.UnmetPrecondition when !string.IsNullOrWhiteSpace(result.ErrorReason) =>
                    result.ErrorReason,

                _ => $"🔴ОШИБКА🔴 - {result.ErrorReason}"
            };

            await context.Channel.SendMessageAsync(reply);
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
            return $"Использование: `{prefix} {cmd.Name}{aliases} {paramStr}`";
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

        private async Task OnClientReadyAsync()
        {
            await StartTimerAsync();
            await EnsureLavaNodeConnectedAsync("ready");
        }

        private Task OnClientConnectedAsync()
        {
            return EnsureLavaNodeConnectedAsync("connected");
        }

        private async Task StartTimerAsync()
        {
            if (_timerStarted)
                return;

            _timerStarted = true;
            _timer.Start();

            await LoggingService.LogInformationAsync("GOVOR", "Таймер сбора сообщений запущен");
        }

        private async Task EnsureLavaNodeConnectedAsync(string reason)
        {
            if (_lavaNode.IsConnected)
            {
                await LoggingService.LogInformationAsync("VI-KA", $"Lavalink подключен / State=({reason})");
                return;
            }

            await _lavaReconnectLock.WaitAsync();
            try
            {
                if (_lavaNode.IsConnected)
                    return;

                var elapsed = DateTime.UtcNow - _lastLavaReconnectAttemptUtc;
                if (elapsed < TimeSpan.FromSeconds(5))
                    await Task.Delay(TimeSpan.FromSeconds(5) - elapsed);

                _lastLavaReconnectAttemptUtc = DateTime.UtcNow;

                await LoggingService.LogInformationAsync("VI-KA", $"Переподключение Lavalink / State=({reason})...");
                await _services.UseLavaNodeAsync();

                if (_lavaNode.IsConnected)
                    await LoggingService.LogInformationAsync("VI-KA", "Подключен к Lavalink");
                else
                    await LoggingService.LogCriticalAsync("VI-KA", "Не подключен к Lavalink");
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync("VI-KA", $"Ошибка переподключения Lavalink / State=({reason})", ex);
            }
            finally
            {
                _lavaReconnectLock.Release();
            }
        }

        private void OnTimedEvent(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (Interlocked.Exchange(ref _timerBusy, 1) == 1)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessTimedEventAsync();
                }
                finally
                {
                    Interlocked.Exchange(ref _timerBusy, 0);
                }
            });
        }

        private async Task ProcessTimedEventAsync()
        {
            try
            {
                var channel = _client.GetChannel(Global.Vars.Cfg.messageSourceChannelId) as IMessageChannel;
                if (channel == null)
                {
                    await LoggingService.LogInformationAsync("GOVOR", $"Не удалось получить канал с ID {Global.Vars.Cfg.messageSourceChannelId}");
                    return;
                }

                var cursorPath = Global.Vars.Cfg.messagesFilePath + ".cursor";
                ulong? oldestId = null;
                if (File.Exists(cursorPath) && ulong.TryParse(await File.ReadAllTextAsync(cursorPath), out var parsed))
                    oldestId = parsed;

                var existingLines = File.Exists(Global.Vars.Cfg.messagesFilePath)
                    ? new HashSet<string>((await File.ReadAllLinesAsync(Global.Vars.Cfg.messagesFilePath))
                        .Select(l => l.Trim()).Where(l => l.Length > 0))
                    : new HashSet<string>();

                var newLines = new List<string>();
                ulong? newOldestId = null;

                var query = oldestId.HasValue
                    ? channel.GetMessagesAsync(oldestId.Value, Direction.Before, (int)_govorilka.Collection).Flatten()
                    : channel.GetMessagesAsync((int)_govorilka.Collection).Flatten();

                await foreach (var message in query)
                {
                    if (message == null ||
                        string.IsNullOrWhiteSpace(message.Content) ||
                        message.Author.IsBot ||
                        message.Attachments.Any() ||
                        message.Embeds.Any())
                    {
                        continue;
                    }

                    var content = message.Content.Trim();

                    if (content.StartsWith(Global.Vars.Cfg.pref1, StringComparison.OrdinalIgnoreCase) ||
                        content.StartsWith(Global.Vars.Cfg.pref2, StringComparison.OrdinalIgnoreCase) ||
                        content.Contains("https://", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (newOldestId == null || message.Id < newOldestId.Value)
                        newOldestId = message.Id;

                    if (existingLines.Add(content))
                        newLines.Add(content);
                }

                if (newLines.Count > 0)
                {
                    await File.AppendAllLinesAsync(Global.Vars.Cfg.messagesFilePath, newLines);
                    await LoggingService.LogInformationAsync("GOVOR", $"Добавлено новых сообщений: {newLines.Count}, всего в датасете: {existingLines.Count}");
                }
                else
                {
                    await LoggingService.LogInformationAsync("GOVOR", "Новых сообщений нет — история исчерпана или канал пуст");
                }

                if (newOldestId.HasValue)
                    await File.WriteAllTextAsync(cursorPath, newOldestId.Value.ToString());
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync("GOVOR", $"Произошла ошибка в обработке таймера: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                _timer.Stop();
                UnhookEvents();
                _lavaReconnectLock.Dispose();
            }
            catch
            {
            }
        }
    }
}