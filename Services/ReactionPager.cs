using Discord;
using Discord.WebSocket;
using System.Collections.Concurrent;

namespace sblngavnav5X.Services
{
    public sealed class ReactionPager
    {
        private static readonly Emoji Prev = new("◀");
        private static readonly Emoji Next = new("▶");
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

        private sealed class PageState
        {
            public List<Embed> Pages { get; init; }
            public int Page { get; set; }
            public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;
            public DateTime LastClickUtc { get; set; }
        }

        private readonly DiscordSocketClient _client;
        private readonly ConcurrentDictionary<ulong, PageState> _states = new();

        public ReactionPager(DiscordSocketClient client)
        {
            _client = client;
            _client.ReactionAdded += OnReactionAddedAsync;
        }

        public async Task SendAsync(IMessageChannel channel, List<Embed> pages, int startPage = 0)
        {
            if (pages.Count == 0)
                return;

            foreach (var kv in _states.ToArray())
                if (DateTime.UtcNow - kv.Value.CreatedAtUtc > Lifetime)
                    _states.TryRemove(kv.Key, out _);

            var page = Math.Clamp(startPage, 0, pages.Count - 1);
            var msg = await channel.SendMessageAsync(embed: pages[page]);
            if (pages.Count == 1)
                return;

            _states[msg.Id] = new PageState { Pages = pages, Page = page };
            await msg.AddReactionsAsync(new IEmote[] { Prev, Next });
        }

        private async Task OnReactionAddedAsync(
            Cacheable<IUserMessage, ulong> message,
            Cacheable<IMessageChannel, ulong> channel,
            SocketReaction reaction)
        {
            if (reaction.UserId == _client.CurrentUser.Id)
                return;

            if (!_states.TryGetValue(reaction.MessageId, out var state))
                return;

            var step = reaction.Emote.Name == Prev.Name ? -1 : reaction.Emote.Name == Next.Name ? 1 : 0;
            if (step == 0)
                return;

            try
            {
                var msg = await message.GetOrDownloadAsync();
                if (msg is null)
                    return;

                var user = reaction.User.IsSpecified ? reaction.User.Value : await msg.Channel.GetUserAsync(reaction.UserId);
                if (user != null)
                    try { await msg.RemoveReactionAsync(reaction.Emote, user); } catch { }

                Embed embed;
                lock (state)
                {
                    var now = DateTime.UtcNow;
                    if ((now - state.LastClickUtc).TotalMilliseconds < 700)
                        return;
                    state.LastClickUtc = now;

                    var next = Math.Clamp(state.Page + step, 0, state.Pages.Count - 1);
                    if (next == state.Page)
                        return;
                    state.Page = next;
                    embed = state.Pages[next];
                }

                await msg.ModifyAsync(m => m.Embed = embed);
            }
            catch (Exception ex)
            {
                await LoggingService.LogWarningAsync("PAGER", $"Ошибка пагинации: {ex.Message}");
            }
        }
    }
}
