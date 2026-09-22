using Discord.Interactions;
using Discord.WebSocket;
using sblngavnav6.Data;
using sblngavnav6.Services;
using System.Reflection;

namespace sblngavnav6.Core
{
    public sealed class InteractionHandler : IDisposable
    {
        private readonly DiscordSocketClient _client;
        private readonly InteractionService _interactions;
        private readonly IServiceProvider _services;
        private int _commandsRegistered;
        private bool _hooked;
        private bool _disposed;

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
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_hooked) return;

            await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _services);

            _client.InteractionCreated += HandleInteractionAsync;
            _client.Ready += RegisterCommandsAsync;
            _hooked = true;
        }

        public Task StopAsync()
        {
            Unhook();
            return Task.CompletedTask;
        }

        private async Task RegisterCommandsAsync()
        {
            if (Interlocked.Exchange(ref _commandsRegistered, 1) != 0)
                return;

            try
            {
                if (Global.Vars.Cfg.slashScopeGuild != 0)
                {
                    await _interactions.RegisterCommandsToGuildAsync(Global.Vars.Cfg.slashScopeGuild);
                    return;
                }

                await _interactions.RegisterCommandsGloballyAsync();

                if (Global.Vars.Cfg.slashDevGuild != 0)
                    await _interactions.RegisterCommandsToGuildAsync(Global.Vars.Cfg.slashDevGuild);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _commandsRegistered, 0);
                await LoggingService.LogCriticalAsync("Interactions", "Ошибка регистрации команд", ex);
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
                await LoggingService.LogCriticalAsync("Interactions", "Ошибка обработки interaction", ex);
            }
        }

        private void Unhook()
        {
            if (!_hooked) return;

            _client.InteractionCreated -= HandleInteractionAsync;
            _client.Ready -= RegisterCommandsAsync;
            _hooked = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unhook();
        }
    }
}
