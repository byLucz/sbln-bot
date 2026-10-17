using Discord;
using Discord.WebSocket;
using sblngavnav6.Core;
using sblngavnav6.Data;
using System.Collections.Concurrent;

namespace sblngavnav6.PPM
{
    public sealed class PpmPanels
    {
        private readonly ConcurrentDictionary<ulong, (ulong ChannelId, ulong MessageId)> _panels = new();

        private readonly DiscordSocketClient _client;
        private readonly PaginatorService _pager;

        public PpmPanels(DiscordSocketClient client, PaginatorService pager)
        {
            _client = client;
            _pager = pager;
        }

        public void Track(ulong ownerId, IMessage message)
        {
            if (message is null)
                return;

            _panels[ownerId] = (message.Channel.Id, message.Id);
        }

        public async Task RefreshAsync(string ownerId)
        {
            if (!ulong.TryParse(ownerId, out var id))
                return;

            await RefreshAsync(id).ConfigureAwait(false);
        }

        public async Task RefreshAsync(ulong ownerId)
        {
            if (!_panels.TryGetValue(ownerId, out var target))
                return;

            if (_client.GetChannel(target.ChannelId) is not IMessageChannel channel)
            {
                _panels.TryRemove(ownerId, out _);
                return;
            }

            IUserMessage message;
            try
            {
                message = await channel.GetMessageAsync(target.MessageId).ConfigureAwait(false) as IUserMessage;
            }
            catch (Exception)
            {
                message = null;
            }

            if (message is null)
            {
                _panels.TryRemove(ownerId, out _);
                return;
            }

            var boxes = await DataBase.GetUserPpmMailboxes(ownerId.ToString());

            try
            {
                await _pager.ReplaceAsync(
                    message,
                    PpmPanelBuilder.BuildRootPages(boxes),
                    ownerId,
                    decorate: PpmPanelBuilder.RootControls(boxes)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                _panels.TryRemove(ownerId, out _);
            }
        }
    }
}
