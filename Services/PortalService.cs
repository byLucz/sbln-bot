using Discord;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Players;
using sblngavnav6.Common;
using sblngavnav6.Data;
using sblngavnav6.GVR;
using System.Text.Json;
using static sblngavnav6.Data.DataRoots;

namespace sblngavnav6.Services
{
    public sealed class PortalService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        private readonly DiscordSocketClient _client;
        private readonly IAudioService _audio;
        private readonly GVRMessagesHandler _gvr;
        private CancellationTokenSource _cts;
        private Task _loop = Task.CompletedTask;
        private string _phraseDate;

        public PortalService(DiscordSocketClient client, IAudioService audio, GVRMessagesHandler gvr)
        {
            _client = client;
            _audio = audio;
            _gvr = gvr;
        }

        private static string Dir => Global.Vars.Cfg.portalDir;

        public void Start(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(Dir) || _cts != null) return;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loop = LoopAsync(_cts.Token);
        }

        public async Task StopAsync()
        {
            if (_cts == null) return;
            await _cts.CancelAsync();
            await _loop;

            try { WriteStatus(false); }
            catch (Exception ex) when (CommonUtils.IsIoFailure(ex)) { await LoggingService.LogWarningAsync("PORTL", $"Не удалось записать оффлайн-статус: {ex.Message}"); }
        }

        private async Task LoopAsync(CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Dir);
            _phraseDate = ReadPhraseDate();

            using var timer = new PeriodicTimer(Interval);
            try
            {
                do
                {
                    try
                    {
                        WriteStatus(_client.ConnectionState == ConnectionState.Connected);

                        var today = DateTime.Now.ToString("yyyy-MM-dd");
                        if (_phraseDate != today && await _gvr.PhraseAsync() is { Length: > 0 } text)
                        {
                            CommonUtils.WriteAtomic(Path.Combine(Dir, "phrase.json"),
                                JsonSerializer.Serialize(new PortalPhraseDto { date = today, text = text }, AppJsonContext.Default.PortalPhraseDto));
                            _phraseDate = today;
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception ex) { await LoggingService.LogWarningAsync("PORTL", $"Экспорт для портала не удался: {ex.Message}"); }
                }
                while (await timer.WaitForNextTickAsync(cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }

        private void WriteStatus(bool online)
        {
            var me = _client.CurrentUser;
            var activity = _client.Activity;
            var status = new PortalStatusDto
            {
                version = Versioning.Version,
                channel = Versioning.Channel,
                commit = Versioning.Commit,
                online = online,
                startedAt = CommonUtils.Time.Started,
                updatedAt = DateTimeOffset.UtcNow,
                latency = _client.Latency,
                guilds = _client.Guilds.Count,
                members = _client.Guilds.Sum(guild => guild.MemberCount),
                playing = _audio.Players.Players.Count(player => player.State == PlayerState.Playing),
                name = me?.Username ?? "",
                discriminator = me?.Discriminator ?? "",
                avatar = me is null ? "" : me.GetAvatarUrl(ImageFormat.Auto, 256) ?? me.GetDefaultAvatarUrl(),
                activityType = activity is CustomStatusGame ? "Custom" : activity?.Type.ToString() ?? "",
                activity = activity is CustomStatusGame custom ? custom.State ?? "" : activity?.Name ?? ""
            };

            CommonUtils.WriteAtomic(Path.Combine(Dir, "status.json"), JsonSerializer.Serialize(status, AppJsonContext.Default.PortalStatusDto));
        }

        private static string ReadPhraseDate()
        {
            try
            {
                var path = Path.Combine(Dir, "phrase.json");
                return File.Exists(path)
                    ? JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.PortalPhraseDto)?.date
                    : null;
            }
            catch (Exception ex) when (ex is JsonException || CommonUtils.IsIoFailure(ex))
            {
                return null;
            }
        }
    }
}
