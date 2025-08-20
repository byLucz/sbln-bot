using sblngavnav5X.Core;

namespace sblngavnav5X
{
    class Program
    {
        private static Task Main()
            => new DiscordService().InitializeAsync();
    }
}
