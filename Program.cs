using sblngavnav6.Core;

namespace sblngavnav6
{
    class Program
    {
        private static Task Main()
        {
            var workers = Math.Max(32, Environment.ProcessorCount * 8);
            ThreadPool.GetMinThreads(out _, out var ports);
            ThreadPool.SetMinThreads(workers, Math.Max(ports, workers));

            return new DiscordService().RunAsync();
        }
    }
}
