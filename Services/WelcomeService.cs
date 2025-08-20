using Discord;
using Discord.WebSocket;

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

            var channel = user.Guild.TextChannels.FirstOrDefault(c => c.Name == "mainlobby");
            if (channel == null) return;

            var newbieRole = user.Guild.Roles.FirstOrDefault(r => r.Name == "Челик");
            if (newbieRole != null)
            {
                await user.AddRoleAsync(newbieRole);
            }

            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle($"Добро пожаловать, {user.Username}!")
                .WithDescription("Я сын гавна, а это официальное представительсво Lois Media в мире Дискордии, устраивайся поудобнее, еп <:Lois:754018858431545375>")
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .Build();

            await channel.SendMessageAsync(embed: embed);
        }
    }
}
