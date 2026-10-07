using sblngavnav6.TelegramExtensions.Core;

namespace sblngavnav6.Services
{
    internal sealed class TelegramHelpSource : ITelegramHelpSource
    {
        private readonly Lazy<IReadOnlyList<TelegramHelpSection>> _sections = new(() => HelpEmbedService
            .TelegramSections()
            .Select(section => new TelegramHelpSection(
                section.Title,
                section.Commands.Select(command => new TelegramHelpEntry(command.Command, command.About)).ToArray()))
            .ToArray());

        public IReadOnlyList<TelegramHelpSection> Sections => _sections.Value;
    }
}
