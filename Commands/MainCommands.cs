using Discord;
using Discord.Commands;
using Discord.WebSocket;
using sblngavnav5X.Core;
using sblngavnav5X.Data;
using sblngavnav5X.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace sblngavnav5X.Commands;

public class MainCommands : ModuleBase<SocketCommandContext>
{
    private readonly DiscordSocketClient _client;

    public MainCommands(DiscordSocketClient client)
    {
        _client = client;
    }

    [Command("ава")]
    public async Task Avatar([Optional] string size, [Optional] IGuildUser User)
    {
        ushort sizen = 128;
        try
        {
            var sizer = size switch
            {
                null => sizen,
                "1" => sizen = 64,
                "2" => sizen = 256,
                "3" => sizen = 512,
                _ => sizen,
            };
        }
        catch
        {
            await ReplyAsync("такого размера нет, используй значение от 1 до 3😤");
        }

        if (User == null)
            User = (IGuildUser)Context.User;
        var eb = new EmbedBuilder();
        eb.WithColor(Color.Red);
        eb.WithImageUrl(User.GetAvatarUrl(ImageFormat.Auto, sizen));
        eb.WithAuthor("sbln аватарки👨‍🦲");
        await Context.Channel.SendMessageAsync("", false, eb.Build());
    }

    [Command("апт")]
    public async Task BotUptime()
    {
        await ReplyAsync(embed: EmbedHandler.Simple(
            "Время работы бота⌛",
            $"🦾 - {DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()}",
            Color.Blue,
            "sbln статистикс🔭"));
    }

    [Command("ст", RunMode = RunMode.Async)]
    [RequireOwner]
    public async Task SetStatus(string status, [Remainder] string args = null)
    {
        var statustype = status switch
        {
            "днд" => UserStatus.DoNotDisturb,
            "спит" => UserStatus.Idle,
            "инвиз" => UserStatus.Invisible,
            "онлайн" => UserStatus.Online,
            _ => UserStatus.Online
        };

        var last = DataBase.GetLastStatus();
        var type = string.IsNullOrEmpty(last.type) ? ActivityType.Playing.ToString() : last.type;
        var text = string.IsNullOrWhiteSpace(args) ? last.text : args;
        var link = type == ActivityType.Streaming.ToString() ? last.link : "";
        DataBase.AddStatus(text, status, link, type);

        await _client.SetStatusAsync(statustype);
        await _client.SetGameAsync(string.IsNullOrWhiteSpace(text) ? null : text, string.IsNullOrEmpty(link) ? null : link,
            Enum.TryParse<ActivityType>(type, out var activity) ? activity : ActivityType.Playing);

        await ReplyAsync(embed: EmbedHandler.Simple(
            string.Empty,
            $"Игра изменена на **{text}** со статусом **{status}** ✅",
            Color.Green,
            "sbln статус🎫"));
    }

    [Command("актив")]
    [RequireOwner]
    public async Task SetActivityAsync(string type, string linkOrText = null, [Remainder] string extra = null)
    {
        var actType = type switch
        {
            "стрим" => ActivityType.Streaming,
            "смотрит" => ActivityType.Watching,
            "слушает" => ActivityType.Listening,
            "соревнуется" => ActivityType.Competing,
            _ => ActivityType.Playing
        };
        string finalLink = null;
        string finalText = null;
        if (actType == ActivityType.Streaming)
        {
            finalLink = linkOrText;
            finalText = extra;
        }
        else
        {
            finalText = linkOrText;
            if (!string.IsNullOrWhiteSpace(extra)) finalText += " " + extra;
        }
        DataBase.AddStatus(finalText ?? "", DataBase.GetLastStatus().pos, finalLink ?? "", actType.ToString());
        await _client.SetGameAsync(finalText, finalLink, actType);

        await ReplyAsync(embed: EmbedHandler.Simple(
            string.Empty,
            $"Активность установлена: **{actType}** - **{finalText}** ✅",
            Color.Green,
            "sbln статус🎫"));
    }

