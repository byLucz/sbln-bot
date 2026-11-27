using Discord.Commands;
using DiscordTelegramFrontier;
using System.Collections.Concurrent;
using static sblngavnav6.Common.CommonUtils.Time;

namespace sblngavnav6.Services
{
    public class CooldownAttribute : PreconditionAttribute
    {
        private const int PurgeThreshold = 256;

        private readonly ConcurrentDictionary<CooldownInfo, DateTimeOffset> _cooldowns = new();

        public CooldownAttribute(int seconds)
        {
            CooldownLength = TimeSpan.FromSeconds(seconds);
        }

        private TimeSpan CooldownLength { get; }

        public override Task<PreconditionResult> CheckPermissionsAsync(
            ICommandContext context,
            CommandInfo command,
            IServiceProvider services)
        {
            var key = new CooldownInfo(context.Sender()?.Id ?? 0, command.GetHashCode());
            var now = DateTimeOffset.UtcNow;

            while (true)
            {
                if (_cooldowns.TryGetValue(key, out var endsAt))
                {
                    var left = endsAt - now;

                    if (left > TimeSpan.Zero)
                        return Task.FromResult(PreconditionResult.FromError(
                            $"дружище, имей совесть, напишешь только через {FormatAge(left)}. Ок? Ок."));

                    if (_cooldowns.TryUpdate(key, now.Add(CooldownLength), endsAt))
                        break;
                }
                else if (_cooldowns.TryAdd(key, now.Add(CooldownLength)))
                {
                    break;
                }
            }

            if (_cooldowns.Count > PurgeThreshold)
                Purge(now);

            return Task.FromResult(PreconditionResult.FromSuccess());
        }

        private void Purge(DateTimeOffset now)
        {
            foreach (var entry in _cooldowns)
                if (entry.Value <= now)
                    _cooldowns.TryRemove(entry);
        }

        public readonly record struct CooldownInfo(ulong UserId, int CommandHashCode);
    }
}
