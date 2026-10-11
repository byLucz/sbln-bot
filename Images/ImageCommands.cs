using Discord;
using Discord.Commands;
using SkiaSharp;
using sblngavnav6.Common;
using sblngavnav6.Core;
using sblngavnav6.Services;
using static sblngavnav6.Common.CommonUtils.Chat;
using static sblngavnav6.Common.CommonUtils.Text;

namespace sblngavnav6.Images
{
    [RequireGuild]
    public sealed class ImageCommands : ModuleBase<SocketCommandContext>
    {
        private const string Footer = "sbln картинки🖼️";
        private const string NoPicture = "прикрепи картинку, ответь на сообщение с ней или дай ссылку";

        private readonly HttpClient _http;

        private string _command = "картинки";

        public ImageCommands(IHttpClientFactory httpClientFactory)
        {
            _http = httpClientFactory.CreateClient();
            _http.Timeout = TimeSpan.FromSeconds(15);
        }

        protected override void BeforeExecute(CommandInfo command) => _command = command.Name;

        [Command("обоссать")]
        [Cooldown(15)]
        public async Task PissAsync(IUser user = null)
        {
            var target = user ?? Context.Message.ReferencedMessage?.Author;

            if (target is null)
            {
                await FailAsync("укажи кого: `обоссать @юзер` или ответом на сообщение");
                return;
            }

            var attackerBytes = await AvatarBytesAsync(Context.User);
            var victimBytes = await AvatarBytesAsync(target);

            await RenderAsync("💦 Обоссать", "piss.gif", $"{Context.User.Mention} обоссал {target.Mention}", async () =>
            {
                using var attacker = ImageTools.Circle(attackerBytes, 160);
                using var victim = ImageTools.Circle(victimBytes, 160);

                return await ImageTools.EncodeGifAsync(
                    PissScene.Width, PissScene.Height, PissScene.Fps,
                    new PissScene(attacker, victim).Render());
            });
        }

        [Command("монетка")]
        [Alias("монета", "коин")]
        [Cooldown(10)]
        public async Task CoinAsync(IUser first = null, IUser second = null)
        {
            var (heads, tails) = second is null
                ? (Context.User, first ?? Context.Message.ReferencedMessage?.Author)
                : (first, second);

            if (tails is null)
            {
                await FailAsync("укажи соперника: `монетка @юзер` или `монетка @юзер1 @юзер2`");
                return;
            }

            var headsWins = CommonUtils.RandomNumber(0, 1) == 0;
            var headsBytes = await AvatarBytesAsync(heads);
            var tailsBytes = await AvatarBytesAsync(tails);

            await RenderAsync("🪙 Монетка", "coin.gif", $"{heads.Mention} против {tails.Mention}\nвыпал: ||{(headsWins ? heads : tails).Mention}||", async () =>
            {
                using var headsImage = ImageTools.Circle(headsBytes, 160);
                using var tailsImage = ImageTools.Circle(tailsBytes, 160);

                return await ImageTools.EncodeGifAsync(
                    CoinScene.Width, CoinScene.Height, CoinScene.Fps,
                    new CoinScene(headsImage, tailsImage, headsWins).Render());
            });
        }

        [Command("демотиватор")]
        [Alias("демот")]
        [Cooldown(5)]
        public async Task DemotivatorAsync([Remainder] string args = "")
        {
            var text = CleanText(args).Split('|', 2, StringSplitOptions.TrimEntries);

            if (string.IsNullOrWhiteSpace(text[0]))
            {
                await FailAsync("нужен текст: `демотиватор заголовок | подпись`");
                return;
            }

            using var source = await SourceAsync(args)
                ?? ImageTools.Decode(await AvatarBytesAsync(Mentioned(args) ?? Context.Message.ReferencedMessage?.Author ?? Context.User));

            if (source is null)
            {
                await FailAsync("не смог открыть картинку");
                return;
            }

            await RenderAsync("🖼️ Демотиватор", "demotivator.jpg", null, () =>
                Task.FromResult(ImageEffects.Demotivator(source, Truncate(text[0], 120), text.Length > 1 ? Truncate(text[1], 240) : null)));
        }