    [Command("инфа разрабов")]
    [Alias("ир")]
    [RequireOwner]
    public async Task InfoDev()
    {
        var EmbedBuilder = new EmbedBuilder()
         .WithTitle("DEVS INFO")
         .WithDescription(
         $"🔹Бот успешно подключен к **{Context.Client.Guilds.Count}** серверам\n" +
         $"🔹Активность: **{Context.Client.Activity}**\n" +
         $"🔹Cостояние: **{Context.Client.ConnectionState}**\n" +
         $"🔹Состояние токена: **{Context.Client.TokenType} {Context.Client.LoginState}**\n" +
         $"🔹Задержка выполнения: **{Math.Max(0, Context.Client.Latency - 37)}ms **\n" +
         $"🔹Время процесса **{DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()}**\n" +
         $"🔹Идентификатор процесса: **{Environment.CurrentManagedThreadId}**\n" +
         $"🔹Директория процесса: **{Environment.CurrentDirectory}**\n" +
         $"🔹Имя системы: **{Environment.MachineName}**\n" +
         $"🔹OS: **{Environment.OSVersion}**\n" +
         $"🔹ОЗУ: **{Environment.SystemPageSize}mb**\n" +
         $"🔹Ядер процессора: **{Environment.ProcessorCount}**\n" +
         $"🔹Версия .NET: **{Environment.Version}**\n" +
         $"🔹Lavalink: **{Utils.lavaHost}:{Utils.lavaPort}**\n" +
         $"🔹Модули: {ModuleToggle.Summary()}\n"
         )
            .WithFooter(footer =>
            {
                footer
                .WithText("sbln статистикс🔭");
            });
        Embed embed = EmbedBuilder.Build();
        await ReplyAsync(embed: embed);
    }

    [Command("инфа")]
    public async Task Info()
    {
        string dopchik;
        if (Context.Guild.Description == "")
        {
            dopchik = "описания нет =(";
        }
        else
        {
            dopchik = (Context.Guild.Description);
        }
        var EmbedBuilder = new EmbedBuilder()
            .WithTitle("Основная информация о сервере 🤖")
            .WithDescription(
            $"🔹Название: ***`{Context.Guild.Name}`***\n" +
            $"🔹Описание: {dopchik} \n" +
            $"🔹Имя Бати: {Context.Guild.Owner.Username}#{Context.Guild.Owner.Discriminator} \n" +
            $"🔹ДР сервера: {Context.Guild.CreatedAt.UtcDateTime} \n" +
            $"🔹2FA: {Context.Guild.MfaLevel} \n" +
            $"🔹Уровень NSFW: {Context.Guild.NsfwLevel} \n" +
            $"🔹Скики ролей: {Context.Guild.Roles.Count} \n" +
            $"🔹Скики cмайликов: {Context.Guild.Emotes.Count} \n" +
            $"🔹Афк таймаут: {Context.Guild.AFKTimeout} сек \n" +
            $"🔹Бустеры: {Context.Guild.PremiumSubscriptionCount} \n" +
            $"🔹ГС каналы: {Context.Guild.VoiceChannels.Count}\n" +
            $"🔹Техтовые каналы: {Context.Guild.TextChannels.Count} \n" +
            $"🔹Челибасики: {Context.Guild.MemberCount}\n" +
            $"")
            .WithThumbnailUrl(Context.Guild.IconUrl)
            .WithFooter(footer =>
            {
                footer
                .WithText("sbln статистикс🔭");
            });
        Embed embed = EmbedBuilder.Build();
        await ReplyAsync(embed: embed);
    }

    [Command("анонс", RunMode = RunMode.Async)]
    [Cooldown(10)]
    public async Task AnnounceMessage([Remainder] string message)
    {
        string user = Context.User.Username;
        var guilds = _client.Guilds.ToList();
        foreach (var guild in guilds)
        {
            var messageChannel = guild.PublicUpdatesChannel as ISocketMessageChannel;
            if (messageChannel != null)
            {
                var e = new EmbedBuilder()
                {
                    Title = "анонс сына гавна",
                    Description = message,
                    ThumbnailUrl = Context.User.GetAvatarUrl(),
                    Footer = new EmbedFooterBuilder()
                    {
                        Text = $@"@{user}"
                    }
                };
                e.WithCurrentTimestamp();
                await messageChannel.SendMessageAsync(embed: e.Build());
                System.Threading.Thread.Sleep(5000);
            }
        }
    }

