using DiscordTelegramFrontier;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace sblngavnav6.TelegramExtensions.Core;

public sealed class TelegramCommandContext
{
    private readonly FrontierUpdateContext _frontier;

    internal TelegramCommandContext(FrontierUpdateContext frontier, Message message, string command, string arguments)
    {
        _frontier = frontier;
        Message = message;
        Command = command;
        Arguments = arguments;
    }

    public ITelegramBotClient Bot => _frontier.Bot;
    public Update Update => _frontier.Update;
    public Message Message { get; }
    public Chat Chat => Message.Chat;
    public User? User => Message.From;
    public string Command { get; }
    public string Arguments { get; }
    public IServiceProvider Services => _frontier.Services;
    public CancellationToken CancellationToken => _frontier.CancellationToken;

    public Task<Message> ReplyAsync(string text, ParseMode parseMode = ParseMode.None, ReplyMarkup? replyMarkup = null)
        => _frontier.ReplyAsync(text, parseMode, replyMarkup);
}
