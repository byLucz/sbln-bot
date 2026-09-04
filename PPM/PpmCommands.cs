using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using sblngavnav5X.Core;
using sblngavnav5X.Common;
using sblngavnav5X.Data;
using static sblngavnav5X.Common.CommonUtils.Text;
using static sblngavnav5X.Common.CommonUtils.Time;
using static sblngavnav5X.Data.DataRoots;

namespace sblngavnav5X.PPM
{
    public static class PpmPanelBuilder
    {
        private const int PageSize = 5;

        public static Embed BuildRootEmbed(IReadOnlyList<PpmMailbox> boxes, int page)
        {
            int totalPages = Pagination.TotalPages(boxes.Count, PageSize);
            var eb = EmbedHandler.FieldsEmbed("📮 PechkinPostManager", Color.Teal, EmbedHandler.PpmFooter);

            if (boxes.Count == 0)
            {
                eb.WithDescription("У тебя нет активных ящиков. Создай новый кнопкой ниже.");
                return eb.Build();
            }

            var lines = boxes
                .Skip(page * PageSize)
                .Take(PageSize)
                .Select(b =>
                {
                    string ttl = b.IsPermanent
                        ? "♾️"
                        : b.ExpiresAt.HasValue ? $"<t:{ToUnix(b.ExpiresAt.Value)}:R>" : "—";
                    return $"`{b.Email}` · {ttl}";
                });

            eb.WithDescription($"Твои ящики (стр. {page + 1}/{totalPages}):\n" + string.Join("\n", lines));
            return eb.Build();
        }

        public static MessageComponent BuildRootComponents(IReadOnlyList<PpmMailbox> boxes, int page)
        {
            int totalPages = Pagination.TotalPages(boxes.Count, PageSize);
            page = Math.Clamp(page, 0, totalPages - 1);

            var builder = new ComponentBuilder();

            var items = boxes.Skip(page * PageSize).Take(PageSize).ToArray();
            foreach (var b in items)
            {
                string label = b.Email.Split('@')[0];
                builder.WithButton($"📬 {label}", $"ppm_open:{b.Id}", ButtonStyle.Primary, row: 0);
            }

            builder.WithButton("➕ 15 мин", "ppm_new:temp", ButtonStyle.Success, row: 1);
            builder.WithButton("➕ навсегда", "ppm_new:perm", ButtonStyle.Success, row: 1);

            if (totalPages > 1)
                builder.AddPager(page, totalPages, "ppm_list", row: 2);

            return builder.Build();
        }

        public static Embed BuildInboxEmbed(PpmMailbox box, IReadOnlyList<PpmMessageView> msgs)
        {
            string ttl = box.IsPermanent
                ? "♾️ постоянный"
                : box.ExpiresAt.HasValue ? $"⏳ удалится <t:{ToUnix(box.ExpiresAt.Value)}:R>" : "—";

            var eb = EmbedHandler.FieldsEmbed($"📬 {box.Email}", Color.Teal, EmbedHandler.PpmFooter)
                .WithDescription(msgs.Count == 0
                    ? $"{ttl}\n\nВходящих писем нет."
                    : $"{ttl}\n\nПоследние письма ({msgs.Count}):");

            foreach (var m in msgs)
            {
                string header = $"✉️ {Truncate(m.Subject, 200)} — {m.Date:dd.MM HH:mm}";
                string value = $"**От:** {Truncate(m.From, 200)}\n{Truncate(m.Body, 700)}";
                eb.AddField(Truncate(header, 256), Truncate(value, 1024));
            }

            return eb.Build();
        }

        public static MessageComponent BuildInboxComponents(PpmMailbox box) =>
            new ComponentBuilder()
                .WithButton("🔄 Обновить", $"ppm_inbox:{box.Id}", ButtonStyle.Secondary, row: 0)
                .WithButton("🗑️ Удалить", $"ppm_del:{box.Id}", ButtonStyle.Danger, row: 0)
                .Build();

    }

    [RequireSQDRole]
    public class PpmCommands : ModuleBase<SocketCommandContext>
    {
        [Command("печкин")]
        [Discord.Commands.Summary("Панель временной почты PechkinPostManager")]
        public async Task Panel()
        {
            var boxes = DataBase.GetUserPpmMailboxes(Context.User.Id.ToString());
            await Context.Channel.SendMessageAsync(
                embed: PpmPanelBuilder.BuildRootEmbed(boxes, 0),
                components: PpmPanelBuilder.BuildRootComponents(boxes, 0));
        }
    }

    public class PpmInteractions : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly PpmService _ppm;

        public PpmInteractions(PpmService ppm)
        {
            _ppm = ppm;
        }

