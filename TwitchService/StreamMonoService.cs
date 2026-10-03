using Discord;
using Discord.WebSocket;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Games;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.Api.Services;
using TwitchLib.Api.Services.Events;
using TwitchLib.Api.Services.Events.LiveStreamMonitor;
using sblngavnav6.Common;
using sblngavnav6.Data;
using sblngavnav6.Services;

namespace sblngavnav6.TwitchService
{
    public class StreamMonoService : StreamMonoServiceBase, IDisposable
    {
        private readonly DiscordSocketClient _discord;
        private LiveStreamMonitorService _liveStreamMonitor;
        private bool _disposed;

        private async Task LoadStreamOnlineState()
        {
            foreach (var id in await DataBase.LoadStreamsOnline())
                StreamsOnline.TryAdd(id, 0);
        }

        public StreamMonoService(DiscordSocketClient discord)
        {
            _discord = discord;

            UpdInt = Global.Vars.BuiltIn.streamUpdTime;

            TwitchAPI api = new TwitchAPI();
            api.Settings.ClientId = Global.Vars.Cfg.streamCid;
            api.Settings.AccessToken = Global.Vars.Cfg.streamAuth;
            TwitchApi = api;
        }

        public async Task CreateStreamMonoAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_liveStreamMonitor != null)
                return;

            StreamModels = new Dictionary<string, StreamData>();
            GetStreamerList();
            await GetStreamerIdDictAsync();

            await LoggingService.LogInformationAsync("TTVLK", $"Кол-во серверов: {_discord.Guilds.Count}");

            var notifChannels = await ResolveNotifChannels();

            if (notifChannels.Count > 0)
                await LoggingService.LogInformationAsync("TTVLK", $"Кол-во каналов оповещений: {notifChannels.Count}");
            else
                await LoggingService.LogCriticalAsync("TTVLK", "Не найдено каналов оповещений");

            try
            {
                StreamProfileImages = await GetProfImgUrlsAsync(StreamIdList, cancellationToken);
            }
            catch (TwitchLib.Api.Core.Exceptions.InternalServerErrorException ex)
            {
                if (CreationAttempts == 1)
                {
                    await LoggingService.LogCriticalAsync("TTVLK", "Максимальное число попыток достигнуто, StreamMonitor выключен");
                    CreationAttempts = 0;
                    return;
                }

                await LoggingService.LogCriticalAsync("TTVLK", $"{ex.GetType().Name} - Попытка {CreationAttempts}: Ошибка в загрузке профилей, повторная попытка...");
                await VerifyAndGetStreamIdAsync(cancellationToken);
                CreationAttempts++;
                await CreateStreamMonoAsync(cancellationToken);
                return;
            }

            LiveStreamMonitorService monitor = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (StreamIdList == null || StreamIdList.Count == 0)
                    throw new ArgumentException("StreamIdList пуст");

                monitor = new LiveStreamMonitorService(TwitchApi, UpdInt, 100);
                monitor.OnServiceTick += OnServiceTickEvent;
                monitor.OnChannelsSet += OnChannelsSetEvent;
                monitor.OnServiceStarted += OnServiceStartedEvent;
                monitor.OnServiceStopped += OnServiceStoppedEvent;
                monitor.OnStreamOnline += OnStreamOnlineEventAsync;
                monitor.OnStreamOffline += OnStreamOfflineEvent;

                await LoadStreamOnlineState();
                monitor.SetChannelsById(StreamIdList);
                monitor.Start();

