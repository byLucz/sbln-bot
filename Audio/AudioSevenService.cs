using System.Text.Json;
using Discord.WebSocket;
using Discord;
using Microsoft.Extensions.Logging;
using Victoria;
using Victoria.WebSocket.EventArgs;
using sblngavnav5X.Services;
using Discord.Commands;
using Victoria.Enums;
using sblngavnav5X.Core;

namespace sblngavnav5X.Audio
{
    public sealed class AudioSevenService : ModuleBase<SocketCommandContext>
    {
        private readonly LavaNode<LavaPlayer<LavaTrack>, LavaTrack> _lavaNode;
        private LavaTrack RequeueCurrentTrack { get; set; }
        private List<LavaTrack> Requeue { get; set; }
        private List<LavaPlayer<LavaTrack>> RepeatPlayer { get; } = [];

        private readonly ILogger _logger;
        public readonly HashSet<ulong> VoteQueue;
        public Dictionary<ulong, IVoiceChannel> VoiceChannels { get; } = new();
        public Dictionary<ulong, ITextChannel> TextChannels { get; } = new();
        private string FormatTime(TimeSpan time) => time.ToString(@"hh\:mm\:ss");

        public AudioSevenService(LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode, DiscordSocketClient socketClient, ILogger<AudioSevenService> logger)
        {
            _lavaNode = lavaNode;
            _logger = logger;
            _lavaNode.OnWebSocketClosed += OnWebSocketClosedAsync;
            _lavaNode.OnStats += OnStatsAsync;
            _lavaNode.OnPlayerUpdate += OnPlayerUpdateAsync;
            _lavaNode.OnTrackEnd += OnTrackEndAsync;
            _lavaNode.OnTrackStart += OnTrackStartAsync;
        }

        private Task OnTrackStartAsync(TrackStartEventArg arg)
        {
            return LoggingService.LogInformationAsync("VI-KA", $"UPD Начат трек: {arg.Track.Title}");
        }

        private async Task OnTrackEndAsync(TrackEndEventArg args)
        {
            await LoggingService.LogInformationAsync("VI-KA", $"UPD Завершён трек: {args.Track.Title} | Причина: {args.Reason}");
            var player = await _lavaNode.GetPlayerAsync(args.GuildId);
            if (args.Reason == TrackEndReason.Load_Failed)
            {
                if (player.GetQueue().Count == 0)
                {
                    return;
                }

                player.GetQueue().TryDequeue(out var track);
                await player.PlayAsync(_lavaNode, track, false);
            }

            if (args.Reason != TrackEndReason.Finished)
            {
                return;
            }

            if (RepeatPlayer.Contains(player))
            {
                var tempQueue = player.GetQueue();
                player.GetQueue().Clear();
                await player.PlayAsync(_lavaNode, args.Track);
                foreach (var item in tempQueue)
                {
                    player.GetQueue().Enqueue(item);
                }

                return;
            }


            if (player.Track is null)
            {
                if (player.GetQueue().Count == 0 && RequeueCurrentTrack is null && Requeue?.Count == 0)
                {
                    return;
                }
            }

            if (!player.GetQueue().TryDequeue(out var queueable))
            {
                if (RequeueCurrentTrack != null)
                {
                    await player.PlayAsync(_lavaNode, RequeueCurrentTrack);
                    await player.SeekAsync(_lavaNode, RequeueCurrentTrack.Position);
                    RequeueCurrentTrack = null;

                    if (!(Requeue?.Count > 0)) return;
                    foreach (var item in Requeue)
                    {
                        player.GetQueue().Enqueue(item);
                    }

                    Requeue = [];

                    return;
                }

                return;
            }

            if (queueable == null)
            {
                await LoggingService.LogInformationAsync("sbln muzik🎸🎧", "Следующий элемент не является треком или очередь пуста");
                return;
            }

            var voiceChannelUsers = (VoiceChannels[player.GuildId] as SocketVoiceChannel)?.ConnectedUsers
                .Where(x => !x.IsBot)
                .ToArray();
            if ((voiceChannelUsers ?? []).Length == 0)
            {
                await _lavaNode.LeaveAsync(VoiceChannels[player.GuildId]);
                VoiceChannels.Remove(player.GuildId);
                TextChannels.Remove(player.GuildId);
                return;
            }

            try
            {
                await player.PlayAsync(_lavaNode, queueable);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            await TextChannels[player.GuildId].SendMessageAsync(embed: await EmbedHandler.CreateMusicEmbed("sbln muzik🎸🎧", $"👺 **Ща Играет: **[{queueable.Title}]({queueable.Url})\n**👤 Автор: **{queueable.Author}\n**⏳ Длительность: **{FormatTime(queueable.Duration)}\n", Color.Purple));
        }


        private Task OnPlayerUpdateAsync(PlayerUpdateEventArg arg)
        {
            _logger.LogInformation("Задержка: {}", arg.Ping);
            return Task.CompletedTask;
        }

        private Task OnStatsAsync(StatsEventArg arg)
        {
            _logger.LogInformation("{}", JsonSerializer.Serialize(arg));
            return Task.CompletedTask;
        }

        private Task OnWebSocketClosedAsync(WebSocketClosedEventArg arg)
        {
            _logger.LogCritical("{}", JsonSerializer.Serialize(arg));
            return Task.CompletedTask;
        }

    }
    
}
