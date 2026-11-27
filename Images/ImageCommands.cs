using Discord;
using Discord.Commands;
using DiscordTelegramFrontier;
using SkiaSharp;
using sblngavnav6.Common;
using sblngavnav6.GVR;
using sblngavnav6.Services;
using static sblngavnav6.Common.CommonUtils.Chat;
using static sblngavnav6.Common.CommonUtils.Text;

namespace sblngavnav6.Images
{
    public sealed class ImageCommands : ModuleBase<SocketCommandContext>
    {
        private const string Author = "sbln картинки🖼️";
        private const string Footer = "powered by SkiaSharp";
        private const string NoPicture = "прикрепи картинку, ответь на сообщение с ней или дай ссылку";
        private const int MaxUrlCandidates = 5;

        private readonly HttpClient _http;
        private readonly GVRMessagesHandler _gvr;

        private string _command = "картинки";

        public ImageCommands(IHttpClientFactory httpClientFactory, GVRMessagesHandler gvr)
        {
            _gvr = gvr;
            _http = httpClientFactory.CreateClient(CommonUtils.Web.PublicClient);
            _http.Timeout = TimeSpan.FromSeconds(15);
        }

        protected override void BeforeExecute(CommandInfo command) => _command = command.Name;

        [Command("обоссать")]
        [Cooldown(5)]
        public async Task PissAsync(IUser user = null)
        {
            var target = user ?? Context.Message?.ReferencedMessage?.Author;

            if (target is null)
            {
                await FailAsync("укажи кого: `обоссать @юзер` или ответом на сообщение");
                return;
            }

            var attacker = Context.Sender();
            var attackerBytes = await AvatarBytesAsync(attacker);
            var victimBytes = await AvatarBytesAsync(target);

            await RenderAsync("piss.gif", $"{attacker.Mention} обоссал {target.Mention} 💦", async () =>
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
        [Cooldown(5)]
        public async Task CoinAsync(IUser first = null, IUser second = null)
        {
            var (heads, tails) = second is null
                ? (Context.Sender(), first ?? Context.Message?.ReferencedMessage?.Author)
                : (first, second);

            if (tails is null)
            {
                await FailAsync("укажи соперника: `монетка @юзер` или `монетка @юзер1 @юзер2`");
                return;
            }

            var headsWins = CommonUtils.RandomNumber(0, 1) == 0;
            var headsBytes = await AvatarBytesAsync(heads);
            var tailsBytes = await AvatarBytesAsync(tails);

            await RenderAsync("coin.gif", $"🪙 {heads.Mention} против {tails.Mention}\nвыпал: ||{(headsWins ? heads : tails).Mention}||", async () =>
            {
                using var headsImage = ImageTools.Circle(headsBytes, 160);
                using var tailsImage = ImageTools.Circle(tailsBytes, 160);

                return await ImageTools.EncodeGifAsync(
                    CoinScene.Width, CoinScene.Height, CoinScene.Fps,
                    new CoinScene(headsImage, tailsImage, headsWins).Render());
            });
        }

        [Frontier]
        [Command("демотиватор")]
        [Alias("демот")]
        [Cooldown(5)]
        public async Task DemotivatorAsync([Remainder] string args = "")
        {
            var text = CleanText(args).Split('|', 2, StringSplitOptions.TrimEntries);

            if (string.IsNullOrWhiteSpace(text[0]))
            {
                if (!await _gvr.SendDemotivatorAsync(Context))
                    await FailAsync("говорилке пока не из чего собрать демотиватор, нужен текст: `демотиватор заголовок | подпись`");
                return;
            }

            var bytes = await SourceBytesAsync(args)
                ?? await AvatarBytesAsync(Mentioned(args) ?? Context.Message?.ReferencedMessage?.Author ?? Context.Sender());

            if (bytes is null)
            {
                await FailAsync("не смог открыть картинку");
                return;
            }

            await RenderAsync("demotivator.jpg", null, () =>
            {
                using var source = ImageTools.Decode(bytes);
                return Task.FromResult(source is null
                    ? null
                    : ImageEffects.Demotivator(source, Truncate(text[0], 120), text.Length > 1 ? Truncate(text[1], 240) : null));
            }, "не смог открыть картинку");
        }

        [Frontier]
        [Command("шефограм")]
        [Alias("шфгр")]
        [Cooldown(5)]
        public async Task SheffogramAsync([Remainder] string args = "")
        {
            var bytes = await SourceBytesAsync(args);

            if (bytes is null)
            {
                await FailAsync(NoPicture);
                return;
            }

            var mentioned = Mentioned(args);
            var avatarBytes = mentioned is null ? null : await AvatarBytesAsync(mentioned);
            string failReason = null;
            byte[] rendered = null;
            var faceCount = 0;

            using (Context.Channel.EnterTypingState())
            {
                await ImageTools.RenderGate.WaitAsync();
                try
                {
                    (rendered, faceCount, failReason) = await Task.Run(() =>
                    {
                        using var source = ImageTools.Decode(bytes);
                        if (source is null)
                            return ((byte[])null, 0, "не смог открыть картинку");

                        var faces = FaceDetector.Detect(source);
                        if (faces.Count == 0)
                            return ((byte[])null, 0, "на картинке не обнаружено ебальничка");

                        using var avatar = mentioned is null ? null : ImageTools.Circle(avatarBytes, 256);
                        var output = ImageEffects.Sheffogram(source, avatar ?? ImageEffects.Sheff.Value,
                            avatar is null ? ImageEffects.SheffPoints : ImageEffects.AvatarPoints, faces);
                        return (output, faces.Count, (string)null);
                    });
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
                {
                    await LoggingService.LogErrorAsync("IMAGE", "Не удалось собрать sheffogram.jpg", ex);
                    await FailAsync("картинка не собралась, детали в логах");
                    return;
                }
                finally
                {
                    ImageTools.RenderGate.Release();
                }
            }

            if (failReason is not null)
            {
                await FailAsync(failReason);
                return;
            }

            using var stream = new MemoryStream(rendered);
            await Context.Channel.SendFileAsync(stream, "sheffogram.jpg", embed: EmbedHandler.Build(new EmbedSpec
            {
                AuthorName = Author,
                Description = $"⬇️ {(faceCount > 1 ? "еще челы превращены" : "еще один челик превращен")} в {(mentioned is null ? "шефа" : mentioned.Mention)}... ⬇️",
                ImageUrl = "attachment://sheffogram.jpg",
                Color = Color.Gold,
                Footer = Footer
            }));
        }

        [Frontier]
        [Command("скан")]
        [Alias("сканер")]
        [Cooldown(5)]
        public async Task ScanAsync([Remainder] string args = "")
        {
            var languages = args.Contains("енг", StringComparison.OrdinalIgnoreCase) ? "eng"
                : args.Contains("рус", StringComparison.OrdinalIgnoreCase) ? "rus"
                : "rus+eng";

            var bytes = await SourceBytesAsync(args);

            if (bytes is null)
            {
                await FailAsync(NoPicture);
                return;
            }

            using var typing = Context.Channel.EnterTypingState();

            string text;
            await ImageTools.RenderGate.WaitAsync();
            try
            {
                using var source = ImageTools.Decode(bytes);
                if (source is null)
                {
                    await FailAsync("не смог открыть картинку");
                    return;
                }

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
                AuthorName = Author,
                Description = Truncate(text, EmbedHandler.MaxDescription),
                Color = Color.Gold,
                Footer = $"powered by Tesseract / {languages}"
            }));
        }

        private async Task RenderAsync(string fileName, string description, Func<Task<byte[]>> render, string emptyError = null)
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

            if (result is null)
            {
                await FailAsync(emptyError ?? "не смог открыть картинку");
                return;
            }

            using var stream = new MemoryStream(result);

            await Context.Channel.SendFileAsync(stream, fileName, embed: EmbedHandler.Build(new EmbedSpec
            {
                AuthorName = Author,
                Description = description,
                ImageUrl = $"attachment://{fileName}",
                Color = Color.Gold,
                Footer = Footer
            }));
        }

        private async Task<byte[]> SourceBytesAsync(string args)
        {
            if (await Context.IncomingImageAsync(ImageTools.MaxSourceBytes) is { Length: > 0 } sent && ImageTools.CanDecode(sent))
                return sent;

            var referenced = Context.Message?.ReferencedMessage;

            var candidates = (Context.Message?.Attachments ?? []).Where(ImageTools.IsImage).Select(attachment => attachment.Url)
                .Concat(referenced?.Attachments.Where(ImageTools.IsImage).Select(attachment => attachment.Url) ?? [])
                .Concat(referenced?.Embeds.Select(ImageTools.EmbedImage) ?? [])
                .Concat(Words(args).Where(word => EmbedHandler.Link(word) is not null));

            foreach (var url in candidates.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct().Take(MaxUrlCandidates))
            {
                var bytes = await CommonUtils.Web.DownloadAsync(_http, url, ImageTools.MaxSourceBytes);
                if (ImageTools.CanDecode(bytes))
                    return bytes;
            }

            return null;
        }

        private IUser Mentioned(string args) =>
            Context.Message?.MentionedUsers.FirstOrDefault(user => Words(args).Any(word => MentionUtils.TryParseUser(word, out var id) && id == user.Id));

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
