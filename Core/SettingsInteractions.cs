using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using sblngavnav6.Common;
using sblngavnav6.Data;

namespace sblngavnav6.Core
{
    public static class SettingsPanel
    {
        public static async Task<(Embed Embed, MessageComponent Components)> BuildAsync(IGuild guild, string note = null)
        {
            var gs = await DataBase.GetGuildSettings(guild.Id);
            return (BuildEmbed(guild, gs, note), BuildComponents(gs));
        }

        private static Embed BuildEmbed(IGuild guild, GuildSettings gs, string note)
        {
            static string Role(ulong? id) => id is ulong r ? MentionUtils.MentionRole(r) : "`не задано`";
            static string Chan(ulong? id) => id is ulong c ? MentionUtils.MentionChannel(c) : "`выключено`";

            return EmbedHandler.Build(new EmbedSpec
            {
                Title = $"⚙️ Настройки сервера — {guild.Name}",
                Description = note,
                Color = Color.Teal,
                Fields =
                [
                    new EmbedFieldSpec("👑 Доступ", $"Superuser-роль: {Role(gs.SuperuserRoleId)}"),
                    new EmbedFieldSpec("👋 Welcome",
                        $"Канал: {Chan(gs.WelcomeChannelId)}\n" +
                        $"Роль: {Role(gs.WelcomeRoleId)}\n" +
                        $"Текст: {(string.IsNullOrWhiteSpace(gs.WelcomeMessage) ? "`по умолчанию`" : gs.WelcomeMessage)}"),
                    new EmbedFieldSpec("📺 Стримы", $"Канал уведомлений: {Chan(gs.StreamNotifChannelId)}")
                ],
                Footer = "sbln настройки / сними выбор в списке, чтобы сбросить"
            });
        }

        private static MessageComponent BuildComponents(GuildSettings gs)
            => new ComponentBuilder()
                .WithSelectMenu(Select("su", "👑 Superuser-роль", ComponentType.RoleSelect, gs.SuperuserRoleId, SelectDefaultValueType.Role), row: 0)
                .WithSelectMenu(Select("wch", "👋 Welcome-канал", ComponentType.ChannelSelect, gs.WelcomeChannelId, SelectDefaultValueType.Channel), row: 1)
                .WithSelectMenu(Select("wrole", "🎭 Welcome-роль", ComponentType.RoleSelect, gs.WelcomeRoleId, SelectDefaultValueType.Role), row: 2)
                .WithSelectMenu(Select("stream", "📺 Стрим-канал", ComponentType.ChannelSelect, gs.StreamNotifChannelId, SelectDefaultValueType.Channel), row: 3)
                .WithButton("💬 Welcome-текст", "setf:wtext", ButtonStyle.Secondary, row: 4)
                .Build();

        private static SelectMenuBuilder Select(string field, string placeholder, ComponentType type, ulong? current, SelectDefaultValueType defaultType)
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId($"setsel:{field}")
                .WithType(type)
                .WithPlaceholder(placeholder)
                .WithMinValues(0)
                .WithMaxValues(1);

            if (type == ComponentType.ChannelSelect)
                menu.WithChannelTypes(ChannelType.Text, ChannelType.News);

            if (current is ulong id)
                menu.WithDefaultValues(new SelectMenuDefaultValue(id, defaultType));

            return menu;
        }
    }

    public class WelcomeTextModal : IModal
    {
        public string Title => "Welcome-текст";

        [InputLabel("Текст (пусто = по умолчанию)")]
        [ModalTextInput("value", TextInputStyle.Paragraph, "@user, добро пожаловать!", maxLength: 1000)]
        [RequiredInput(false)]
        public string Value { get; set; }
    }

    public class SettingsInteractions : InteractionModuleBase<SocketInteractionContext>
    {
        [ComponentInteraction("setopen")]
        public async Task Open()
        {
            if (!await Allowed()) return;

            var (embed, components) = await SettingsPanel.BuildAsync(Context.Guild);
            await RespondAsync(embed: embed, components: components, ephemeral: true);
        }

        [ComponentInteraction("setsel:*")]
        public async Task Select(string field, string[] values)
        {
            if (!await Allowed()) return;

            var component = (SocketMessageComponent)Context.Interaction;
            ulong? id = values is { Length: > 0 } && ulong.TryParse(values[0], out var parsed) ? parsed : null;

            var error = await ApplyAsync(field, id);
            var (embed, components) = await SettingsPanel.BuildAsync(
                Context.Guild,
                error is null ? (id is null ? "✅ Сброшено" : "✅ Сохранено") : $"❌ {error}");

            await component.UpdateAsync(message =>
            {
                message.Embed = embed;
                message.Components = components;
            });
        }

        [ComponentInteraction("setf:wtext")]
        public async Task EditWelcomeText()
        {
            if (!await Allowed()) return;

            var gs = await DataBase.GetGuildSettings(Context.Guild.Id);
            await Context.Interaction.RespondWithModalAsync("setmodal:wtext", new WelcomeTextModal { Value = gs.WelcomeMessage });
        }

        [ModalInteraction("setmodal:wtext")]
        public async Task SaveWelcomeText(WelcomeTextModal modal)
        {
            if (!await Allowed()) return;

            var raw = modal.Value?.Trim();
            var reset = string.IsNullOrWhiteSpace(raw);

            await DataBase.SetWelcomeMessage(Context.Guild.Id, reset ? null : raw);

            var (embed, components) = await SettingsPanel.BuildAsync(
                Context.Guild,
                reset ? "✅ Welcome-текст сброшен" : "✅ Welcome-текст обновлён");

            await ((SocketModal)Context.Interaction).UpdateAsync(message =>
            {
                message.Embed = embed;
                message.Components = components;
            });
        }

        private async Task<string> ApplyAsync(string field, ulong? id)
        {
            var gid = Context.Guild.Id;

            if (id is ulong target && Validate(field, target) is { } error)
                return error;

            switch (field)
            {
                case "su":
                    if (id is null && Context.User is IGuildUser { GuildPermissions.Administrator: false })
                        return "Сброс superuser-роли доступен только администратору сервера — иначе потеряешь доступ к панели";
                    await DataBase.SetSuperuserRole(gid, id);
                    return null;

                case "wch":
                    await DataBase.SetWelcomeChannel(gid, id);
                    return null;

                case "wrole":
                    await DataBase.SetWelcomeRole(gid, id);
                    return null;

                case "stream":
                    await DataBase.SetStreamNotifChannel(gid, id);
                    return null;

                default:
                    return $"Неизвестное поле настроек: {field}";
            }
        }

        private string Validate(string field, ulong id)
        {
            switch (field)
            {
                case "su":
                case "wrole":
                    var role = Context.Guild.GetRole(id);
                    if (role == null)
                        return "Роль не найдена на этом сервере";
                    if (role.IsManaged || role.IsEveryone)
                        return "Эту роль выбрать нельзя";
                    if (field == "wrole" && !Context.Guild.CurrentUser.GuildPermissions.ManageRoles)
                        return "У бота нет права управлять ролями";
                    if (field == "wrole" && role.Position >= Context.Guild.CurrentUser.Hierarchy)
                        return "Роль выше роли бота, выдать её не получится";
                    return null;

                case "wch":
                case "stream":
                    return Context.Guild.GetChannel(id) is SocketTextChannel ? null : "Нужен текстовый канал";

                default:
                    return null;
            }
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
