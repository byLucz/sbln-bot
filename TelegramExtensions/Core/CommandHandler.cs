using DiscordTelegramFrontier;
using Microsoft.Extensions.DependencyInjection;

namespace sblngavnav6.TelegramExtensions.Core;

internal sealed class CommandHandler(CommandCatalog commands) : IFrontierModule
{
    public async Task<bool> HandleAsync(FrontierUpdateContext update)
    {
        if (update.AddressedToAnotherBot) return false;
        var message = update.Update.Message ?? update.Update.ChannelPost;
        if (message is null) return false;
        if (message.From?.IsBot == true && message.SenderChat is null) return true;
        if (message.IsAutomaticForward == true) return true;
        var text = message.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return false;

        string? name;
        string arguments;
        if (text.StartsWith('/'))
        {
            name = update.Command;
            arguments = update.Arguments;
        }
        else
        {
            var end = 0;
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
            name = text[..end];
            arguments = text[end..].TrimStart();
        }
        if (name is null || !commands.TryGet(name, out var command)) return false;

        var context = new TelegramCommandContext(update, message, command.Name, arguments);
        foreach (var check in command.Preconditions)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (await check.CheckAsync(context) is not { } error) continue;
            await context.ReplyAsync(error);
            return true;
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        var module = (TelegramModuleBase)context.Services.GetRequiredService(command.ModuleType);
        module.Initialize(context);
        await command.Execute(module);
        return true;
    }
}
