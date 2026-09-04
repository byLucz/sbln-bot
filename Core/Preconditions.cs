using Discord;
using Discord.Commands;
using Discord.Interactions;
using sblngavnav5X.Data;

namespace sblngavnav5X.Core
{
    // Общие precondition-атрибуты (доступ по роли SQD). Реюзают PgApi и PPM.
    public class RequireSQDRoleAttribute : Discord.Commands.PreconditionAttribute
    {
        public override Task<Discord.Commands.PreconditionResult> CheckPermissionsAsync(
            ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (Utils.sqdRoleId == 0)
                return Task.FromResult(Discord.Commands.PreconditionResult.FromError("pgApiRoleId не задан в Utils"));

            if (context.User is not IGuildUser gu || !gu.RoleIds.Contains(Utils.sqdRoleId))
                return Task.FromResult(Discord.Commands.PreconditionResult.FromError("Нет доступа к pgAPI"));

            return Task.FromResult(Discord.Commands.PreconditionResult.FromSuccess());
        }
    }

    public class RequireSQDRoleInteractionAttribute : Discord.Interactions.PreconditionAttribute
    {
        public override Task<Discord.Interactions.PreconditionResult> CheckRequirementsAsync(
            IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
        {
            if (Utils.sqdRoleId == 0)
                return Task.FromResult(Discord.Interactions.PreconditionResult.FromError("pgApiRoleId не задан в Utils"));

            if (context.User is not IGuildUser gu || !gu.RoleIds.Contains(Utils.sqdRoleId))
                return Task.FromResult(Discord.Interactions.PreconditionResult.FromError("Нет доступа к pgAPI"));

            return Task.FromResult(Discord.Interactions.PreconditionResult.FromSuccess());
        }
    }
}
