using Discord;
using Discord.WebSocket;
using sblngavnav6.Common;
using sblngavnav6.Data;

namespace sblngavnav6.Services
{
    public sealed class WelcomeService : IDisposable
    {
        private readonly DiscordSocketClient _client;
        private bool _disposed;

        public WelcomeService(DiscordSocketClient client)
        {
            _client = client;
            _client.UserJoined += OnUserJoined;
        }

        public Task StopAsync()
        {
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _client.UserJoined -= OnUserJoined;
        }

        private async Task OnUserJoined(SocketGuildUser user)
        {
            var gs = await DataBase.GetGuildSettings(user.Guild.Id);

            if (gs.WelcomeRoleId is ulong roleId)
            {
                var role = user.Guild.GetRole(roleId);
                if (role != null)
                    try { await user.AddRoleAsync(role); } catch { }
            }

            if (gs.WelcomeChannelId is not ulong chId)
                return;

            var channel = user.Guild.GetTextChannel(chId);
            if (channel == null) return;

            await channel.SendMessageAsync(embed: EmbedHandler.Build(new EmbedSpec
            {
                Title = user.Guild.Name,
                Description = string.IsNullOrWhiteSpace(gs.WelcomeMessage) ? $"{user.Mention}, добро пожаловать!" : gs.WelcomeMessage,
                Color = Color.Green,
                ThumbnailUrl = user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl()
            }));
        }
    }
}
