using Discord;
using Discord.Commands;
using Discord.Interactions;
using sblngavnav6.Data;

namespace sblngavnav6.Core
{
    public class RequireSuperuserAttribute : Discord.Commands.PreconditionAttribute
    {
        public override async Task<Discord.Commands.PreconditionResult> CheckPermissionsAsync(
            ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.Guild is null || context.User is not IGuildUser gu)
                return Discord.Commands.PreconditionResult.FromError("только на сервере");

            if (await SuperuserGate.IsAllowedAsync(context.Client, context.Guild, gu))
                return Discord.Commands.PreconditionResult.FromSuccess();

            return Discord.Commands.PreconditionResult.FromError("нужна роль суперюзера");
        }
    }

    public class RequireSuperuserInteractionAttribute : Discord.Interactions.PreconditionAttribute
    {
        public override async Task<Discord.Interactions.PreconditionResult> CheckRequirementsAsync(
            IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
        {
            if (context.Guild is null || context.User is not IGuildUser gu)
                return Discord.Interactions.PreconditionResult.FromError("только на сервере");

            if (await SuperuserGate.IsAllowedAsync(context.Client, context.Guild, gu))
                return Discord.Interactions.PreconditionResult.FromSuccess();

            return Discord.Interactions.PreconditionResult.FromError("нужна роль суперюзера");
        }
    }

    public class RequirePgOperatorAttribute : Discord.Commands.PreconditionAttribute
    {
        public override async Task<Discord.Commands.PreconditionResult> CheckPermissionsAsync(
            ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.Guild is null || context.User is not IGuildUser gu)
                return Discord.Commands.PreconditionResult.FromError("только на сервере");

            if (context.Guild.Id != PgApiGate.DevGuildId)
                return Discord.Commands.PreconditionResult.FromError("pgAPI доступен только на сервере разработчиков");

            if (await SuperuserGate.IsAllowedAsync(context.Client, context.Guild, gu))
                return Discord.Commands.PreconditionResult.FromSuccess();

            return Discord.Commands.PreconditionResult.FromError("нужна роль суперюзера");
        }
    }

    public class RequirePgOperatorInteractionAttribute : Discord.Interactions.PreconditionAttribute
    {
        public override async Task<Discord.Interactions.PreconditionResult> CheckRequirementsAsync(
            IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
        {
            if (context.Guild is null || context.User is not IGuildUser gu)
                return Discord.Interactions.PreconditionResult.FromError("только на сервере");

            if (context.Guild.Id != PgApiGate.DevGuildId)
                return Discord.Interactions.PreconditionResult.FromError("pgAPI доступен только на сервере разработчиков");

            if (await SuperuserGate.IsAllowedAsync(context.Client, context.Guild, gu))
                return Discord.Interactions.PreconditionResult.FromSuccess();

            return Discord.Interactions.PreconditionResult.FromError("нужна роль суперюзера");
        }
    }

    public static class PgApiGate
    {
        public const ulong DevGuildId = 500673210463813632;
    }

    public static class SuperuserGate
    {
        private static ulong _botOwnerId;

        public static async Task<bool> IsAllowedAsync(IDiscordClient client, IGuild guild, IGuildUser user)
        {
            if (_botOwnerId == 0)
            {
                try
                {
                    var app = await client.GetApplicationInfoAsync();
                    _botOwnerId = app.Owner?.Id ?? 0;
                }
                catch { }
            }

            if (_botOwnerId != 0 && user.Id == _botOwnerId)
                return true;

            var gs = DataBase.GetGuildSettings(guild.Id);
            return gs.SuperuserRoleId is ulong role && user.RoleIds.Contains(role);
        }
    }
}
