using Discord.WebSocket;
using sblngavnav6.Common;

namespace sblngavnav6.Services
{
    public static class ReminderService
    {
        public static async Task RemindAsyncSeconds(SocketUser user, int seconds, string message, CancellationToken cancellationToken = default)
        {
            var delay = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, (int)TimeSpan.FromDays(1).TotalSeconds));
            var placed = DateTimeOffset.Now;

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            var dm = await user.CreateDMChannelAsync().ConfigureAwait(false);

            await dm.SendMessageAsync(embed: EmbedHandler.Build(new EmbedSpec
            {
                Title = "sbln напоминалка👽",
                Description = message,
                Color = Discord.Color.Teal,
                Footer = $"поставлена в {placed:HH:mm:ss} / прошло {CommonUtils.Time.FormatTime(delay)}",
                FooterIconUrl = user.GetAvatarUrl()
            })).ConfigureAwait(false);
        }
    }
}
