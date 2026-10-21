using sblngavnav5X.Data;
using sblngavnav5X.TwitchService;
using TwitchLib.Api.Helix.Models.Users.GetUsers;

namespace sblngavnav5X.Services.Twitch
{
    public class StreamerFileHelper
    {
        private readonly StreamMonoService _lsms;

        public StreamerFileHelper(StreamMonoService lsms)
        {
            _lsms = lsms;
        }

        public async Task<int> TryAddStreamerAsync(string name)
        {
            string streamer = name.ToLower();
            string streamerId;

            if (_lsms.StreamList.Contains(streamer))
                return 0;

            try
            {
                streamerId = await TryVerifyStreamerAsync(name);
            }
            catch (IndexOutOfRangeException)
            {
                return -1;
            }

            DataBase.AddStreamer(streamer, streamerId);
            DataBase.DownloadStreamers();
            return 1;
        }

        public Task<bool> TryRemoveStreamerAsync(string name)
        {
            string streamer = name.ToLower();

            if (!_lsms.StreamList.Contains(streamer))
                return Task.FromResult(false);

            DataBase.DeleteStreamer(streamer);
            DataBase.DownloadStreamers();
            return Task.FromResult(true);
        }

        public async Task<string> TryVerifyStreamerAsync(string streamer)
        {
            List<string> tmp = new() { streamer };

            GetUsersResponse result =
                await _lsms.TwitchApi.Helix.Users.GetUsersAsync(logins: tmp, accessToken: _lsms.TwitchApi.Settings.AccessToken);

            return result.Users[0].Id;
        }
    }
}
