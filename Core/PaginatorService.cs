using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Collections.Concurrent;

namespace sblngavnav5X.Core
{
    public sealed class PaginatorService
    {
        private readonly record struct View(IReadOnlyList<Embed> Pages, int Page, ulong? OwnerId, DateTimeOffset At);

        private readonly ConcurrentDictionary<ulong, View> _views = new();

        public PaginatorService()
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(5));
                    var cut = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(15);
                    foreach (var kv in _views.ToArray())
                        if (kv.Value.At < cut)
                            _views.TryRemove(kv.Key, out _);
                }
            });
        }

        public async Task<IUserMessage> SendAsync(IMessageChannel channel, IReadOnlyList<Embed> pages, ulong? ownerId = null, int startPage = 0)
        {
            var single = pages.Count <= 1;
            startPage = Math.Clamp(startPage, 0, pages.Count - 1);

            var msg = await channel.SendMessageAsync(
                embed: pages[startPage],
                components: single ? null : Build(startPage, pages.Count));

            if (!single)
                _views[msg.Id] = new View(pages, startPage, ownerId, DateTimeOffset.UtcNow);

            return msg;
        }

        public bool TryFlip(ulong messageId, ulong userId, int target, out Embed embed, out MessageComponent components)
        {
            embed = null;
            components = null;

            if (!_views.TryGetValue(messageId, out var v))
                return false;
            if (v.OwnerId is ulong owner && owner != userId)
                return false;

            target = Math.Clamp(target, 0, v.Pages.Count - 1);
            if (target == v.Page)
                return false;

            _views[messageId] = v with { Page = target, At = DateTimeOffset.UtcNow };
            embed = v.Pages[target];
            components = Build(target, v.Pages.Count);
            return true;
        }

        private static MessageComponent Build(int page, int total)
            => new ComponentBuilder().AddPager(page, total, "pgr_page", 0).Build();
    }

    public class PaginatorInteractions : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly PaginatorService _pager;

        public PaginatorInteractions(PaginatorService pager)
        {
            _pager = pager;
        }

        [ComponentInteraction("pgr_page:*")]
        public async Task Flip(string pageRaw)
        {
            if (Context.Interaction is not SocketMessageComponent c)
                return;

            if (int.TryParse(pageRaw, out var page) &&
                _pager.TryFlip(c.Message.Id, Context.User.Id, page, out var embed, out var components))
            {
                await c.UpdateAsync(m => { m.Embed = embed; m.Components = components; });
            }
            else
            {
                await c.DeferAsync();
            }
        }
    }
}