                _liveStreamMonitor = monitor;
                monitor = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DetachMonitor(monitor);
                throw;
            }
            catch (ArgumentException e)
            {
                DetachMonitor(monitor);
                await LoggingService.LogInformationAsync("TTVLK", $"Лист стримеров пуст: {e.Message}");
            }
            catch (Exception ex)
            {
                DetachMonitor(monitor);
                await LoggingService.LogCriticalAsync("TTVLK", "Не удалось запустить мониторинг", ex);
            }

            await LoggingService.LogInformationAsync("TTVLK", $"Статус мониторинга - {_liveStreamMonitor?.Enabled ?? false}");
        }

        private async Task<List<SocketTextChannel>> ResolveNotifChannels()
        {
            var channels = new List<SocketTextChannel>();

            foreach (var guild in _discord.Guilds)
            {
                var gs = await DataBase.GetGuildSettings(guild.Id);
                if (gs.StreamNotifChannelId is not ulong chId)
                    continue;

                var channel = guild.GetTextChannel(chId);
                if (channel != null)
                    channels.Add(channel);
            }

            return channels;
        }

        private void DetachMonitor(LiveStreamMonitorService monitor)
        {
            if (monitor == null) return;

            monitor.OnServiceTick -= OnServiceTickEvent;
            monitor.OnChannelsSet -= OnChannelsSetEvent;
            monitor.OnServiceStarted -= OnServiceStartedEvent;
            monitor.OnServiceStopped -= OnServiceStoppedEvent;
            monitor.OnStreamOnline -= OnStreamOnlineEventAsync;
            monitor.OnStreamOffline -= OnStreamOfflineEvent;

            if (monitor.Enabled)
                monitor.Stop();
        }

        private void OnServiceTickEvent(object sender, OnServiceTickArgs e)
        {
        }

        private static async void OnServiceStartedEvent(object sender, OnServiceStartedArgs e)
        {
            await LoggingService.LogInformationAsync("TTVLK", "Мониторинг успешно запущен!");
        }

        private static async void OnServiceStoppedEvent(object sender, OnServiceStoppedArgs e)
        {
            await LoggingService.LogInformationAsync("TTVLK", "Мониторинг остановлен...");
        }

        private async void OnStreamOnlineEventAsync(object sender, OnStreamOnlineArgs e)
        {
            if (!StreamsOnline.TryAdd(e.Stream.UserId, 0))
                return;

            try
            {
                var getGamesResponse = await TwitchApi.Helix.Games.GetGamesAsync(new List<string> { e.Stream.GameId });
                UpdateLiveStreamModelsAsync(e.Stream, getGamesResponse);

                var embed = CreateStreamerEmbed(StreamModels[e.Stream.UserId], e.Stream.ThumbnailUrl);

                foreach (var channel in await ResolveNotifChannels())
                {
                    try
                    {
                        await channel.SendMessageAsync($"@everyone, {e.Stream.UserName} сейчас стримит!", false, embed);
                    }
                    catch (Exception ex)
                    {
                        await LoggingService.LogWarningAsync("TTVLK", $"Не удалось оповестить канал {channel.Guild.Name}/{channel.Name}: {ex.Message}");
                    }
                }

                await DataBase.AddStreamOnline(e.Stream.UserId);
                await LoggingService.LogInformationAsync("TTVLK", $"{e.Stream.UserName} добавлен в лист отслеживания");
            }
            catch (Exception ex)
            {
                StreamsOnline.TryRemove(e.Stream.UserId, out _);
                await LoggingService.LogCriticalAsync("TTVLK", $"OnStreamOnline {e.Stream.UserName}: {ex.Message}");
            }
        }

        private async void OnStreamOfflineEvent(object sender, OnStreamOfflineArgs e)
        {
            try
            {
                StreamsOnline.TryRemove(e.Stream.UserId, out _);
                await DataBase.RemoveStreamOnline(e.Stream.UserId);
                await LoggingService.LogInformationAsync("TTVLK", $"{e.Stream.UserName} оффлайн, убран из листа");
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync("TTVLK", $"OnStreamOffline {e.Stream.UserName}: {ex.Message}");
            }
        }

        private static async void OnChannelsSetEvent(object sender, OnChannelsSetArgs e)
        {
            if (e.Channels is { Count: > 0 })
            {
                await LoggingService.LogInformationAsync("TTVLK", "Каналы загружены");
                return;
            }

            await LoggingService.LogCriticalAsync("TTVLK", "Каналы не настроены");
        }

        private void GetStreamerList()
        {
            List<string> tmp = DataRoots.States.Streamers;
            StreamList = tmp ?? new List<string>();
        }

        private async Task GetStreamerIdDictAsync()
        {
            StreamIds = new Dictionary<string, string>(DataRoots.States.StreamerMap);
            StreamIdList = DataRoots.States.StreamerIds.ToList();

            await LoggingService.LogInformationAsync("TTVLK", "ТвичМонитор включен");
        }

        private void UpdateLiveStreamModelsAsync(TwitchLib.Api.Helix.Models.Streams.GetStreams.Stream twitchStream, GetGamesResponse game)
        {
            string gameName = game.Data.Length != 0 ? game.Data[0].Name : "Неизвестна";

            StreamData streamModel = new()
            {
                Stream = twitchStream.UserName,
                Thumb = twitchStream.ThumbnailUrl,
                Id = twitchStream.UserId,
                Avatar = StreamProfileImages[twitchStream.UserId],
                Title = twitchStream.Title,
                Game = gameName,
                Viewers = twitchStream.ViewerCount,
                Link = $"https://www.twitch.tv/{twitchStream.UserName}"
            };

            if (StreamModels.ContainsKey(twitchStream.UserId))
                StreamModels.Remove(twitchStream.UserId);

            StreamModels.Add(twitchStream.UserId, streamModel);
        }

        private async Task<Dictionary<string, string>> GetProfImgUrlsAsync(List<string> streamIds, CancellationToken cancellationToken = default)
        {
            Dictionary<string, string> profImages = new();

            if (!streamIds.Any())
                return profImages;

            GetUsersResponse usersResponse =
                await TwitchApi.Helix.Users.GetUsersAsync(streamIds, null, TwitchApi.Settings.AccessToken).WaitAsync(cancellationToken);

            foreach (var user in usersResponse.Users)
                profImages[user.Id] = user.ProfileImageUrl;

            return profImages;
        }

        private static Embed CreateStreamerEmbed(StreamData streamModel, string thumbnailUrl) =>
            EmbedHandler.Build(new EmbedSpec
            {
                AuthorName = streamModel.Stream,
                AuthorIconUrl = streamModel.Avatar,
                Title = streamModel.Title,
                Url = streamModel.Link,
                Color = new Color(191, 0, 255),
                ImageUrl = string.IsNullOrWhiteSpace(thumbnailUrl)
                    ? null
                    : thumbnailUrl.Replace("{width}", "1280").Replace("{height}", "720")
                      + $"?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                Fields =
                [
                    new EmbedFieldSpec("Категория", streamModel.Game, true),
                    new EmbedFieldSpec("Зрители", $"{streamModel.Viewers}", true)
                ],
                Footer = "sbln твич📺 / powered by TwitchLib"
            });

        public async Task VerifyAndGetStreamIdAsync(CancellationToken cancellationToken = default)
        {
            Dictionary<string, string> streamsidsDict = new();
            List<string> verifiedStreams = new();
            List<string> tmp = new() { " " };

            foreach (string s in StreamList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                tmp[0] = s;

                try
                {
                    GetUsersResponse response =
                        await TwitchApi.Helix.Users.GetUsersAsync(logins: tmp, accessToken: TwitchApi.Settings.AccessToken).WaitAsync(cancellationToken);

                    if (response.Users.Length > 0)
                    {
                        streamsidsDict.Add(response.Users[0].Login, response.Users[0].Id);
                        verifiedStreams.Add(s);
                    }

                    await Task.Delay(5000, cancellationToken);
                }
                catch (TwitchLib.Api.Core.Exceptions.InternalServerErrorException ex)
                {
                    await LoggingService.LogCriticalAsync("TTVLK", $"InternalService: {ex.Message}");
                }
            }

            await UpdateChannelsToMonitor(cancellationToken);
        }

        public async Task UpdateChannelsToMonitor(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await GetStreamerIdDictAsync();
            if (_liveStreamMonitor == null) return;

            try
            {
                _liveStreamMonitor.SetChannelsById(StreamIdList);
            }
            catch (ArgumentException ex)
            {
                await LoggingService.LogCriticalAsync("TTVLK", $"Argument: {ex.Message}");

                if (_liveStreamMonitor.Enabled)
                    _liveStreamMonitor.Stop();
            }

            StreamProfileImages = await GetProfImgUrlsAsync(StreamIdList, cancellationToken);
            GetStreamerList();
        }

        public bool StopLsm()
        {
            if (_disposed || _liveStreamMonitor?.Enabled != true)
                return false;

            _liveStreamMonitor.Stop();
            return true;
        }

        public bool StartLsm()
        {
            if (_disposed || _liveStreamMonitor == null || _liveStreamMonitor.Enabled)
                return false;

            _liveStreamMonitor.Start();
            return true;
        }

        public string StatusLsm()
        {
            return _liveStreamMonitor?.Enabled == true ? "Онлайн" : "Oффлайн";
        }

        public Task StopAsync()
        {
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            var monitor = _liveStreamMonitor;
            if (monitor == null) return;

            monitor.OnServiceTick -= OnServiceTickEvent;
            monitor.OnChannelsSet -= OnChannelsSetEvent;
            monitor.OnServiceStarted -= OnServiceStartedEvent;
            monitor.OnServiceStopped -= OnServiceStoppedEvent;
            monitor.OnStreamOnline -= OnStreamOnlineEventAsync;
            monitor.OnStreamOffline -= OnStreamOfflineEvent;

            if (monitor.Enabled)
                monitor.Stop();
        }
    }
}
