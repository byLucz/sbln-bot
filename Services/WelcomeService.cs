using Discord;
using Discord.WebSocket;
using sblngavnav5X.Data;

namespace sblngavnav5X.Services
{
    public class WelcomeService
    {
        private readonly DiscordSocketClient _client;

        public WelcomeService(DiscordSocketClient client)
        {
            _client = client;
            _client.UserJoined += OnUserJoined;
        }

        private async Task OnUserJoined(SocketGuildUser user)
        {
            var newbieRole = user.Guild.Roles.FirstOrDefault(r => r.Name == Utils.welcomeRole);
            if (newbieRole != null)
            {
                try { await user.AddRoleAsync(newbieRole); }
                catch (Exception ex) { await LoggingService.LogWarningAsync("WLCME", $"Роль {newbieRole.Name} не выдана на {user.Guild.Name}: {ex.Message}"); }
            }

            var channel = user.Guild.TextChannels.FirstOrDefault(c => c.Name == Utils.welcomeChannel);
            if (channel == null) return;

            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle($"Добро пожаловать, {user.Username}!")
                .WithDescription("Я сын гавна, больше про меня можешь узнать в команде -> **памаги**")
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .Build();

            await channel.SendMessageAsync(embed: embed);
        }
    }
}
