using Discord;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using static sblngavnav6.Common.CommonUtils.Text;

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

        private static string FailureReason(TrackLoadResult result) =>
            Audio8Embeds.DescribeFailure(result.Exception?.Message);

        private async Task<List<LavalinkTrack>> CollectPicksAsync(string rawText, CancellationToken cancellationToken)
        {
            var picks = new List<LavalinkTrack>(Audio8Constants.MaxSearchPicks);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (prefix, _) in Audio8Query.Fallbacks)
            {
                if (picks.Count >= Audio8Constants.MaxSearchPicks)
                    return picks;

                await TryAddAsync(prefix + rawText).ConfigureAwait(false);
            }

            foreach (var variant in Audio8Query.Variants(rawText))
            {
                if (picks.Count >= Audio8Constants.MaxSearchPicks)
                    break;

                await TryAddAsync(Audio8Query.YouTubePrefix + variant).ConfigureAwait(false);
            }

            return picks;

            async Task TryAddAsync(string identifier)
            {
                var track = await _service.LoadFirstAsync(identifier, cancellationToken).ConfigureAwait(false);
                if (track is not null && seen.Add(Audio8Service.TrackKey(track)))
                    picks.Add(track);
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
                requestedByUserId).ConfigureAwait(false);

            _states.AddPick(new Audio8PickState(guildId, message.Id, requestedByUserId, picks, DateTimeOffset.UtcNow));

            return true;
        }
    }
}