        [Command("шефограм")]
        [Alias("шфгр")]
        [Cooldown(10)]
        public async Task SheffogramAsync([Remainder] string args = "")
        {
            using var source = await SourceAsync(args);

            if (source is null)
            {
                await FailAsync(NoPicture);
                return;
            }

            var faces = await Task.Run(() => FaceDetector.Detect(source));

            if (faces.Count == 0)
            {
                await FailAsync("на картинке не обнаружено ебальничка");
                return;
            }

            var mentioned = Mentioned(args);
            var avatarBytes = mentioned is null ? null : await AvatarBytesAsync(mentioned);

            await RenderAsync("🧑‍🍳 Шефограм", "sheffogram.jpg", $"⬇️ {(faces.Count > 1 ? "еще челы превращены" : "еще один челик превращен")} в {(mentioned is null ? "шефа" : mentioned.Mention)}... ⬇️", () =>
            {
                using var avatar = mentioned is null ? null : ImageTools.Circle(avatarBytes, 256);

                return Task.FromResult(ImageEffects.Sheffogram(source, avatar ?? ImageEffects.Sheff.Value,
                    avatar is null ? ImageEffects.SheffPoints : ImageEffects.AvatarPoints, faces));
            });
        }

        [Command("скан")]
        [Alias("сканер")]
        [Cooldown(10)]
        public async Task ScanAsync([Remainder] string args = "")
        {
            var languages = args.Contains("енг", StringComparison.OrdinalIgnoreCase) ? "eng"
                : args.Contains("рус", StringComparison.OrdinalIgnoreCase) ? "rus"
                : "rus+eng";

            using var source = await SourceAsync(args);

            if (source is null)
            {
                await FailAsync(NoPicture);
                return;
            }

            using var typing = Context.Channel.EnterTypingState();

            string text;
            await ImageTools.RenderGate.WaitAsync();
            try
            {
                text = (await ImageTools.RecognizeTextAsync(source, languages)).Trim();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            {
                await LoggingService.LogErrorAsync("IMAGE", "Не удалось распознать текст", ex);
                await FailAsync("сканер не сработал, детали в логах");
                return;
            }
            finally
            {
                ImageTools.RenderGate.Release();
            }

            if (text.Length == 0)
            {
                await FailAsync("текста на картинке не нашёл");
                return;
            }

            await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
            {
                Title = "🖨️ Скан",
                Description = Truncate(text, EmbedHandler.MaxDescription),
                Color = Color.Gold,
                Footer = $"{Footer} / {languages}"
            }));
        }

        private async Task RenderAsync(string title, string fileName, string description, Func<Task<byte[]>> render)
        {
            using var typing = Context.Channel.EnterTypingState();

            byte[] result;
            await ImageTools.RenderGate.WaitAsync();
            try
            {
                result = await render();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            {
                await LoggingService.LogErrorAsync("IMAGE", $"Не удалось собрать {fileName}", ex);
                await FailAsync("картинка не собралась, детали в логах");
                return;
            }
            finally
            {
                ImageTools.RenderGate.Release();
            }

            using var stream = new MemoryStream(result);

            await Context.Channel.SendFileAsync(stream, fileName, embed: EmbedHandler.Build(new EmbedSpec
            {
                Title = title,
                Description = description,
                ImageUrl = $"attachment://{fileName}",
                Color = Color.Gold,
                Footer = Footer
            }));
        }

        private async Task<SKBitmap> SourceAsync(string args)
        {
            var referenced = Context.Message.ReferencedMessage;

            var candidates = Context.Message.Attachments.Where(ImageTools.IsImage).Select(attachment => attachment.Url)
                .Concat(referenced?.Attachments.Where(ImageTools.IsImage).Select(attachment => attachment.Url) ?? [])
                .Concat(referenced?.Embeds.Select(ImageTools.EmbedImage) ?? [])
                .Concat(Words(args).Where(word => EmbedHandler.Link(word) is not null));

            foreach (var url in candidates.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct())
            {
                if (ImageTools.Decode(await CommonUtils.Web.DownloadAsync(_http, url, ImageTools.MaxSourceBytes)) is { } bitmap)
                    return bitmap;
            }

            return null;
        }

        private IUser Mentioned(string args) =>
            Context.Message.MentionedUsers.FirstOrDefault(user => Words(args).Any(word => MentionUtils.TryParseUser(word, out var id) && id == user.Id));

        private static IEnumerable<string> Words(string args) =>
            (args ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

        private static string CleanText(string args) =>
            string.Join(" ", Words(args).Where(word => EmbedHandler.Link(word) is null && !MentionUtils.TryParseUser(word, out _)));

        private Task<byte[]> AvatarBytesAsync(IUser user) =>
            CommonUtils.Web.DownloadAsync(_http, user is IGuildUser member ? GuildAvatar(member) : Avatar(user, 256), ImageTools.MaxSourceBytes);

        private async Task FailAsync(string reason) =>
            await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed(_command, reason));
    }
}
