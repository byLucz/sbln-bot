using Discord;
using Discord.WebSocket;
using sblngavnav5X.Data;

namespace sblngavnav5X.Services
{
    public class WelcomeService
    {
        private const string DefaultMessage = "Добро пожаловать!";

        private readonly DiscordSocketClient _client;

        public WelcomeService(DiscordSocketClient client)
        {
            _client = client;
            _client.UserJoined += OnUserJoined;
        }

        private async Task OnUserJoined(SocketGuildUser user)
        {
            var gs = DataBase.GetGuildSettings(user.Guild.Id);

            var channel = gs.WelcomeChannelId is ulong chId
                ? user.Guild.GetTextChannel(chId)
                : user.Guild.SystemChannel ?? user.Guild.DefaultChannel;
            if (channel == null) return;

            if (gs.WelcomeRoleId is ulong roleId)
            {
                var role = user.Guild.GetRole(roleId);
                if (role != null)
                    try { await user.AddRoleAsync(role); } catch { }
            }

            var message = string.IsNullOrWhiteSpace(gs.WelcomeMessage) ? DefaultMessage : gs.WelcomeMessage;

            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle($"Добро пожаловать, {user.Username}!")
                .WithDescription(message)
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .Build();

            await channel.SendMessageAsync(embed: embed);
        }
    }
}
