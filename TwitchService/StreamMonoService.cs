using Discord;
using Discord.WebSocket;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Games;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.Api.Services;
using TwitchLib.Api.Services.Events;
using TwitchLib.Api.Services.Events.LiveStreamMonitor;
using sblngavnav5X.Data;
using sblngavnav5X.Services;

namespace sblngavnav5X.TwitchService
{
    public class StreamMonoService : StreamMonoServiceBase
    {
        private readonly DiscordSocketClient _discord;
        private LiveStreamMonitorService _liveStreamMonitor;

        public StreamMonoService(DiscordSocketClient discord)
        {
            _discord = discord;

            UpdInt = Utils.streamUpdTime;
            NotifChannelName = Utils.streamNotifCh;

            TwitchAPI api = new TwitchAPI();
            api.Settings.ClientId = Utils.streamCid;
            api.Settings.AccessToken = Utils.streamAuth;
            TwitchApi = api;
        }

        public async Task CreateStreamMonoAsync()
        {
            if (_liveStreamMonitor != null)
                return;

            StreamModels = new Dictionary<string, StreamData>();
            GetStreamerList();
            await GetStreamerIdDictAsync();

            await LoggingService.LogInformationAsync("TTVLK", $"Кол-во серверов: {_discord.Guilds.Count}");

            List<SocketTextChannel> notifChannels = new();

            foreach (var guild in _discord.Guilds)
            {
                await LoggingService.LogInformationAsync("TTVLK", $"Сервера: {guild.Name}");

                var channel = guild.TextChannels.FirstOrDefault(x => x.Name.Contains(NotifChannelName));
                if (channel != null)
                    notifChannels.Add(channel);
            }

            StreamNotifChannels = notifChannels;

            if (StreamNotifChannels.Any())
                await LoggingService.LogInformationAsync("TTVLK", $"Кол-во каналов оповещений: {StreamNotifChannels.Count}");
            else
                await LoggingService.LogCriticalAsync("TTVLK", "Не найдено каналов оповещений");

            try
            {
                StreamProfileImages = await GetProfImgUrlsAsync(StreamIdList);
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
                await VerifyAndGetStreamIdAsync();
                CreationAttempts++;
                await CreateStreamMonoAsync();
                return;
            }

            try
            {
                _liveStreamMonitor = new LiveStreamMonitorService(TwitchApi, UpdInt, 100);
                _liveStreamMonitor.OnServiceTick += OnServiceTickEvent;
                _liveStreamMonitor.OnChannelsSet += OnChannelsSetEvent;
                _liveStreamMonitor.OnServiceStarted += OnServiceStartedEvent;
                _liveStreamMonitor.OnServiceStopped += OnServiceStoppedEvent;
                _liveStreamMonitor.OnStreamOnline += OnStreamOnlineEventAsync;
                _liveStreamMonitor.OnStreamOffline += OnStreamOfflineEvent;

                if (StreamIdList == null || !StreamIdList.Any())
                    throw new ArgumentException("StreamIdList пуст");

                _liveStreamMonitor.SetChannelsById(StreamIdList);
                _liveStreamMonitor.Start();
            }
            catch (ArgumentException e)
            {
                await LoggingService.LogInformationAsync("TTVLK", $"Лист стримеров пуст: {e.Message}");
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync("TTVLK", ex.Message);
            }

            await LoggingService.LogInformationAsync("TTVLK", $"Статус мониторинга - {_liveStreamMonitor?.Enabled ?? false}");
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
            if (StreamsOnline.Contains(e.Stream.UserId))
                return;

            var gameTemp = new List<string> { e.Stream.GameId };

            GetGamesResponse getGamesResponse;
            try
            {
                getGamesResponse = await TwitchApi.Helix.Games.GetGamesAsync(gameTemp);
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync("TTVLK", $"GameResponse: {ex.Message}");
                return;
            }

            try
            {
                UpdateLiveStreamModelsAsync(e.Stream, getGamesResponse);
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync("TTVLK", $"UpdateLiveStreams: {ex.Message}");
                return;
            }

            EmbedBuilder eb = CreateStreamerEmbed(StreamModels[e.Stream.UserId], e.Stream.ThumbnailUrl);

            foreach (var x in StreamNotifChannels)
                await x.SendMessageAsync($"@everyone, {e.Stream.UserName} сейчас стримит!", false, eb.Build());

            StreamsOnline.Add(e.Stream.UserId);

            if (StreamsOnline.Contains(e.Stream.UserId))
                await LoggingService.LogInformationAsync("TTVLK", $"{e.Stream.UserName} добавлен в лист отслеживания");
            else
                await LoggingService.LogCriticalAsync("TTVLK", $"Ошибка при добавлении {e.Stream.UserName}");
        }

