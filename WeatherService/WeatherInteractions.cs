using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using sblngavnav6.Core;

namespace sblngavnav6.Commands
{
    internal static class WeatherControls
    {
        public const string ViewId = "wth_view";

        private static readonly string[] Labels = ["Сейчас", "24 часа", "5 дней"];

        public static Action<ComponentBuilder, int> Views(int total) => (builder, page) =>
        {
            for (var index = 0; index < Math.Min(total, Labels.Length); index++)
            {
                if (index == page)
                    continue;

                builder.WithButton(Labels[index], $"{ViewId}:{index}", ButtonStyle.Secondary);
            }
        };
    }

    public sealed class WeatherInteractions : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly PaginatorService _pager;

        public WeatherInteractions(PaginatorService pager)
        {
            _pager = pager;
        }

        [ComponentInteraction($"{WeatherControls.ViewId}:*")]
        public async Task View(string viewRaw)
        {
            if (Context.Interaction is not SocketMessageComponent component)
                return;

            if (!int.TryParse(viewRaw, out var view))
            {
                await component.DeferAsync();
                return;
            }

            await _pager.HandleFlipAsync(component, Context.User.Id, view);
        }
    }
}
