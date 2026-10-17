using Discord;
using Discord.WebSocket;
using sblngavnav6.Common;
using sblngavnav6.Data;
using sblngavnav6.GVR;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using static sblngavnav6.Data.DataRoots;

namespace sblngavnav6.Services
{
    public sealed class PortalService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
        private static readonly string Dir = Path.Combine(CommonUtils.DataDir, "portal");
        private static readonly string StatusPath = Path.Combine(Dir, "status.json");
        private static readonly string CommandsPath = Path.Combine(Dir, "commands.json");
        private static readonly string CorePath = Path.Combine(Dir, "core.json");
        private const int CoreWords = 150;
        private const int CoreLinksPerWord = 12;

        private readonly DiscordSocketClient _client;
        private readonly GVRMessagesHandler _gvr;
        private CancellationTokenSource _cts;
        private Task _loop = Task.CompletedTask;

        public PortalService(DiscordSocketClient client, GVRMessagesHandler gvr)
        {
            _client = client;
            _gvr = gvr;
        }

        public void Start(CancellationToken cancellationToken = default)
        {
            if (_cts != null) return;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _gvr.Collected += WriteCoreAsync;
            _loop = LoopAsync(_cts.Token);
        }

        public async Task StopAsync()
        {
            if (_cts == null) return;
            _gvr.Collected -= WriteCoreAsync;
            await _cts.CancelAsync();
            await _loop;

            try { Write(StatusPath, Status(false), AppJsonContext.Default.PortalStatusDto); }
            catch (Exception ex) when (CommonUtils.IsIoFailure(ex)) { await LoggingService.LogWarningAsync("PORTL", $"Не удалось записать оффлайн-статус: {ex.Message}"); }
        }

        private async Task LoopAsync(CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Dir);

            using var timer = new PeriodicTimer(Interval);
            try
            {
                Write(CommandsPath, Commands(), AppJsonContext.Default.ListPortalCommandSectionDto);
                if (!File.Exists(CorePath))
                    await WriteCoreAsync();

                do
                {
                    try { Write(StatusPath, Status(_client.ConnectionState == ConnectionState.Connected), AppJsonContext.Default.PortalStatusDto); }
                    catch (Exception ex) { await LoggingService.LogWarningAsync("PORTL", $"Экспорт для портала не удался: {ex.Message}"); }
                }
                while (await timer.WaitForNextTickAsync(cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex) when (CommonUtils.IsIoFailure(ex)) { await LoggingService.LogWarningAsync("PORTL", $"Портал не запустился: {ex.Message}"); }
        }

        private async Task WriteCoreAsync()
        {
            try
            {
                var (words, links) = await _gvr.CoreAsync(CoreWords, CoreLinksPerWord);
                Write(CorePath, new PortalCoreDto
                {
                    updatedAt = DateTimeOffset.UtcNow,
                    words = words.Select(word => new PortalCoreWordDto { text = word.Word, count = word.Count }).ToList(),
                    links = links.Select(link => new[] { link.A, link.B, link.Weight }).ToList()
                }, AppJsonContext.Default.PortalCoreDto);
            }
            catch (Exception ex) { await LoggingService.LogWarningAsync("PORTL", $"Семантическое ядро не обновилось: {ex.Message}"); }
        }

        private static void Write<T>(string path, T value, JsonTypeInfo<T> type) =>
            CommonUtils.WriteAtomic(path, JsonSerializer.Serialize(value, type));

        private PortalStatusDto Status(bool online)
        {
            var me = _client.CurrentUser;
            var activity = _client.Activity;
            return new PortalStatusDto
            {
                version = Versioning.Version,
                online = online,
                startedAt = CommonUtils.Time.Started,
                updatedAt = DateTimeOffset.UtcNow,
                latency = _client.Latency,
                name = me?.Username ?? "",
                discriminator = me?.Discriminator ?? "",
                avatar = me is null ? "" : me.GetAvatarUrl(ImageFormat.Auto, 256) ?? me.GetDefaultAvatarUrl(),
                activityType = activity is CustomStatusGame ? "Custom" : activity?.Type.ToString() ?? "",
                activity = activity is CustomStatusGame custom ? custom.State ?? "" : activity?.Name ?? ""
            };
        }

        private static List<PortalCommandSectionDto> Commands() =>
            HelpEmbedService.PublicSections
                .Select(section => new PortalCommandSectionDto
                {
                    title = section.Title,
                    commands = section.Commands
                        .Select(command => new PortalCommandDto
                        {
                            names = command.Command.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                            about = command.About,
                            telegram = command.Telegram
                        })
                        .ToList()
                })
                .ToList();
    }
}
