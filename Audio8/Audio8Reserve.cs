using Lavalink4NET.Tracks;
using sblngavnav6.Common;
using sblngavnav6.Data;
using sblngavnav6.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace sblngavnav6.Audio8
{
    internal sealed class Audio8Reserve
    {
        private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(12);
        private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(5);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Audio8Service _service;

        public Audio8Reserve(IHttpClientFactory httpClientFactory, Audio8Service service)
        {
            _httpClientFactory = httpClientFactory;
            _service = service;
        }

        private static Uri BaseUri => new UriBuilder("http", Global.Vars.Cfg.lavaYtAgent.Trim(), Global.Vars.Cfg.lavaYtAgentPort).Uri;

        public static bool Configured =>
            !string.IsNullOrWhiteSpace(Global.Vars.Cfg.lavaYtAgent) && !string.IsNullOrWhiteSpace(Global.Vars.Cfg.remnaAgentToken);

        public static bool Covers(LavalinkTrack track) =>
            track is not null && track.SourceName is not ("http" or "local" or "soundcloud");

        public async Task<string> StatusAsync(CancellationToken cancellationToken = default)
        {
            if (!Configured)
                return "`выкл`, не задан Lava:YtAgent";

            var baseUri = BaseUri;
            const string route = "/health";
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var response = await CommonUtils.Web.SendAsync(
                _httpClientFactory, "YtAgent", baseUri.ToString(), HttpMethod.Get, route, HealthTimeout,
                request => CommonUtils.Web.SignAgent(request, baseUri, route, Global.Vars.Cfg.remnaAgentToken),
                cancellationToken).ConfigureAwait(false);

            if (!response.Ok)
                return response.Failure == HttpRequestError.ResponseEnded
                    ? "⚠️ неверный токен, сбиты часы или агент не обновлён"
                    : $"❌ {CommonUtils.Web.BriefError(response.Error, 60)}";

            try
            {
                var info = JsonNode.Parse(response.Body);
                var cache = info?["cache"]?.GetValue<long>() ?? 0;
                var limit = info?["limit"]?.GetValue<long>() ?? 0;

                return $"✅ на связи, `{watch.ElapsedMilliseconds}ms`\n" +
                       $"yt-dlp `{info?["ytdlp"]?.GetValue<string>() ?? "?"}`\n" +
                       $"кеш `{CommonUtils.Text.Bytes(cache)}` / `{CommonUtils.Text.Bytes(limit)}`\n" +
                       $"cookies: `{(info?["cookies"]?.GetValue<bool>() == true ? "есть, 18+ доступны" : "нет")}`";
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                return "⚠️ кривой ответ /health";
            }
        }

        public async Task<LavalinkTrack> ResolveAsync(string query, CancellationToken cancellationToken = default)
        {
            if (!Configured || string.IsNullOrWhiteSpace(query))
                return null;

            var token = Global.Vars.Cfg.remnaAgentToken;
            var baseUri = BaseUri;
            var route = $"/resolve?q={Uri.EscapeDataString(query.Trim())}";

            var response = await CommonUtils.Web.SendAsync(
                _httpClientFactory, "YtAgent", baseUri.ToString(), HttpMethod.Get, route, ResolveTimeout,
                request => CommonUtils.Web.SignAgent(request, baseUri, route, token),
                cancellationToken).ConfigureAwait(false);

            if (!response.Ok)
            {
                await LoggingService.LogWarningAsync(Audio8Constants.LogSource,
                    $"Резерв не нашёл \"{query}\": {response.Error} {CommonUtils.Text.Truncate(response.Body, 200)}".TrimEnd()).ConfigureAwait(false);
                return null;
            }

            JsonNode info;
            try { info = JsonNode.Parse(response.Body); }
            catch (JsonException ex)
            {
                await LoggingService.LogWarningAsync(Audio8Constants.LogSource, $"Резерв ответил кривым JSON: {ex.Message}").ConfigureAwait(false);
                return null;
            }

            var id = info?["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id))
                return null;

            var expires = DateTimeOffset.UtcNow.Add(LinkLifetime).ToUnixTimeSeconds().ToString();
            var path = $"/audio/{Uri.EscapeDataString(id)}";
            var audio = new Uri(baseUri, $"{path}?exp={expires}&sig={CommonUtils.Web.AgentSignature(token, path, expires)}");

            var track = await _service.LoadFirstAsync(audio.ToString(), cancellationToken).ConfigureAwait(false);
            if (track is null)
            {
                await LoggingService.LogWarningAsync(Audio8Constants.LogSource, $"Lavalink не открыл резервный трек {id}").ConfigureAwait(false);
                return null;
            }

            return track with
            {
                Title = info["title"]?.GetValue<string>() is { Length: > 0 } title ? title : track.Title,
                Author = info["uploader"]?.GetValue<string>() is { Length: > 0 } author ? author : track.Author,
                Duration = info["duration"] is JsonValue duration && duration.TryGetValue<double>(out var seconds) ? TimeSpan.FromSeconds(seconds) : track.Duration,
                Uri = Uri.TryCreate(info["url"]?.GetValue<string>(), UriKind.Absolute, out var page) ? page : track.Uri,
                ArtworkUri = Uri.TryCreate(info["thumbnail"]?.GetValue<string>(), UriKind.Absolute, out var artwork) ? artwork : track.ArtworkUri
            };
        }
    }
}
