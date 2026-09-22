using sblngavnav6.Core;

namespace sblngavnav6
{
    class Program
    {
        private static Task Main()
            => new DiscordService().RunAsync();
    }
}
