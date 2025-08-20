using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace sblngavnav5X.Core
{
    public sealed class KumaReporter
    {
        private readonly DiscordSocketClient _client;
        private readonly HttpClient _http = new();
        private readonly string _pushUrl;
        private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(60));

        public KumaReporter(DiscordSocketClient client, string pushUrl)
        {
            _client = client; _pushUrl = pushUrl;
            _client.Ready += () => Push("up", "ready");
            _client.Connected += () => Push("up", "connected");
            _client.Disconnected += ex => Push("down", "disconnected");
            _ = Loop();
        }

        private async Task Loop()
        {
            while (await _timer.WaitForNextTickAsync())
                await Push("up", "heartbeat");
        }

        private Task Push(string status, string msg)
        {
            var ping = _client.Latency;
            var url = $"{_pushUrl}?status={status}&msg={Uri.EscapeDataString(msg)}&ping={ping}";
            return _http.GetAsync(url);
        }

    }
}