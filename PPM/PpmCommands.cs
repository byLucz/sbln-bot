using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using sblngavnav6.Core;
using sblngavnav6.Common;
using sblngavnav6.Data;
using static sblngavnav6.Common.CommonUtils.Text;
using static sblngavnav6.Common.CommonUtils.Time;
using static sblngavnav6.Data.DataRoots;

namespace sblngavnav6.PPM
{
    public static class PpmPanelBuilder
    {
        private const int PageSize = 5;

        public static List<Embed> BuildRootPages(IReadOnlyList<PpmMailbox> boxes)
        {
            if (boxes.Count == 0)
                return [Page("У тебя нет активных ящиков, создай новый кнопкой ниже")];

            var pages = new List<Embed>(Pagination.TotalPages(boxes.Count, PageSize));

            for (var offset = 0; offset < boxes.Count; offset += PageSize)
            {
                var lines = boxes
                    .Skip(offset)
                    .Take(PageSize)
                    .Select(box => $"`{box.Email}` {Ttl(box)}");

                pages.Add(Page("Твои ящики:\n" + string.Join("\n", lines)));
            }

            return pages;
        }

        public static Action<ComponentBuilder, int> RootControls(IReadOnlyList<PpmMailbox> boxes) => (builder, page) =>
        {
            foreach (var box in boxes.Skip(page * PageSize).Take(PageSize))
                builder.WithButton($"📬 {box.Email.Split('@')[0]}", $"ppm_open:{box.Id}", ButtonStyle.Secondary, row: 1);

            builder.WithButton($"➕ {Global.Vars.Cfg.ppmTtlMinutes} мин", "ppm_new:temp", ButtonStyle.Success, row: 2);
            builder.WithButton("➕ навсегда", "ppm_new:perm", ButtonStyle.Success, row: 2);
        };

        public static Embed BuildInboxEmbed(PpmMailbox box, IReadOnlyList<PpmMessageView> msgs)
        {
            var fields = msgs
                .Select(msg => new EmbedFieldSpec(
                    Truncate($"✉️ {Truncate(msg.Subject, 200)} / {msg.Date:dd.MM HH:mm}", EmbedHandler.MaxFieldName),
                    Truncate($"**От:** {Truncate(msg.From, 200)}\n{Truncate(msg.Body, 700)}", EmbedHandler.MaxFieldValue)))
                .ToArray();

            return EmbedHandler.Build(new EmbedSpec
            {
                AuthorName = $"📬 {box.Email}",
                Description = msgs.Count == 0
                    ? $"{Ttl(box)}\n\nВходящих писем нет"
                    : $"{Ttl(box)}\n\nПоследние письма ({msgs.Count}):",
                Color = Color.Teal,
                Fields = fields,
                Footer = EmbedHandler.PpmFooter
            });
        }

        public static MessageComponent BuildInboxComponents(PpmMailbox box) =>
            new ComponentBuilder()
                .WithButton("🔄 Обновить", $"ppm_inbox:{box.Id}", ButtonStyle.Secondary, row: 0)
                .WithButton("🗑️ Удалить", $"ppm_del:{box.Id}", ButtonStyle.Danger, row: 0)
                .Build();

        public static string Ttl(PpmMailbox box) =>
            box.IsPermanent ? "♾️ постоянный"
            : box.ExpiresAt is DateTime expiresAt ? $"⏳ удалится <t:{ToUnix(expiresAt)}:R>"
            : "⏳ без срока";

        private static Embed Page(string description) => EmbedHandler.Build(new EmbedSpec
        {
            AuthorName = "📮 PechkinPostManager",
            Description = description,
            Color = Color.Teal,
            Footer = EmbedHandler.PpmFooter
        });
    }

    [RequireSuperuser]
    public class PpmCommands : ModuleBase<SocketCommandContext>
    {
        private readonly PaginatorService _pager;

        public PpmCommands(PaginatorService pager)
        {
            _pager = pager;
        }

        [Command("печкин")]
        [Alias("ппм","емейл")]

        public async Task Panel()
        {
            var boxes = DataBase.GetUserPpmMailboxes(Context.User.Id.ToString());

            await _pager.SendAsync(
                Context.Channel,
                PpmPanelBuilder.BuildRootPages(boxes),
                Context.User.Id,
                decorate: PpmPanelBuilder.RootControls(boxes));
        }
    }

    public class PpmInteractions : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly PpmService _ppm;
        private readonly PaginatorService _pager;

        public PpmInteractions(PpmService ppm, PaginatorService pager)
        {
            _ppm = ppm;
            _pager = pager;
        }

        [RequireSuperuserInteraction]
        [ComponentInteraction("ppm_new:*")]
        public async Task Create(string kind)
        {
            await DeferAsync(ephemeral: true);

            bool permanent = kind == "perm";
            var (ok, box, error, ready) = await _ppm.CreateAsync(Context.User.Id.ToString(), permanent);

            if (!ok)
            {
                await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed(
                    "печкин", $"не удалось создать ящик:\n```{Truncate(error, 500)}```"), ephemeral: true);
                return;
            }

            var note = ready
                ? "готов принимать письма"
                : "почтовик ещё применяет настройки, первые письма могут отбиться, попробуй через минуту";

            await FollowupAsync(embed: EmbedHandler.Simple(
                "Временная почта", $"📬 **`{box.Email}`**\n\n{PpmPanelBuilder.Ttl(box)}\n{(ready ? "✅" : "⚠️")} {note}",
                ready ? Color.Green : Color.Orange, EmbedHandler.PpmFooter), ephemeral: true);

            await RepaintRoot();
        }

        [RequireSuperuserInteraction]
        [ComponentInteraction("ppm_open:*")]
        public Task Open(string idRaw) => ShowInbox(idRaw);

        [RequireSuperuserInteraction]
        [ComponentInteraction("ppm_inbox:*")]
        public Task Refresh(string idRaw) => ShowInbox(idRaw);

        [RequireSuperuserInteraction]
        [ComponentInteraction("ppm_del:*")]
        public async Task Delete(string idRaw)
        {
            await DeferAsync(ephemeral: true);

            var box = ResolveOwned(idRaw, out var deny);
            if (box == null)
            {
                await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed("печкин", deny), ephemeral: true);
                return;
            }

            var (ok, error) = await _ppm.DeleteAsync(box);
            await FollowupAsync(embed: ok
                ? EmbedHandler.Simple("🗑️ PPM", $"Ящик `{box.Email}` удалён", Color.Orange, EmbedHandler.PpmFooter)
                : await EmbedHandler.CreateErrorEmbed("печкин", $"не удалось удалить:\n```{Truncate(error, 500)}```"),
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
                await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed("печкин", deny), ephemeral: true);
                return;
            }

            List<PpmMessageView> msgs;
            try
            {
                msgs = await _ppm.ReadInboxAsync(box);
            }
            catch (Exception ex)
            {
                await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed("печкин", $"IMAP отказал:\n```{Truncate(ex.Message, 500)}```"), ephemeral: true);
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

            await _pager.ReplaceAsync(
                component.Message,
                PpmPanelBuilder.BuildRootPages(boxes),
                Context.User.Id,
                decorate: PpmPanelBuilder.RootControls(boxes));
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
