using Discord;
using Discord.WebSocket;

namespace sblngavnav5X.Services
{
    public static class ReminderService
	{
        public const int MaxSeconds = 7 * 24 * 60 * 60;

		public static async Task RemindAsyncSeconds(SocketUser user, IMessageChannel origin, int time, string msg)
		{
			string timenow = DateTime.Now.ToString("hh:mm:ss tt");

            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(time, 1, MaxSeconds)));

			EmbedBuilder embed = new EmbedBuilder();
			embed.WithTitle("sbln напоминалка👽");
			embed.WithDescription(msg);
			embed.WithFooter($"была поставлена в {timenow}", user.GetAvatarUrl());

            try
            {
                IDMChannel dm = await user.CreateDMChannelAsync();
                await dm.SendMessageAsync("", false, embed.Build());
            }
            catch (Discord.Net.HttpException ex) when (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
            {
                await origin.SendMessageAsync(user.Mention, embed: embed.Build());
            }
		}
	}
}
