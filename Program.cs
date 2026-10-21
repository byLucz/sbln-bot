using sblngavnav5X.Core;

namespace sblngavnav5X
{
    class Program
    {
        private static Task<int> Main(string[] args)
        {
            if (args.Contains("--health"))
                return Task.FromResult(!string.IsNullOrWhiteSpace(DiscordService.ReadyFile) && File.Exists(DiscordService.ReadyFile) ? 0 : 1);

            return RunAsync();
        }

        private static async Task<int> RunAsync()
        {
            await new DiscordService().InitializeAsync();
            return Environment.ExitCode;
        }
    }
}