    [Command("удоли")]
    public async Task Clean(int max)
    {
        if(max < 1)
        {
            await ReplyAsync("ага, ага, нужно положительное число");
            return;
        }
        await ReplyAsync("легчайшее");
        var messages = Context.Channel.GetMessagesAsync(max + 2).Flatten();
        foreach (var h in await messages.ToArrayAsync())
        {
            await this.Context.Channel.DeleteMessageAsync(h);
        }
    }

    [Command("эхо")]
    [Cooldown(5)]
    public Task EchoAsync([Remainder] string text)
             => ReplyAsync('\u200B' + text, true);

    [Command("почта")]
    public async Task SendMailAsync(SocketGuildUser user = null, [Remainder] string message = null)
    {
        var hasAttachments = Context.Message.Attachments.Any();

        if (user == null)
        {
            await ReplyAsync("Укажи пользователя через @упоминание");
            return;
        }

        if (string.IsNullOrWhiteSpace(message) && !hasAttachments)
        {
            await ReplyAsync("Укажи сообщение или приложи вложение, которое нужно отправить");
            return;
        }

        var (isAnonymous, preparedMessage) = ParseMailMode(message);
        if (string.IsNullOrWhiteSpace(preparedMessage) && !hasAttachments)
        {
            await ReplyAsync("После флага анонимности нужно указать текст или приложить вложение");
            return;
        }

        try
        {
            var outgoingEmbed = new EmbedBuilder()
                .WithColor(isAnonymous ? Color.DarkGrey : Color.Blue)
                .WithTitle("📩 Вам письмо // sbln почта📧")
                .WithDescription(string.IsNullOrWhiteSpace(preparedMessage) ? "*пустое сообщение*" : preparedMessage)
                .WithFooter("↩️ Для ответа отправителю сделай реплай на это сообщение");

            if (isAnonymous)
            {
                outgoingEmbed.AddField("Отправитель", "Анонимно", true);
            }
            else
            {
                outgoingEmbed
                    .AddField("Отправитель:", $"{Context.User.Username}", true)
                    .AddField("Получатель:", $"{user.Username}", true);
            }

            if (hasAttachments)
            {
                var attachmentLinks = string.Join('\n', Context.Message.Attachments.Select(a => a.Url));
                outgoingEmbed.AddField("Вложения:", attachmentLinks);
            }

            var sentMessage = await user.SendMessageAsync(embed: outgoingEmbed.Build());
            CommandHandler.RegisterMailReplyRoute(sentMessage.Id, Context.User.Id, user.Id, isAnonymous);

            await LoggingService.LogInformationAsync(
                "XMAIL",
                $"SEND anonymous={isAnonymous} sender={Context.User.Id} recipient={user.Id} content={preparedMessage}");

            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithDescription($"Сообщение отправлено в ЛС: {user.Mention}")
                .AddField("Режим:", isAnonymous ? "Анон" : "Обычный", true)
                .AddField("Вложения:", Context.Message.Attachments.Count, true)
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .WithFooter("sbln почта📧")
                .Build();

            await ReplyAsync(embed: embed);
        }
        catch
        {
            var embed = new EmbedBuilder()
                .WithColor(Color.Red)
                .WithDescription($"Не удалось отправить сообщение {user.Mention} =(")
                .WithFooter("sbln почта📧")
                .Build();

            await ReplyAsync(embed: embed);
        }
    }

    [Command("версия")]
    public async Task BotVersionInfo()
    {
        var embed = new EmbedBuilder()
            .WithAuthor($"sblngavna ver {Utils.sblnver}")
            .WithDescription(
                "Внерелизная сборка ядра 5X, не входит в версионирование(!)\n" +
                ".NET 8 LTS / Victoria 7 / Discord.Net 3.20.1\n\n" +
                $"Модули: {ModuleToggle.Summary()}")
            .AddField("Собрано из",
                "[v5.6.1](https://lois.media/sbln/v5.6.1) - основа\n" +
                "[v6.0.0](https://lois.media/sbln/v6.0.0) - общие фиксы, обновление схемы данных")
            .WithColor(Color.LighterGrey)
            .WithFooter(footer => footer
                .WithText("part of Lois Media Group😋 \ndev by lucz@lois.media🏃")
                .WithIconUrl("https://cdn.betterttv.net/emote/5eef8ed979645a0dec34cc0a/3x"))
            .Build();

        await ReplyAsync(embed: embed);
    }

