using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using sblngavnav6.Common;
using sblngavnav6.Core;
using sblngavnav6.Data;
using sblngavnav6.Services;
using System.Collections.Concurrent;
using static sblngavnav6.Common.CommonUtils.Chat;
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

        public static Action<ComponentBuilder, int> RootControls(IReadOnlyList<PpmMailbox> boxes, bool busy = false) => (builder, page) =>
        {
            foreach (var box in boxes.Skip(page * PageSize).Take(PageSize))
                builder.WithButton($"📬 {box.Email.Split('@')[0]}", $"ppm_open:{box.Id}", ButtonStyle.Secondary, row: 1, disabled: busy);

            builder.WithButton(
                busy ? "создаю…" : $"➕ {Global.Vars.Cfg.ppmTtlMinutes} мин",
                "ppm_new:temp",
                ButtonStyle.Success,
                row: 2,
                disabled: busy);

            builder.WithButton(
                busy ? "создаю…" : "➕ навсегда",
                "ppm_new:perm",
                ButtonStyle.Success,
                row: 2,
                disabled: busy);
        };

        public static Embed BuildInboxEmbed(PpmMailbox box, IReadOnlyList<PpmMessageView> msgs)
        {
            var fields = msgs
                .Select(msg => new EmbedFieldSpec(
                    Truncate($"✉️ {Truncate(msg.Subject, 200)} / {msg.Date:dd.MM HH:mm}{(msg.Folder is null ? "" : $" / {msg.Folder}")}", EmbedHandler.MaxFieldName),
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
            : box.ExpiresAt is DateTime expiresAt ? $"⏳ удалится {Stamp(expiresAt)}"
            : "⏳ без срока";

        private static Embed Page(string description) => EmbedHandler.Build(new EmbedSpec
        {
            AuthorName = "📮 PechkinPostManager",
            Description = description,
            Color = Color.Teal,
            Footer = EmbedHandler.PpmFooter
        });
    }

    [RequireDevGuild]
    [RequireSuperuser]
    public class PpmCommands : ModuleBase<SocketCommandContext>
    {
        private readonly PaginatorService _pager;
        private readonly PpmPanels _panels;
        private readonly CommandHandler _commandHandler;

        public PpmCommands(PaginatorService pager, PpmPanels panels, CommandHandler commandHandler)
        {
            _pager = pager;
            _panels = panels;
            _commandHandler = commandHandler;
        }

        [Command("почта")]
        public async Task SendMailAsync(SocketGuildUser user = null, [Remainder] string message = null)
        {
            var attachments = Context.Message.Attachments;

            if (user is null)
            {
                await FailAsync("внутренняя почта", "укажи пользователя через @упоминание");
                return;
            }

            if (string.IsNullOrWhiteSpace(message) && attachments.Count == 0)
            {
                await FailAsync("внутренняя почта", "укажи сообщение или приложи вложение");
                return;
            }

            var (isAnonymous, prepared) = ParseMailMode(message);

            if (string.IsNullOrWhiteSpace(prepared) && attachments.Count == 0)
            {
                await FailAsync("внутренняя почта", "после флага анонимности нужен текст или вложение");
                return;
            }

            var fields = new List<EmbedFieldSpec>();

            if (isAnonymous)
            {
                fields.Add(new EmbedFieldSpec("Отправитель", "Анонимно", true));
            }
            else
            {
                fields.Add(new EmbedFieldSpec("Отправитель:", Context.User.Username, true));
                fields.Add(new EmbedFieldSpec("Получатель:", user.Username, true));
            }

            if (attachments.Count > 0)
                fields.Add(new EmbedFieldSpec("Вложения:", string.Join('\n', attachments.Select(attachment => attachment.Url))));

            try
            {
                var sent = await user.SendMessageAsync(embed: EmbedHandler.Build(new EmbedSpec
                {
                    Title = "📩 Вам письмо // sbln внутренняя почта📧",
                    Description = string.IsNullOrWhiteSpace(prepared) ? "*пустое сообщение*" : prepared,
                    Color = isAnonymous ? Color.DarkGrey : Color.Blue,
                    Fields = fields,
                    Footer = "↩️ Для ответа отправителю сделай реплай на это сообщение"
                }));

                _commandHandler.RegisterMailReplyRoute(sent.Id, Context.User.Id, user.Id, isAnonymous);

                await LoggingService.LogInformationAsync(
                    "XMAIL",
                    $"SEND anonymous={isAnonymous} sender={Context.User.Id} recipient={user.Id} contentLength={prepared.Length}");

                await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
                {
                    Description = $"Сообщение отправлено в ЛС: {user.Mention}",
                    Color = Color.Green,
                    ThumbnailUrl = Avatar(user),
                    Fields =
                    [
                        new EmbedFieldSpec("Режим:", isAnonymous ? "Анон" : "Обычный", true),
                        new EmbedFieldSpec("Вложения:", attachments.Count.ToString(), true)
                    ],
                    Footer = "sbln внутренняя почта📧"
                }));
            }
            catch (Exception ex) when (ex is Discord.Net.HttpException or TimeoutException)
            {
                await FailAsync("внутренняя почта", $"не удалось доставить письмо {user.Mention}, возможно у него закрыты личные сообщения");
            }
        }

        private static (bool IsAnonymous, string Prepared) ParseMailMode(string rawMessage)
        {
            if (string.IsNullOrWhiteSpace(rawMessage))
                return (false, string.Empty);

            var text = rawMessage.Trim();

            foreach (var prefix in new[] { "анонимно", "анон", "anon" })
            {
                if (text.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    return (true, string.Empty);

                if (text.StartsWith($"{prefix} ", StringComparison.OrdinalIgnoreCase))
                    return (true, text[(prefix.Length + 1)..].Trim());
            }

            return (false, text);
        }

        [Command("печкин")]
        [Alias("ппм", "емейл")]

        public async Task Panel()
        {
            var boxes = await DataBase.GetUserPpmMailboxes(Context.User.Id.ToString());

            var panel = await _pager.SendAsync(
                Context.Channel,
                PpmPanelBuilder.BuildRootPages(boxes),
                Context.User.Id,
                decorate: PpmPanelBuilder.RootControls(boxes));

            _panels.Track(Context.User.Id, panel);
        }
        private async Task FailAsync(string source, string reason) =>
            await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed(source, reason));
    }

    public sealed class PpmNameModal : IModal
    {
        public const string Id = "ppm_name";

        public string Title => "Постоянный ящик";

        [InputLabel("Подпись до собачки")]
        [ModalTextInput("local", placeholder: "например daun", minLength: 3, maxLength: 32)]
        public string Local { get; set; }

        [InputLabel("Пароль")]
        [RequiredInput(false)]
        [ModalTextInput("pass", placeholder: "пусто - сгенерирую сам", maxLength: 64)]
        public string Password { get; set; }
    }

    [RequireDevGuildInteraction]
    public class PpmInteractions : InteractionModuleBase<SocketInteractionContext>
    {
        private static readonly ConcurrentDictionary<ulong, byte> _creating = new();

        private readonly PpmService _ppm;
        private readonly PaginatorService _pager;
        private readonly PpmPanels _panels;

        public PpmInteractions(PpmService ppm, PaginatorService pager, PpmPanels panels)
        {
            _ppm = ppm;
            _pager = pager;
            _panels = panels;
        }

        [RequireSuperuserInteraction]
        [ComponentInteraction("ppm_new:*")]
        public async Task Create(string kind)
        {
            if (kind == "perm")
            {
                await RespondWithModalAsync<PpmNameModal>(PpmNameModal.Id);
                return;
            }

            await CreateCoreAsync(false, null, null);
        }

        [RequireSuperuserInteraction]
        [ModalInteraction(PpmNameModal.Id)]
        public async Task Named(PpmNameModal modal) => await CreateCoreAsync(true, modal.Local, modal.Password);

        private async Task CreateCoreAsync(bool permanent, string wanted, string password)
        {
            await DeferAsync(ephemeral: true);

            if (!_creating.TryAdd(Context.User.Id, 0))
            {
                await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed(
                    "печкин", "ящик уже создаётся, подожди пару секунд"), ephemeral: true);
                return;
            }

            try
            {
                await RepaintRoot(busy: true);

                var (ok, box, error, ready) = await _ppm.CreateAsync(Context.User.Id.ToString(), permanent, wanted, password);

                if (!ok)
                {
                    await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed(
                        "печкин", $"не удалось создать ящик:\n```{Truncate(error, 500)}```"), ephemeral: true);
                    return;
                }

                var note = ready
                    ? "готов принимать письма"
                    : "почтовик ещё применяет настройки, первые письма могут отбиться, попробуй через минуту";

                var credentials = permanent
                    ? $"\nпароль: ||`{box.Password}`||"
                    : "";

                await FollowupAsync(embed: EmbedHandler.Simple(
                    permanent ? "Постоянная почта" : "Временная почта",
                    $"📬 **`{box.Email}`**{credentials}\n\n{PpmPanelBuilder.Ttl(box)}\n{(ready ? "✅" : "⚠️")} {note}",
                    ready ? Color.Green : Color.Orange, EmbedHandler.PpmFooter), ephemeral: true);
            }
            finally
            {
                _creating.TryRemove(Context.User.Id, out _);
                await RepaintRoot();
            }
        }

        [RequireSuperuserInteraction]
        [ComponentInteraction("ppm_open:*")]
        public Task Open(string idRaw) => ShowInbox(idRaw, inPlace: false);

        [RequireSuperuserInteraction]
        [ComponentInteraction("ppm_inbox:*")]
        public Task Refresh(string idRaw) => ShowInbox(idRaw, inPlace: true);

        [RequireSuperuserInteraction]
        [ComponentInteraction("ppm_del:*")]
        public async Task Delete(string idRaw)
        {
            await DeferAsync(ephemeral: true);

            var (box, deny) = await ResolveOwnedAsync(idRaw);
            if (box == null)
            {
                await ReportAsync(await EmbedHandler.CreateErrorEmbed("печкин", deny), inPlace: true);
                return;
            }

            var (ok, error) = await _ppm.DeleteAsync(box);

            await ReportAsync(ok
                ? EmbedHandler.Simple("🗑️ PPM", $"Ящик `{box.Email}` удалён", Color.Orange, EmbedHandler.PpmFooter)
                : await EmbedHandler.CreateErrorEmbed("печкин", $"не удалось удалить:\n```{Truncate(error, 500)}```"),
                inPlace: true,
                dropControls: ok);

            if (ok)
                await _panels.RefreshAsync(Context.User.Id);
        }

        private async Task ShowInbox(string idRaw, bool inPlace)
        {
            await DeferAsync(ephemeral: true);

            var (box, deny) = await ResolveOwnedAsync(idRaw);
            if (box == null)
            {
                await ReportAsync(await EmbedHandler.CreateErrorEmbed("печкин", deny), inPlace);
                return;
            }

            List<PpmMessageView> msgs;
            try
            {
                msgs = await _ppm.ReadInboxAsync(box);
            }
            catch (Exception ex)
            {
                await ReportAsync(await EmbedHandler.CreateErrorEmbed("печкин", $"IMAP отказал:\n```{Truncate(ex.Message, 500)}```"), inPlace);
                return;
            }

            var embed = PpmPanelBuilder.BuildInboxEmbed(box, msgs);
            var controls = PpmPanelBuilder.BuildInboxComponents(box);

            if (inPlace)
            {
                await ModifyOriginalResponseAsync(message =>
                {
                    message.Embed = embed;
                    message.Components = controls;
                });

                return;
            }

            await FollowupAsync(embed: embed, components: controls, ephemeral: true);
        }

        private async Task ReportAsync(Embed embed, bool inPlace, bool dropControls = false)
        {
            if (!inPlace)
            {
                await FollowupAsync(embed: embed, ephemeral: true);
                return;
            }

            await ModifyOriginalResponseAsync(message =>
            {
                message.Embed = embed;

                if (dropControls)
                    message.Components = new ComponentBuilder().Build();
            });
        }

        private async Task RepaintRoot(bool busy = false)
        {
            if (Context.Interaction is not SocketMessageComponent component ||
                component.Message.Flags?.HasFlag(MessageFlags.Ephemeral) == true)
            {
                await _panels.RefreshAsync(Context.User.Id);
                return;
            }

            _panels.Track(Context.User.Id, component.Message);

            var boxes = await DataBase.GetUserPpmMailboxes(Context.User.Id.ToString());

            await _pager.ReplaceAsync(
                component.Message,
                PpmPanelBuilder.BuildRootPages(boxes),
                Context.User.Id,
                decorate: PpmPanelBuilder.RootControls(boxes, busy));
        }

        private async Task<(PpmMailbox Box, string Deny)> ResolveOwnedAsync(string idRaw)
        {
            if (!int.TryParse(idRaw, out var id))
                return (null, "Некорректный идентификатор ящика");

            var box = await DataBase.GetPpmMailboxById(id);

            if (box is null)
                return (null, "Ящик не найден или уже удалён");

            if (box.OwnerId != Context.User.Id.ToString())
                return (null, "Это не твой ящик");

            return (box, null);
        }
    }
}
