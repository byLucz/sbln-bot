using Discord.Interactions;
using Discord.WebSocket;
using sblngavnav5X.Services;
using System.Reflection;

namespace sblngavnav5X.Core
{
    public class InteractionHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly InteractionService _interactions;
        private readonly IServiceProvider _services;
        private bool _commandsRegistered;

        public InteractionHandler(DiscordSocketClient client,
                                  InteractionService interactions,
                                  IServiceProvider services)
        {
            _client = client;
            _interactions = interactions;
            _services = services;
        }

        public async Task InitializeAsync()
        {
            _client.InteractionCreated += HandleInteractionAsync;
            await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
            _client.Ready += RegisterCommandsAsync;
        }

        private async Task RegisterCommandsAsync()
        {
            if (_commandsRegistered)
                return;

            try
            {
                await _interactions.RegisterCommandsGloballyAsync();
                await _interactions.RegisterCommandsToGuildAsync(500673210463813632);
                _commandsRegistered = true;
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync("Interactions", $"Ошибка регистрации команд: {ex.Message}");
            }
        }

        private async Task HandleInteractionAsync(SocketInteraction socketInteraction)
        {
            try
            {
                var ctx = new SocketInteractionContext(_client, socketInteraction);
                await _interactions.ExecuteCommandAsync(ctx, _services);
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync("Interactions", $"Ошибка обработки interaction: {ex.Message}");
            }
        }
    }
}
