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
                ? Discord.Commands.PreconditionResult.FromError(DevGuildGate.GuildOnly)
                : Discord.Commands.PreconditionResult.FromSuccess());
    }

    public class RequireSuperuserAttribute : Discord.Commands.PreconditionAttribute
    {
        public override async Task<Discord.Commands.PreconditionResult> CheckPermissionsAsync(
            ICommandContext context, CommandInfo command, IServiceProvider services)
            => await SuperuserGate.ErrorAsync(context.Client, context.Guild, context.User) is { } error
                ? Discord.Commands.PreconditionResult.FromError(error)
                : Discord.Commands.PreconditionResult.FromSuccess();
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

    public class RequireDevAccessAttribute : Discord.Commands.PreconditionAttribute
    {
        public override async Task<Discord.Commands.PreconditionResult> CheckPermissionsAsync(
            ICommandContext context, CommandInfo command, IServiceProvider services)
            => await DevAccessGate.ErrorAsync(context.Client, context.Guild, context.User) is { } error
                ? Discord.Commands.PreconditionResult.FromError(error)
                : Discord.Commands.PreconditionResult.FromSuccess();
    }

    public class RequireDevAccessInteractionAttribute : Discord.Interactions.PreconditionAttribute
    {
        public override async Task<Discord.Interactions.PreconditionResult> CheckRequirementsAsync(
            IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
            => await DevAccessGate.ErrorAsync(context.Client, context.Guild, context.User) is { } error
                ? Discord.Interactions.PreconditionResult.FromError(error)
                : Discord.Interactions.PreconditionResult.FromSuccess();
    }

    public class RequireOwnerInteractionAttribute : Discord.Interactions.RequireOwnerAttribute
    {
        public override string ErrorMessage => "недостаточно прав";
    }

    public static class DevAccessGate
    {
        public static async Task<string> ErrorAsync(IDiscordClient client, IGuild guild, IUser user)
        {
            if (guild is null)
                return await SuperuserGate.IsBotOwnerAsync(client, user) ? null : DevGuildGate.GuildOnly;

            return DevGuildGate.Error(guild) ?? await SuperuserGate.ErrorAsync(client, guild, user);
        }
    }

    public static class DevGuildGate
    {
        public const string GuildOnly = "только на сервере";

        public static bool IsDevGuild(IGuild guild) =>
            guild is not null && Global.Vars.Cfg.devGuild != 0 && guild.Id == Global.Vars.Cfg.devGuild;

        public static string Error(IGuild guild) =>
            guild is null ? GuildOnly
            : Global.Vars.Cfg.devGuild == 0 ? "не задан System:DevGuild"
            : guild.Id != Global.Vars.Cfg.devGuild ? "доступно только на сервере разработчиков"
            : null;
    }

    public static class SuperuserGate
    {
        public static async Task<bool> IsBotOwnerAsync(IDiscordClient client, IUser user)
        {
            if (user is null)
                return false;

            try
            {
                return (await client.GetApplicationInfoAsync()).Owner?.Id == user.Id;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static async Task<string> ErrorAsync(IDiscordClient client, IGuild guild, IUser user)
        {
            if (guild is null)
                return DevGuildGate.GuildOnly;

            return user is IGuildUser member && await IsAllowedAsync(client, guild, member) ? null : "нужна роль суперюзера";
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
