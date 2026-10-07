namespace sblngavnav6.TelegramExtensions.Core;

public sealed record TelegramHelpEntry(string Command, string About);

public sealed record TelegramHelpSection(string Title, IReadOnlyList<TelegramHelpEntry> Commands);

public interface ITelegramHelpSource
{
    IReadOnlyList<TelegramHelpSection> Sections { get; }
}
