using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using sblngavnav6.Common;
using sblngavnav6.Core;
using sblngavnav6.Data;

namespace sblngavnav6.Commands
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
        [RequiredInput(false)]
        public string Value { get; set; }
    }

    public class SettingsInteractions : InteractionModuleBase<SocketInteractionContext>
    {
        private const int MaxWelcomeLength = 1000;

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
            var reset = string.IsNullOrWhiteSpace(raw);

            if (field == "wtext")
            {
                if (!reset && raw.Length > MaxWelcomeLength)
                {
                    await FailAsync($"Текст слишком длинный: {raw.Length} символов, максимум {MaxWelcomeLength}");
                    return;
                }

                DataBase.SetWelcomeMessage(gid, reset ? null : raw);
                await ShowPanelAsync(reset ? "Welcome-текст сброшен на дефолт" : "Welcome-текст обновлён");
                return;
            }

            ulong? id = null;
            if (!reset)
            {
                if (!ulong.TryParse(raw, out var parsed))
                {
                    await FailAsync("Нужен числовой ID. Значение не сохранено — очисти поле, если хотел сбросить");
                    return;
                }

                var error = ValidateTarget(field, parsed);
                if (error != null)
                {
                    await FailAsync(error);
                    return;
                }

                id = parsed;
            }

            if (field == "su" && reset && Context.User is IGuildUser self && !self.GuildPermissions.Administrator)
            {
                await FailAsync("Сброс superuser-роли доступен только администратору сервера — иначе потеряешь доступ к панели");
                return;
            }

            switch (field)
            {
                case "su": DataBase.SetSuperuserRole(gid, id); break;
                case "wch": DataBase.SetWelcomeChannel(gid, id); break;
                case "wrole": DataBase.SetWelcomeRole(gid, id); break;
                case "stream": DataBase.SetStreamNotifChannel(gid, id); break;
                default:
                    await FailAsync($"Неизвестное поле настроек: {field}");
                    return;
            }

            await ShowPanelAsync(reset ? "Сброшено на дефолт" : "Сохранено");
        }

        private string ValidateTarget(string field, ulong id)
        {
            switch (field)
            {
                case "su":
                case "wrole":
                    var role = Context.Guild.GetRole(id);
                    if (role == null)
                        return "Роль с таким ID не найдена на этом сервере";
                    if (role.IsManaged)
                        return "Управляемую роль (бот/интеграция) выдавать нельзя";
                    if (field == "wrole" && !Context.Guild.CurrentUser.GuildPermissions.ManageRoles)
                        return "У бота нет права управлять ролями";
                    return null;

                case "wch":
                case "stream":
                    var channel = Context.Guild.GetChannel(id);
                    if (channel is null)
                        return "Канал с таким ID не найден на этом сервере";
                    if (channel is not SocketTextChannel)
                        return "Нужен текстовый канал";
                    return null;

                default:
                    return $"Неизвестное поле настроек: {field}";
            }
        }

        private Task FailAsync(string message)
            => RespondAsync($"🔴 {message}", ephemeral: true);

        private Task ShowPanelAsync(string note)
            => RespondAsync(
                $"✅ {note}",
                embed: SettingsPanel.Build(Context.Guild),
                components: SettingsPanel.Buttons(),
                ephemeral: true);

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
