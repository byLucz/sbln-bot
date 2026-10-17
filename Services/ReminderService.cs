using Discord;
using Discord.Net;
using Discord.WebSocket;
using sblngavnav6.Common;

namespace sblngavnav6.Services
{
    public static class ReminderService
    {
        public static async Task RemindAsyncSeconds(SocketUser user, IMessageChannel fallback, int seconds, string message, CancellationToken cancellationToken = default)
        {
            var delay = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, (int)TimeSpan.FromDays(1).TotalSeconds));
            var placed = DateTimeOffset.Now;

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            var embed = EmbedHandler.Build(new EmbedSpec
            {
                Title = "sbln напоминалка👽",
                Description = message,
                Color = Color.Teal,
                Footer = $"поставлена в {placed:HH:mm:ss} / прошло {CommonUtils.Time.FormatTime(delay)}",
                FooterIconUrl = user.GetAvatarUrl()
            });

            try
            {
                await user.SendMessageAsync(embed: embed).ConfigureAwait(false);
            }
            catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
            {
                try
                {
                    await fallback.SendMessageAsync(user.Mention, embed: embed, allowedMentions: new AllowedMentions { UserIds = [user.Id] }).ConfigureAwait(false);
                }
                catch (Exception fallbackEx) when (fallbackEx is HttpException or TimeoutException)
                {
                    await LoggingService.LogWarningAsync("REMND", $"Напоминание для {user.Id} не доставлено: ЛС закрыты, канал недоступен ({fallbackEx.Message})").ConfigureAwait(false);
                }
            }
        }
    }
}