    [Command("позови")]
    [Cooldown(10)]
    public async Task CallUser(SocketGuildUser user)
    {
        int repeats = 8;
        string text = $"{user.Mention} ЗАЙДИ В ДС";
        var message = string.Join(" ", Enumerable.Repeat(text, repeats));
        await Context.Channel.SendMessageAsync(message);
    }

    [Command("пинг")]
    public async Task Ping()
    {
        await ReplyAsync($"🏓 понг ``{(Context.Client as DiscordSocketClient).Latency}ms`");
    }

    [Command("чел")]
    public async Task UserInfo(SocketGuildUser u = null)
    {
        SocketGuildUser user = u ?? (SocketGuildUser)Context.User;
        EmbedBuilder output = new EmbedBuilder();

        DateTimeOffset createdAt = user.CreatedAt;
        DateTimeOffset joinedAt = (DateTimeOffset)user.JoinedAt;
        string nickname = string.IsNullOrEmpty(user.Nickname) ? "" : $"({user.Nickname})";
        string dopchik;
        if (user.VoiceState.ToString() == "")
        {
            dopchik = "***нет***";
        }
        else
        {
            dopchik = (user.VoiceState.ToString());
        }

        output.WithTitle($"{user.Username} {nickname}")
            .AddField("Появился в Дискорде:", $"{createdAt} ({(DateTime.UtcNow - createdAt).Days} дней назад)")
            .AddField("Появился на этом сервере:", $"{joinedAt} ({(DateTime.UtcNow - joinedAt).Days} дней назад)")
            .AddField("Роли:", $"{string.Join(", ", user.Roles.ToList())}")
            .AddField("В войсе:", $"{dopchik}")
            .WithThumbnailUrl(user.GetAvatarUrl())
            .WithFooter(footer =>
            {
                footer
                .WithText("sbln статистикс🔭");
            });

        await ReplyAsync("", embed: output.Build());
    }

    [Command("кик")]
    [RequireUserPermission(GuildPermission.KickMembers, ErrorMessage = "тебе нельзя ты додик")]
    public async Task KickMember(SocketGuildUser user = null, [Remainder] string reason = null)
    {
        if (user == null)
        {
            await ReplyAsync("'выбери кентошарика'");
            return;
        }
        if (reason == null) reason = "воля администратора";

        await user.KickAsync();

        await ReplyAsync(embed: EmbedHandler.Moderation(
            "sbln кик <:roflanPominki:552795319516135424>",
            $":white_check_mark: {user.Mention} был кикнут с сервера **{Context.Guild.Name}** \n❓Причина: ***{reason}***",
            user.GetAvatarUrl(),
            Context.User.Username,
            Context.User.GetAvatarUrl()));
    }

    [Command("бан")]
    [RequireUserPermission(GuildPermission.BanMembers, ErrorMessage = "тебе нельзя ты додик")]
    public async Task BanMember(SocketGuildUser user = null, [Remainder] string reason = null)
    {
        if (user == null)
        {
            await ReplyAsync("'выбери кентошарика'");
            return;
        }
        if (reason == null) reason = "воля администратора";

        await user.BanAsync();

        await ReplyAsync(embed: EmbedHandler.Moderation(
            "sbln бан <:roflanPominki:552795319516135424>",
            $":white_check_mark: {user.Mention} был забанен на сервере **{Context.Guild.Name}** \n❓Причина: ***{reason}***",
            user.GetAvatarUrl(),
            Context.User.Username,
            Context.User.GetAvatarUrl()));
    }

    private static (bool isAnonymous, string preparedMessage) ParseMailMode(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return (false, string.Empty);

        var text = rawMessage.Trim();
        string[] anonymousPrefixes = new[] { "анонимно", "анон", "anon" };

        foreach (var prefix in anonymousPrefixes)
        {
            if (text.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return (true, string.Empty);

            if (text.StartsWith($"{prefix} ", StringComparison.OrdinalIgnoreCase))
                return (true, text[(prefix.Length + 1)..].Trim());
        }

        return (false, text);
    }
}
