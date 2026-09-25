using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Collections.Concurrent;

namespace sblngavnav6.Core
{
    public sealed class PaginatorService : IDisposable, IAsyncDisposable
    {
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ViewLifetime = TimeSpan.FromMinutes(15);

        private readonly record struct View(
            IReadOnlyList<Embed> Pages,
            int Page,
            ulong? OwnerId,
            DateTimeOffset At,
            Action<ComponentBuilder, int> Decorate);

        private readonly ConcurrentDictionary<ulong, View> _views = new();
        private CancellationTokenSource _cleanupCts;
        private Task _cleanupTask = Task.CompletedTask;
        private bool _disposed;

        public void StartCleanup(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cleanupCts != null) return;
            _cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cleanupTask = CleanupLoopAsync(_cleanupCts.Token);
        }

        public async Task StopCleanupAsync()
        {
            if (_disposed || _cleanupCts == null) return;
            await _cleanupCts.CancelAsync();
            await _cleanupTask;
        }

        private async Task CleanupLoopAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(CleanupInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    var cut = DateTimeOffset.UtcNow - ViewLifetime;
                    foreach (var kv in _views.ToArray())
                        if (kv.Value.At < cut)
                            _views.TryRemove(kv);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }

        public async Task<IUserMessage> SendAsync(
            IMessageChannel channel,
            IReadOnlyList<Embed> pages,
            ulong? ownerId = null,
            int startPage = 0,
            Action<ComponentBuilder, int> decorate = null)
        {
            ArgumentNullException.ThrowIfNull(pages);
            if (pages.Count == 0)
                throw new ArgumentException("Нечего показывать: список страниц пуст", nameof(pages));

            var single = pages.Count == 1;
            startPage = Math.Clamp(startPage, 0, pages.Count - 1);

            var components = (single && decorate == null) ? null : Build(startPage, pages.Count, decorate);

            var msg = await channel.SendMessageAsync(embed: pages[startPage], components: components);

            if (!single || decorate != null)
                _views[msg.Id] = new View(pages, startPage, ownerId, DateTimeOffset.UtcNow, decorate);

            return msg;
        }

        public FlipOutcome TryPrepareFlip(ulong messageId, ulong userId, int target, out Embed embed, out MessageComponent components)
        {
            embed = null;
            components = null;

            if (!_views.TryGetValue(messageId, out var view))
                return FlipOutcome.Expired;
            if (view.OwnerId is ulong owner && owner != userId)
                return FlipOutcome.Forbidden;

            target = Math.Clamp(target, 0, view.Pages.Count - 1);
            if (target == view.Page)
                return FlipOutcome.Unchanged;

            embed = view.Pages[target];
            components = Build(target, view.Pages.Count, view.Decorate);
            return FlipOutcome.Ready;
        }

        public void CommitFlip(ulong messageId, int page)
        {
            if (!_views.TryGetValue(messageId, out var view))
                return;

            page = Math.Clamp(page, 0, view.Pages.Count - 1);
            _views.TryUpdate(messageId, view with { Page = page, At = DateTimeOffset.UtcNow }, view);
        }

        public MessageComponent BuildControls(Action<ComponentBuilder, int> controls, int page = 0)
            => Build(page, total: 1, controls);

        public async Task ModifyAsync(
            IUserMessage message,
            Embed embed = null,
            Action<ComponentBuilder, int> controls = null,
            bool clearControls = false)
        {
            ArgumentNullException.ThrowIfNull(message);

            await message.ModifyAsync(properties =>
            {
                if (embed is not null)
                    properties.Embed = embed;

                if (clearControls)
                    properties.Components = BuildControls(null);
                else if (controls is not null)
                    properties.Components = BuildControls(controls);
            });
        }

        private static MessageComponent Build(int page, int total, Action<ComponentBuilder, int> decorate)
        {
            var b = new ComponentBuilder();
            decorate?.Invoke(b, page);
            if (total > 1)
                b.AddPager(page, total, "pgr_page", 0);
            return b.Build();
        }

        public async ValueTask DisposeAsync()
        {
            try { await StopCleanupAsync(); }
            finally { Dispose(); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _cleanupCts?.Cancel(); }
            catch (ObjectDisposedException) { }
            _cleanupCts?.Dispose();

            _views.Clear();
        }
    }

    public enum FlipOutcome
    {
        Ready,
        Unchanged,
        Forbidden,
        Expired
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

            if (!int.TryParse(pageRaw, out var page))
            {
                await c.DeferAsync();
                return;
            }

            var outcome = _pager.TryPrepareFlip(c.Message.Id, Context.User.Id, page, out var embed, out var components);

            switch (outcome)
            {
                case FlipOutcome.Ready:
                    await c.UpdateAsync(m => { m.Embed = embed; m.Components = components; });
                    _pager.CommitFlip(c.Message.Id, page);
                    break;

                case FlipOutcome.Expired:
                    await c.RespondAsync("Панель устарела, вызови команду заново", ephemeral: true);
                    break;

                case FlipOutcome.Forbidden:
                    await c.RespondAsync("Это не твоя панель", ephemeral: true);
                    break;

                default:
                    await c.DeferAsync();
                    break;
            }
        }
    }
}