        [RequireSQDRoleInteraction]
        [ComponentInteraction("ppm_list:*")]
        public async Task ChangePage(string pageRaw)
        {
            if (!int.TryParse(pageRaw, out var page))
                page = 0;

            if (Context.Interaction is not SocketMessageComponent component)
                return;

            var boxes = DataBase.GetUserPpmMailboxes(Context.User.Id.ToString());

            await component.UpdateAsync(msg =>
            {
                msg.Embed = PpmPanelBuilder.BuildRootEmbed(boxes, page);
                msg.Components = PpmPanelBuilder.BuildRootComponents(boxes, page);
            });
        }

        [RequireSQDRoleInteraction]
        [ComponentInteraction("ppm_new:*")]
        public async Task Create(string kind)
        {
            await DeferAsync(ephemeral: true);

            bool permanent = kind == "perm";
            var (ok, box, error) = await _ppm.CreateAsync(Context.User.Id.ToString(), permanent);

            if (!ok)
            {
                await FollowupAsync(embed: EmbedHandler.Simple(
                    "❌ PPM", $"Не удалось создать ящик:\n```{Truncate(error, 500)}```",
                    Color.DarkRed, EmbedHandler.PpmFooter), ephemeral: true);
                return;
            }

            string ttl = permanent
                ? "♾️ постоянный"
                : $"⏳ удалится <t:{ToUnix(box.ExpiresAt!.Value)}:R>";

            await FollowupAsync(embed: EmbedHandler.Simple(
                "Временная почта", $"📬 **`{box.Email}`**\n\n{ttl}",
                Color.Green, EmbedHandler.PpmFooter), ephemeral: true);

            await RepaintRoot();
        }

        [RequireSQDRoleInteraction]
        [ComponentInteraction("ppm_open:*")]
        public Task Open(string idRaw) => ShowInbox(idRaw);

        [RequireSQDRoleInteraction]
        [ComponentInteraction("ppm_inbox:*")]
        public Task Refresh(string idRaw) => ShowInbox(idRaw);

        [RequireSQDRoleInteraction]
        [ComponentInteraction("ppm_del:*")]
        public async Task Delete(string idRaw)
        {
            await DeferAsync(ephemeral: true);

            var box = ResolveOwned(idRaw, out var deny);
            if (box == null)
            {
                await FollowupAsync(embed: EmbedHandler.Simple("❌ PPM", deny, Color.DarkRed, EmbedHandler.PpmFooter), ephemeral: true);
                return;
            }

            var (ok, error) = await _ppm.DeleteAsync(box);
            await FollowupAsync(embed: ok
                ? EmbedHandler.Simple("🗑️ PPM", $"Ящик `{box.Email}` удалён.", Color.Orange, EmbedHandler.PpmFooter)
                : EmbedHandler.Simple("❌ PPM", $"Не удалось удалить:\n```{Truncate(error, 500)}```", Color.DarkRed, EmbedHandler.PpmFooter),
                ephemeral: true);

            if (ok)
                await RepaintRoot();
        }

        private async Task ShowInbox(string idRaw)
        {
            await DeferAsync(ephemeral: true);

            var box = ResolveOwned(idRaw, out var deny);
            if (box == null)
            {
                await FollowupAsync(embed: EmbedHandler.Simple("❌ PPM", deny, Color.DarkRed, EmbedHandler.PpmFooter), ephemeral: true);
                return;
            }

            List<PpmMessageView> msgs;
            try
            {
                msgs = await _ppm.ReadInboxAsync(box);
            }
            catch (Exception ex)
            {
                await FollowupAsync(embed: EmbedHandler.Simple("❌ PPM", $"Ошибка IMAP:\n```{Truncate(ex.Message, 500)}```", Color.DarkRed, EmbedHandler.PpmFooter), ephemeral: true);
                return;
            }

            await FollowupAsync(
                embed: PpmPanelBuilder.BuildInboxEmbed(box, msgs),
                components: PpmPanelBuilder.BuildInboxComponents(box),
                ephemeral: true);
        }

        private async Task RepaintRoot()
        {
            if (Context.Interaction is not SocketMessageComponent component)
                return;

            var boxes = DataBase.GetUserPpmMailboxes(Context.User.Id.ToString());

            await component.Message.ModifyAsync(msg =>
            {
                msg.Embed = PpmPanelBuilder.BuildRootEmbed(boxes, 0);
                msg.Components = PpmPanelBuilder.BuildRootComponents(boxes, 0);
            });
        }

        private PpmMailbox ResolveOwned(string idRaw, out string deny)
        {
            deny = null;
            if (!int.TryParse(idRaw, out var id))
            {
                deny = "Некорректный идентификатор ящика";
                return null;
            }

            var box = DataBase.GetPpmMailboxById(id);
            if (box == null)
            {
                deny = "Ящик не найден или уже удалён";
                return null;
            }
            if (box.OwnerId != Context.User.Id.ToString())
            {
                deny = "Это не твой ящик";
                return null;
            }
            return box;
        }
    }
}
