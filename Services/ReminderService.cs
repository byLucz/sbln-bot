using Discord;
using Discord.WebSocket;

namespace sblngavnav5X.Services
{
    public static class ReminderService
	{
		public static async Task RemindAsyncSeconds(SocketUser guild, int time, string msg)
		{
			int convert = (int) TimeSpan.FromSeconds(time).TotalMilliseconds;
			string timenow = DateTime.Now.ToString("hh:mm:ss tt");

            await Task.Delay(convert);

			IDMChannel dm = await guild.CreateDMChannelAsync();

			EmbedBuilder embed = new EmbedBuilder();
			embed.WithTitle("sbln напоминалка👽");
			embed.WithDescription(msg);
			embed.WithFooter($"была поставлена в {timenow}", guild.GetAvatarUrl());

			await dm.SendMessageAsync("", false, embed.Build());
		}
	}
}