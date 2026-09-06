using Discord;
using Discord.Commands;
using sblngavnav5X.Common;
using sblngavnav5X.Core;
using sblngavnav5X.Data;

namespace sblngavnav5X.Commands
{
    [Group("настройки")]
    [RequireSuperuser]
    public class SettingsCommands : ModuleBase<SocketCommandContext>
    {
        [Command]
        public async Task Show()
        {
            var gs = DataBase.GetGuildSettings(Context.Guild.Id);

            string Role(ulong? id) => id is ulong r ? MentionUtils.MentionRole(r) : "не задано (по умолчанию)";
            string Chan(ulong? id) => id is ulong c ? MentionUtils.MentionChannel(c) : "не задано (по умолчанию)";

            var desc =
                $"👑 **Суперюзер-роль:** {Role(gs.SuperuserRoleId)}\n" +
                $"👋 **Welcome-канал:** {Chan(gs.WelcomeChannelId)}\n" +
                $"🎭 **Welcome-роль:** {Role(gs.WelcomeRoleId)}\n" +
                $"💬 **Welcome-текст:** {(string.IsNullOrWhiteSpace(gs.WelcomeMessage) ? "по умолчанию" : gs.WelcomeMessage)}\n" +
                $"📺 **Стрим-канал:** {Chan(gs.StreamNotifChannelId)}";

            await ReplyAsync(embed: EmbedHandler.Simple("⚙️ Настройки сервера", desc, Color.Teal, "sbln настройки"));
        }

        [Command("суперюзер")]
        public async Task SetSuperuser(IRole role = null)
        {
            DataBase.SetSuperuserRole(Context.Guild.Id, role?.Id);
            await Ack(role == null ? "Роль суперюзера сброшена" : $"Суперюзер-роль: {role.Mention}");
        }

        [Command("велком-канал")]
        public async Task SetWelcomeChannel(ITextChannel channel = null)
        {
            DataBase.SetWelcomeChannel(Context.Guild.Id, channel?.Id);
            await Ack(channel == null ? "Welcome-канал сброшен на дефолт" : $"Welcome-канал: {channel.Mention}");
        }

        [Command("велком-роль")]
        public async Task SetWelcomeRole(IRole role = null)
        {
            DataBase.SetWelcomeRole(Context.Guild.Id, role?.Id);
            await Ack(role == null ? "Welcome-роль сброшена на дефолт" : $"Welcome-роль: {role.Mention}");
        }

        [Command("велком-текст")]
        public async Task SetWelcomeMessage([Remainder] string message = null)
        {
            DataBase.SetWelcomeMessage(Context.Guild.Id, message);
            await Ack(string.IsNullOrWhiteSpace(message) ? "Welcome-текст сброшен на дефолт" : "Welcome-текст обновлён");
        }

        [Command("стрим-канал")]
        public async Task SetStreamChannel(ITextChannel channel = null)
        {
            DataBase.SetStreamNotifChannel(Context.Guild.Id, channel?.Id);
            await Ack(channel == null ? "Стрим-канал сброшен на дефолт (по имени twitch)" : $"Стрим-уведомления в: {channel.Mention}");
        }

        private Task Ack(string text)
            => ReplyAsync(embed: EmbedHandler.Simple("⚙️ Настройки", $"✅ {text}", Color.Green, "sbln настройки"));
    }
}
