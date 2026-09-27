using sblngavnav6.Data;
using sblngavnav6.TwitchService;

namespace sblngavnav6.Services.Twitch
{
    public enum StreamerChange
    {
        Done,
        AlreadyThere,
        Missing,
        NotFound
    }

    public sealed class StreamerFileHelper
    {
        private readonly StreamMonoService _lsms;

        public StreamerFileHelper(StreamMonoService lsms)
        {
            _lsms = lsms;
        }

        public async Task<StreamerChange> TryAddStreamerAsync(string name)
        {
            var streamer = name.Trim().ToLowerInvariant();

            if (_lsms.StreamList.Contains(streamer))
                return StreamerChange.AlreadyThere;

            var streamerId = await TryVerifyStreamerAsync(streamer).ConfigureAwait(false);

            if (streamerId is null)
                return StreamerChange.NotFound;

            DataBase.AddStreamer(streamer, streamerId);
            DataBase.DownloadStreamers();

            return StreamerChange.Done;
        }

        public async Task<StreamerChange> TryRemoveStreamerAsync(string name)
        {
            var streamer = name.Trim().ToLowerInvariant();

            if (!_lsms.StreamList.Contains(streamer))
                return StreamerChange.Missing;

            var streamerId = await TryVerifyStreamerAsync(streamer).ConfigureAwait(false);

            if (streamerId is null)
                return StreamerChange.NotFound;

            DataBase.DeleteStreamer(streamer, streamerId);
            DataBase.DownloadStreamers();

            return StreamerChange.Done;
        }

        public async Task<string> TryVerifyStreamerAsync(string streamer)
        {
            try
            {
                var result = await _lsms.TwitchApi.Helix.Users
                    .GetUsersAsync(logins: [streamer], accessToken: _lsms.TwitchApi.Settings.AccessToken)
                    .ConfigureAwait(false);

                return result?.Users is { Length: > 0 } users ? users[0].Id : null;
            }
            catch (Exception ex)
            {
                await LoggingService.LogWarningAsync("TTVLK", $"Проверка стримера {streamer} не удалась: {ex.Message}").ConfigureAwait(false);
                return null;
            }
        }
    }
}
