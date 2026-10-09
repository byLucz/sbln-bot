using Discord;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;

namespace sblngavnav6.Audio8
{
    internal sealed class Audio8SearchService
    {
        private readonly Audio8Service _service;
        private readonly Audio8MessageStates _states;

        public Audio8SearchService(Audio8Service service, Audio8MessageStates states)
        {
            _service = service;
            _states = states;
        }

        public async Task<bool> TryRecoverAsync(
            ulong guildId,
            ITextChannel channel,
            ulong requestedByUserId,
            string normalizedQuery,
            TrackLoadResult initial,
            CancellationToken cancellationToken = default)
        {
            var (sourceName, isUrl) = Audio8Query.Detect(normalizedQuery);

            if (isUrl)
            {
                await _service.SendAsync(channel, await Audio8Embeds.Error(
                    sourceName,
                    initial.IsFailed
                        ? $"источник отказал: {FailureReason(initial)}"
                        : "по ссылке ничего не открылось")).ConfigureAwait(false);

                return true;
            }

            var rawText = Audio8Query.StripPrefix(normalizedQuery);

            if (!string.IsNullOrWhiteSpace(rawText))
            {
                var picks = await CollectPicksAsync(rawText, cancellationToken).ConfigureAwait(false);

                if (await PresentAsync(guildId, channel, requestedByUserId, picks, "бро, там не нашел, но есть интересное здесь:").ConfigureAwait(false))
                    return true;
            }

            if (!initial.IsFailed)
                return false;

            await _service.SendAsync(channel, await Audio8Embeds.Error(
                sourceName,
                $"источник недоступен: {FailureReason(initial)}")).ConfigureAwait(false);

            return true;
        }

        public async Task<bool> OfferAlternativesAsync(
            ulong guildId,
            ITextChannel channel,
            LavalinkTrack failed,
            CancellationToken cancellationToken = default)
        {
            var rawText = BuildQuery(failed);

            if (string.IsNullOrWhiteSpace(rawText))
                return false;

            var picks = await CollectPicksAsync(rawText, cancellationToken).ConfigureAwait(false);
            var failedKey = Audio8Service.TrackKey(failed);

            picks.RemoveAll(track => Audio8Service.TrackKey(track) == failedKey);

            return await PresentAsync(
                guildId,
                channel,
                requestedByUserId: 0,
                picks,
                "этот трек не играется, но есть похожее:").ConfigureAwait(false);
        }

        private static string BuildQuery(LavalinkTrack track)
        {
            var title = track?.Title?.Trim();

            if (string.IsNullOrWhiteSpace(title))
                return null;

            var author = track.Author?.Trim();

            return string.IsNullOrWhiteSpace(author) || title.Contains(author, StringComparison.OrdinalIgnoreCase)
                ? title
                : $"{title} {author}";
        }

        private static string FailureReason(TrackLoadResult result) =>
            Audio8Embeds.DescribeFailure(result.Exception?.Message);

        private async Task<List<LavalinkTrack>> CollectPicksAsync(string rawText, CancellationToken cancellationToken)
        {
            var picks = new List<LavalinkTrack>(Audio8Constants.MaxSearchPicks);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var sources = Audio8Query.Fallbacks
                .Select(fallback => _service.LoadFirstAsync(fallback.Prefix + rawText, cancellationToken));

            Absorb(await Task.WhenAll(sources).ConfigureAwait(false));

            if (picks.Count >= Audio8Constants.MaxSearchPicks)
                return picks;

            var variants = Audio8Query.Variants(rawText)
                .Take(Audio8Constants.MaxSearchVariants)
                .Select(variant => _service.LoadFirstAsync(Audio8Query.YouTubePrefix + variant, cancellationToken));

            Absorb(await Task.WhenAll(variants).ConfigureAwait(false));

            return picks;

            void Absorb(IEnumerable<LavalinkTrack> found)
            {
                foreach (var track in found)
                {
                    if (picks.Count >= Audio8Constants.MaxSearchPicks)
                        return;

                    if (track is not null && seen.Add(Audio8Service.TrackKey(track)))
                        picks.Add(track);
                }
            }
        }

        private async Task<bool> PresentAsync(
            ulong guildId,
            ITextChannel channel,
            ulong requestedByUserId,
            List<LavalinkTrack> picks,
            string header)
        {
            if (picks.Count == 0)
                return false;

            if (picks.Count > Audio8Constants.MaxSearchPicks)
                picks = picks.Take(Audio8Constants.MaxSearchPicks).ToList();

            var message = await _service.SendWithControlsAsync(
                channel,
                await Audio8Embeds.Picks(picks, header).ConfigureAwait(false),
                Audio8Controls.Picks(picks.Count),
                requestedByUserId,
                Audio8Scopes.Picks(guildId)).ConfigureAwait(false);

            _states.AddPick(new Audio8PickState(guildId, message.Id, requestedByUserId, picks, DateTimeOffset.UtcNow));

            return true;
        }
    }
}
