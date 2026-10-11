using Discord;
using Discord.Commands;
using Discord.Interactions;
using sblngavnav6.Data;

namespace sblngavnav6.Core
{
    public class RequireGuildAttribute : Discord.Commands.PreconditionAttribute
    {
        public override Task<Discord.Commands.PreconditionResult> CheckPermissionsAsync(
            ICommandContext context, CommandInfo command, IServiceProvider services)
            => Task.FromResult(context.Guild is null
                ? Discord.Commands.PreconditionResult.FromError("только на сервере")
                : Discord.Commands.PreconditionResult.FromSuccess());
    }

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

    public class RequireDevGuildAttribute : Discord.Commands.PreconditionAttribute
    {
        public override Task<Discord.Commands.PreconditionResult> CheckPermissionsAsync(
            ICommandContext context, CommandInfo command, IServiceProvider services)
            => Task.FromResult(DevGuildGate.Error(context.Guild) is { } error
                ? Discord.Commands.PreconditionResult.FromError(error)
                : Discord.Commands.PreconditionResult.FromSuccess());
    }

    public class RequireDevGuildInteractionAttribute : Discord.Interactions.PreconditionAttribute
    {
        public override Task<Discord.Interactions.PreconditionResult> CheckRequirementsAsync(
            IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
            => Task.FromResult(DevGuildGate.Error(context.Guild) is { } error
                ? Discord.Interactions.PreconditionResult.FromError(error)
                : Discord.Interactions.PreconditionResult.FromSuccess());
    }

    public static class DevGuildGate
    {
        public static bool IsDevGuild(IGuild guild) =>
            guild is not null && Global.Vars.Cfg.devGuild != 0 && guild.Id == Global.Vars.Cfg.devGuild;

        public static string Error(IGuild guild) =>
            guild is null ? "только на сервере"
            : Global.Vars.Cfg.devGuild == 0 ? "не задан System:DevGuild"
            : guild.Id != Global.Vars.Cfg.devGuild ? "доступно только на сервере разработчиков"
            : null;
    }

    public static class SuperuserGate
    {
        private static ulong _botOwnerId;

        public static async Task<bool> IsBotOwnerAsync(IDiscordClient client, IUser user)
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

            return _botOwnerId != 0 && user.Id == _botOwnerId;
        }

        public static async Task<bool> IsAllowedAsync(IDiscordClient client, IGuild guild, IGuildUser user)
        {
            if (await IsBotOwnerAsync(client, user).ConfigureAwait(false))
                return true;

            var gs = await DataBase.GetGuildSettings(guild.Id);
            return gs.SuperuserRoleId is ulong role && user.RoleIds.Contains(role);
        }
    }
}
