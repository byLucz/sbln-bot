using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Games;
using TwitchLib.Api.Helix.Models.Users;
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

            NotifChannelName = "twitch";

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
            await Task.Run(GetStreamerList);
            await GetStreamerIdDictAsync();

            await LoggingService.LogInformationAsync("TTV", $"Кол-во серверов: {_discord.Guilds.Count}");

            List<SocketTextChannel> notifChannels = new List<SocketTextChannel>();
            IEnumerator<SocketGuild> eguilds = _discord.Guilds.GetEnumerator();

            try
            {
                eguilds.MoveNext();
                while (eguilds.Current != null)
                {
                    int currentPos = 0;

                    await LoggingService.LogInformationAsync("TTV", $"Сервера: {eguilds.Current.Name}");

                    IEnumerator<SocketTextChannel> echannels = eguilds.Current.TextChannels.GetEnumerator();

                    try
                    {
                        echannels.MoveNext();
                        while (currentPos != eguilds.Current.TextChannels.Count - 1)
                        {
                            currentPos++;
                            if (echannels.Current != null && echannels.Current.Name.Contains(NotifChannelName))
                            {
                                notifChannels.Add(echannels.Current);
                                break;
                            }
                            echannels.MoveNext();
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                    finally
                    {
                        echannels.Dispose();
                    }
                    eguilds.MoveNext();
                }
            }
            catch (Exception ex)
            {
            }
            finally
            {
                eguilds.Dispose();
            }

            StreamNotifChannels = notifChannels;

            if (StreamNotifChannels.Any())
            {
                await LoggingService.LogInformationAsync("TTV", $"Кол-во каналов оповещений: {StreamNotifChannels.Count()}");
            }
            else
            {
                await LoggingService.LogCriticalAsync("TTV", $"Не найдено каналов оповещений");
            }

            try
            {
                StreamProfileImages = await GetProfImgUrlsAsync(StreamIdList);
            }
            catch (TwitchLib.Api.Core.Exceptions.InternalServerErrorException ex)
            {
                if (CreationAttempts == 1)
                {
                    await LoggingService.LogCriticalAsync("TTV", $"Максимальное число попыток достигнуто, StreamMonitor выключен");
                    CreationAttempts = 0;
                    return;
                }
                await LoggingService.LogCriticalAsync("TTV", $"{ex.GetType().Name} - Попытка {CreationAttempts}: Ошибка в загрузке профилей, повторная попытка...");
                await VerifyAndGetStreamIdAsync();
                CreationAttempts++;
                await CreateStreamMonoAsync();
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
                {
                    throw new ArgumentException("StreamIdList пуст");
                }

                _liveStreamMonitor.SetChannelsById(StreamIdList);
                _liveStreamMonitor.Start();
            }
            catch (ArgumentException e)
            {
                await LoggingService.LogInformationAsync("TTV", $"Лист стримеров пуст: {e.Message}");
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync("TTV", $"{ex.Message}");
            }

            await LoggingService.LogInformationAsync("TTV", $"Статус мониторинга - {_liveStreamMonitor?.Enabled ?? false}");
        }

        private void OnServiceTickEvent(object sender, OnServiceTickArgs e)
        {
        }

        private static async void OnServiceStartedEvent(object sender, OnServiceStartedArgs e)
        {
            await LoggingService.LogInformationAsync("TTV", $"Мониторинг успешно запущен!");
        }

        private static async void OnServiceStoppedEvent(object sender, OnServiceStoppedArgs e)
        {
            await LoggingService.LogInformationAsync("TTV", $"Мониторинг остановлен...");
        }

        private async void OnStreamOnlineEventAsync(object sender, OnStreamOnlineArgs e)
        {
            if (StreamsOnline.Contains(e.Stream.UserId))
                return;

            var gameTemp = new List<string>
            {
                e.Stream.GameId
            };

            GetGamesResponse getGamesResponse = new GetGamesResponse();
            try
            {
                getGamesResponse = await TwitchApi.Helix.Games.GetGamesAsync(gameTemp);
            }
            catch (Exception ex)
            {

                await LoggingService.LogCriticalAsync("TTV", $"GameResponse: {ex.Message}");
                return;
            }

            try
            {
                UpdateLiveStreamModelsAsync(e.Stream, getGamesResponse);
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync("TTV", $"UpdateLiveStreams: {ex.Message}");
                return;
            }

            EmbedBuilder eb = CreateStreamerEmbed(StreamModels[e.Stream.UserId], e.Stream.ThumbnailUrl);

            foreach (var x in StreamNotifChannels)
            {
                await x.SendMessageAsync($"@everyone, {e.Stream.UserName} сейчас стримит!", false, eb.Build());
            }

            StreamsOnline.Add(e.Stream.UserId);
            if (StreamsOnline.Contains(e.Stream.UserId))
            {
                await LoggingService.LogInformationAsync("TTV", $"{e.Stream.UserName} добавлен в лист отслеживания");
            }
            else
            {
                await LoggingService.LogCriticalAsync("TTV", $"Ошибка при добавлении {e.Stream.UserName}");
            }
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
                await LoggingService.LogInformationAsync("TTV", $"Каналы загружены");

                return;
            }
            await LoggingService.LogCriticalAsync("TTV", $"Каналы не настроены");
        }

        private async void GetStreamerList()
        {
            List<string> tmp = DataBase.streamers;
            StreamList = tmp ?? new List<string>();
        }

        private async Task GetStreamerIdDictAsync()
        {
            var tmp = DataBase.streamerIds.Select(part => part.Split(':')).Where(part => part.Length == 2).ToDictionary(sp => sp[0], sp => sp[1]);
            StreamIds = tmp ?? new Dictionary<string, string>();
            StreamIdList = StreamIds != null ? StreamIds.Values.AsEnumerable().ToList() : new List<string>();

            await LoggingService.LogInformationAsync("TTV", $"ТвичМонитор включен");
        }

        private void UpdateLiveStreamModelsAsync(TwitchLib.Api.Helix.Models.Streams.Stream twitchStream,
            GetGamesResponse game)
        {
            string gameName = game.Games.Length != 0 ? game.Games[0].Name : "Неизвестна";

            StreamData streamModel = new StreamData()
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
            Dictionary<string, string> profImages = new Dictionary<string, string>();

            if (!streamIds.Any())
                return profImages;

            GetUsersResponse usersResponse = await TwitchApi.Helix.Users.GetUsersAsync(streamIds, null, TwitchApi.Settings.AccessToken);

            foreach (var user in usersResponse.Users)
            {
                profImages.Add(user.Id, user.ProfileImageUrl);
            }

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
            Dictionary<string, string> streamsidsDict = new Dictionary<string, string>();
            List<string> verifiedStreams = new List<string>();
            List<string> tmp = new List<string>()
                { " " };

            foreach (string s in StreamList)
            {
                tmp[0] = s;
                try
                {
                    GetUsersResponse response = await TwitchApi.Helix.Users.GetUsersAsync(logins: tmp, accessToken: TwitchApi.Settings.AccessToken);
                    streamsidsDict.Add(response.Users[0].Login, response.Users[0].Id);
                    verifiedStreams.Add(s);
                    Thread.Sleep(5000);
                }
                catch (TwitchLib.Api.Core.Exceptions.InternalServerErrorException ex)
                {
                    await LoggingService.LogCriticalAsync("TTV", $"InternalService: {ex.Message}");
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
                await LoggingService.LogCriticalAsync("TTV", $"Argument: {ex.Message}");
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
