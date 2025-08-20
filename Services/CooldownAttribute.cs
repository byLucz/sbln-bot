using System.Collections.Concurrent;
using Discord.Commands;

namespace sblngavnav5X.Services
{
    public class CooldownAttribute : PreconditionAttribute
	{
		private readonly ConcurrentDictionary<CooldownInfo, DateTime> cooldowns =
			new ConcurrentDictionary<CooldownInfo, DateTime>();

		public CooldownAttribute(int seconds)
		{
			CooldownLength = TimeSpan.FromSeconds(seconds);
		}

		private TimeSpan CooldownLength { get; }
		public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command,
			IServiceProvider services)
		{
			CooldownInfo key = new CooldownInfo(context.User.Id, command.GetHashCode());

			if (cooldowns.TryGetValue(key, out DateTime endsAt))
			{
				TimeSpan difference = endsAt.Subtract(DateTime.Now);
				if (difference.Ticks > 0)
					return Task.FromResult(
						PreconditionResult.FromError($"дружище, имей совесть, напишешь только через {difference:ss} секунд. Ок? Ок."));

				DateTime time = DateTime.Now.Add(CooldownLength);
				cooldowns.TryUpdate(key, time, endsAt);
			}
			else
			{
				cooldowns.TryAdd(key, DateTime.Now.Add(CooldownLength));
			}

			return Task.FromResult(PreconditionResult.FromSuccess());
		}

        public struct CooldownInfo
        {
            public ulong UserId { get; }

            public int CommandHashCode { get; }

            public CooldownInfo(ulong userId, int commandHashCode)
            {
                UserId = userId;
                CommandHashCode = commandHashCode;
            }
        }
    }
}