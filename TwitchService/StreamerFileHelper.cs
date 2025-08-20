using sblngavnav5X.Data;
using sblngavnav5X.TwitchService;
using TwitchLib.Api.Helix.Models.Users;

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
            catch (IndexOutOfRangeException e)
            {
                Console.WriteLine(e.Message);
                return -1;
            }

            DataBase.AddStreamer(streamer, streamerId);
            DataBase.DownloadStreamers();
            return 1;
        }

        public async Task<bool> TryRemoveStreamerAsync(string name)
        {
            string streamer = name.ToLower();
            string streamerId;

            if (!(_lsms.StreamList.Contains(streamer)))
                return false;

            try
            {
                streamerId = await TryVerifyStreamerAsync(name);
            }
            catch (IndexOutOfRangeException e)
            {
                Console.WriteLine(e.Message);
                return false;
            }

            DataBase.DeleteStreamer(streamer, streamerId);
            DataBase.DownloadStreamers();
            return true;
        }

        public async Task<string> TryVerifyStreamerAsync(string streamer)
        {
            try
            {
                List<string> tmp = new List<string>();
                tmp.Add(streamer);

                GetUsersResponse result = await _lsms.TwitchApi.Helix.Users.GetUsersAsync(logins: tmp, accessToken: _lsms.TwitchApi.Settings.AccessToken);

                return result.Users[0].Id;
            }
            catch (Exception e)
            {
                throw;
            }
        }
    }
}
