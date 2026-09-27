using System.Collections.Concurrent;
using Discord.Commands;
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
            var key = new CooldownInfo(context.User.Id, command.GetHashCode());
            var now = DateTimeOffset.UtcNow;

            if (_cooldowns.TryGetValue(key, out var endsAt))
            {
                var left = endsAt - now;

                if (left > TimeSpan.Zero)
                    return Task.FromResult(PreconditionResult.FromError(
                        $"дружище, имей совесть, напишешь только через {FormatAge(left)}. Ок? Ок."));

                _cooldowns.TryUpdate(key, now.Add(CooldownLength), endsAt);
            }
            else
            {
                _cooldowns.TryAdd(key, now.Add(CooldownLength));
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