        private async void OnStreamOfflineEvent(object sender, OnStreamOfflineArgs e)
        {
            bool removalBool = StreamsOnline.Remove(e.Stream.UserId);
            await Console.Out.WriteLineAsync($"Стример {e.Stream.UserName} оффлайн {removalBool}");
        }

        private async void OnChannelsSetEvent(object sender, OnChannelsSetArgs e)
        {
            if (_liveStreamMonitor.ChannelsToMonitor != null)
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
            var tmp = DataRoots.States.StreamerIds
                .Select(part => part.Split(':'))
                .Where(part => part.Length == 2)
                .ToDictionary(sp => sp[0], sp => sp[1]);

            StreamIds = tmp ?? new Dictionary<string, string>();
            StreamIdList = StreamIds.Values.ToList();

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

        private async Task<Dictionary<string, string>> GetProfImgUrlsAsync(List<string> streamIds)
        {
            Dictionary<string, string> profImages = new();

            if (!streamIds.Any())
                return profImages;

            GetUsersResponse usersResponse =
                await TwitchApi.Helix.Users.GetUsersAsync(streamIds, null, TwitchApi.Settings.AccessToken);

            foreach (var user in usersResponse.Users)
                profImages[user.Id] = user.ProfileImageUrl;

            return profImages;
        }

        private EmbedBuilder CreateStreamerEmbed(StreamData streamModel, string thumbnailUrl)
        {
            var a = new EmbedAuthorBuilder()
            {
                Name = streamModel.Stream,
                IconUrl = streamModel.Avatar
            };

            var b = new EmbedFooterBuilder()
            {
                Text = "sbln твич📺   ///   powered by TwitchLib",
            };

            var eb = new EmbedBuilder()
            {
                Footer = b,
                Author = a,
                Color = new Color(191, 0, 255),
                ImageUrl = thumbnailUrl.Replace("{width}", "1280").Replace("{height}", "720"),
                Title = streamModel.Title,
                Url = streamModel.Link,
            };

            eb.AddField(x =>
            {
                x.IsInline = true;
                x.Name = "**Категория:**";
                x.Value = streamModel.Game;
            });

            eb.AddField(x =>
            {
                x.IsInline = true;
                x.Name = "**Зрители:**";
                x.Value = streamModel.Viewers;
            });

            return eb;
        }

        public async Task VerifyAndGetStreamIdAsync()
        {
            Dictionary<string, string> streamsidsDict = new();
            List<string> verifiedStreams = new();
            List<string> tmp = new() { " " };

            foreach (string s in StreamList)
            {
                tmp[0] = s;

                try
                {
                    GetUsersResponse response =
                        await TwitchApi.Helix.Users.GetUsersAsync(logins: tmp, accessToken: TwitchApi.Settings.AccessToken);

                    if (response.Users.Length > 0)
                    {
                        streamsidsDict.Add(response.Users[0].Login, response.Users[0].Id);
                        verifiedStreams.Add(s);
                    }

                    await Task.Delay(5000);
                }
                catch (TwitchLib.Api.Core.Exceptions.InternalServerErrorException ex)
                {
                    await LoggingService.LogCriticalAsync("TTVLK", $"InternalService: {ex.Message}");
                }
            }

            await UpdateChannelsToMonitor();
        }

        public async Task UpdateChannelsToMonitor()
        {
            await GetStreamerIdDictAsync();

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

            await GetProfImgUrlsAsync(StreamIdList);
            GetStreamerList();
        }

        public bool StopLsm()
        {
            if (!_liveStreamMonitor.Enabled)
                return false;

            _liveStreamMonitor.Stop();
            return true;
        }

        public bool StartLsm()
        {
            if (_liveStreamMonitor.Enabled)
                return false;

            _liveStreamMonitor.Start();
            return true;
        }

        public string StatusLsm()
        {
            return _liveStreamMonitor.Enabled ? "Онлайн" : "Oффлайн";
        }
    }
}