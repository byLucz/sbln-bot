using sblngavnav6.Data;
using sblngavnav6.TelegramExtensions.Core;

namespace sblngavnav6.Services
{
    internal sealed class TelegramHelpSource : ITelegramHelpSource
    {
        private readonly Lazy<IReadOnlyList<TelegramHelpSection>> _sections = new(() => Build(dev: false));
        private readonly Lazy<IReadOnlyList<TelegramHelpSection>> _devSections = new(() => Build(dev: true));
        private readonly Lazy<Dictionary<long, ulong>> _chats = new(() => Global.Vars.Cfg.IdMap("Telegram:ChatGuild").ToDictionary(pair => pair.left, pair => pair.right));
        private readonly Lazy<HashSet<long>> _linked = new(() => Global.Vars.Cfg.IdMap("Telegram:UserLink").Select(pair => pair.left).ToHashSet());

        public IReadOnlyList<TelegramHelpSection> GetSections(long chatId, long? userId)
        {
            var guild = _chats.Value.TryGetValue(chatId, out var mapped) ? mapped : Global.Vars.Cfg.telegramDefaultGuild;
            var dev = Global.Vars.Cfg.devGuild != 0 && guild == Global.Vars.Cfg.devGuild
                || userId is { } user && chatId == user && _linked.Value.Contains(user);
            return dev ? _devSections.Value : _sections.Value;
        }

        private static IReadOnlyList<TelegramHelpSection> Build(bool dev) => HelpEmbedService
            .TelegramSections(dev)
            .Select(section => new TelegramHelpSection(
                section.Title,
                section.Commands.Select(command => new TelegramHelpEntry(command.Command, command.About)).ToArray()))
            .ToArray();
    }
}
