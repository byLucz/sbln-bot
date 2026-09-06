using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using sblngavnav5X.Common;
using sblngavnav5X.Core;
using sblngavnav5X.Data;

namespace sblngavnav5X.Commands
{
    public static class SettingsPanel
    {
        public static Embed Build(IGuild guild)
        {
            var gs = DataBase.GetGuildSettings(guild.Id);

            static string Role(ulong? id) => id is ulong r ? MentionUtils.MentionRole(r) : "`не задано`";
            static string Chan(ulong? id) => id is ulong c ? MentionUtils.MentionChannel(c) : "`не задано`";

            return new EmbedBuilder()
                .WithTitle($"⚙️ Настройки сервера — {guild.Name}")
                .WithColor(Color.Teal)
                .AddField("👑 Доступ", $"Superuser-роль: {Role(gs.SuperuserRoleId)}")
                .AddField("👋 Welcome",
                    $"Канал: {Chan(gs.WelcomeChannelId)}\n" +
                    $"Роль: {Role(gs.WelcomeRoleId)}\n" +
                    $"Текст: {(string.IsNullOrWhiteSpace(gs.WelcomeMessage) ? "`по умолчанию`" : gs.WelcomeMessage)}")
                .AddField("📺 Стримы",
                    $"Канал уведомлений: {(gs.StreamNotifChannelId is ulong s ? MentionUtils.MentionChannel(s) : "`выключено`")}")
                .WithFooter("sbln настройки")
                .Build();
        }

        public static MessageComponent Buttons()
            => new ComponentBuilder()
                .WithButton("👑 Superuser", "setf:su", ButtonStyle.Secondary, row: 0)
                .WithButton("👋 Welcome-канал", "setf:wch", ButtonStyle.Secondary, row: 0)
                .WithButton("🎭 Welcome-роль", "setf:wrole", ButtonStyle.Secondary, row: 0)
                .WithButton("💬 Welcome-текст", "setf:wtext", ButtonStyle.Secondary, row: 1)
                .WithButton("📺 Стрим-канал", "setf:stream", ButtonStyle.Secondary, row: 1)
                .Build();
    }

    public class SettingsValueModal : IModal
    {
        public string Title => "Настройка";

        [InputLabel("Значение (пусто = сброс на дефолт)")]
        [ModalTextInput("value", TextInputStyle.Paragraph, "id роли/канала или текст")]
        public string Value { get; set; }
    }

    public class SettingsInteractions : InteractionModuleBase<SocketInteractionContext>
    {
        [ComponentInteraction("setopen")]
        public async Task Open()
        {
            if (!await Allowed()) return;
            await RespondAsync(embed: SettingsPanel.Build(Context.Guild), components: SettingsPanel.Buttons(), ephemeral: true);
        }

        [ComponentInteraction("setf:*")]
        public async Task Field(string field)
        {
            if (!await Allowed()) return;
            await Context.Interaction.RespondWithModalAsync<SettingsValueModal>($"setmodal:{field}");
        }

        [ModalInteraction("setmodal:*")]
        public async Task Save(string field, SettingsValueModal modal)
        {
            if (!await Allowed()) return;

            var gid = Context.Guild.Id;
            var raw = modal.Value?.Trim();
            ulong? id = ulong.TryParse(raw, out var v) ? v : null;

            switch (field)
            {
                case "su": DataBase.SetSuperuserRole(gid, id); break;
                case "wch": DataBase.SetWelcomeChannel(gid, id); break;
                case "wrole": DataBase.SetWelcomeRole(gid, id); break;
                case "wtext": DataBase.SetWelcomeMessage(gid, string.IsNullOrWhiteSpace(raw) ? null : raw); break;
                case "stream": DataBase.SetStreamNotifChannel(gid, id); break;
            }

            await RespondAsync(embed: SettingsPanel.Build(Context.Guild), components: SettingsPanel.Buttons(), ephemeral: true);
        }

        private async Task<bool> Allowed()
        {
            if (Context.Guild is null || Context.User is not IGuildUser gu)
            {
                await RespondAsync("только на сервере", ephemeral: true);
                return false;
            }
            if (await SuperuserGate.IsAllowedAsync(Context.Client, Context.Guild, gu))
                return true;

            await RespondAsync("нужна роль суперюзера или права владельца бота", ephemeral: true);
            return false;
        }
    }
}
