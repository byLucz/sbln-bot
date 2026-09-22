using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace sblngavnav6.TelegramExtensions.Core;

public abstract class TelegramModuleBase
{
    private TelegramCommandContext? _context;

    public TelegramCommandContext Context => _context
        ?? throw new InvalidOperationException("The command module has not been initialized.");

    internal void Initialize(TelegramCommandContext context) => _context = context;

    protected Task<Message> ReplyAsync(string text, ParseMode parseMode = ParseMode.None, ReplyMarkup? replyMarkup = null)
        => Context.ReplyAsync(text, parseMode, replyMarkup);
}
