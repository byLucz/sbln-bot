using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using sblngavnav5X.Services;
using sblngavnav5X.Data;

namespace sblngavnav5X.Commands;

public class MainCommands : ModuleBase<SocketCommandContext>
{
    private DiscordSocketClient _client;

    public IGuildUser User { get; private set; }

    public MainCommands(DiscordSocketClient client, CommandService commands)
    {
        _client = client;
    }

    //[Command("111")]
    //[RequireOwner]
    //public async Task ChangeLog()
    //{
    //    var EmbedBuilder = new EmbedBuilder()
    //    .WithDescription($"{Format.Bold($"devlog ver {Utils.sblnver}")} - Обновление внутреннего дизайна, MariaDB, sbln.portal и шефские рецепты\n" +
    //       $"• Начинается эра веб-панели sbln.portal, которая будет предоставлять полный coverage для ботика и открывать окна новых возможностей\n" +
    //       $"• Из основного: полное хранение данных в MariaDB, команда {Format.Bold("х рецепт")} для вкуснейших блюд от шефа и целая гора оптимизаций и улучшений\n" +
    //       $"• [Полный чендж-лог доступен на сайте 🌀](https://lois.media/sbln/v5.5.0)")
    //    .WithFooter(footer =>
    //    {
    //        footer
    //        .WithText("part of Lois Media Group😋 \ndev by lucz@lois.media🏃")
    //        .WithIconUrl("https://cdn.betterttv.net/emote/5eef8ed979645a0dec34cc0a/3x");
    //    });
    //    Embed embed = EmbedBuilder.Build();
    //    await ReplyAsync(embed: embed);
    //}

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

    public static string GetAvatarForUser(IGuildUser user, string defaultAvatarURL = "https://cdn-icons-png.flaticon.com/512/3670/3670157.png")
    {
        if (user != null)
        {
            string avatarUrl = user.GetGuildAvatarUrl();
            if (avatarUrl != null)
            {
                return avatarUrl;
            }
            avatarUrl = user.GetAvatarUrl();
            if (avatarUrl != null)
            {
                return avatarUrl;
            }
        }
        return defaultAvatarURL;
    }

    [Command("апт")]
    public async Task BotUptime()
    {
        var EmbedBuilder = new EmbedBuilder()
            .WithTitle("Время работы бота⌛")
            .WithDescription(
            $"🦾 - {DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()}" 
            )
               .WithFooter(footer =>
               {
                   footer
                   .WithText("sbln статистикс🔭");
               });
        Embed embed = EmbedBuilder.Build();
        await ReplyAsync(embed: embed);

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
    
        DataBase.AddStatus(args,status,"","");

        await _client.SetStatusAsync(statustype);
        await _client.SetGameAsync(args);

        var EmbedBuilder = new EmbedBuilder()
               .WithDescription($"Игра изменена на **{args}** со статусом **{status}** ✅")
               .WithFooter(footer =>
               {
                   footer
                   .WithText("sbln статус🎫");
               });
        Embed embed = EmbedBuilder.Build();
        await ReplyAsync(embed: embed);
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
        DataBase.AddStatus(finalText ?? "","", finalLink ?? "", actType.ToString());
        await _client.SetGameAsync(finalText, finalLink, actType);

        var EmbedBuilder = new EmbedBuilder()
               .WithDescription($"Активность установлена: **{actType}** - **{finalText}** ✅")
               .WithFooter(footer =>
               {
                   footer
                   .WithText("sbln статус🎫");
               });
        Embed embed = EmbedBuilder.Build();
        await ReplyAsync(embed: embed);
    }

    [Command("инфа разрабов")]
    [Alias("ир")]
    public async Task InfoDev()
    {
        var EmbedBuilder = new EmbedBuilder()
         .WithTitle("DEVS INFO")
         .WithDescription(
         $"🔹Бот успешно подключен к **{Context.Client.Guilds.Count}** серверам\n" +
         $"🔹Активность: **{Context.Client.Activity}**\n" +
         $"🔹Cостояние: **{Context.Client.ConnectionState}**\n" +
         $"🔹Состояние токена: **{Context.Client.TokenType} {Context.Client.LoginState}**\n" +
         $"🔹Задержка выполнения: **{Context.Client.Latency - 37}ms **\n" +
         $"🔹Время процесса **{DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()}**\n" +
         $"🔹Идентификатор процесса: **{Environment.CurrentManagedThreadId}**\n" +
         $"🔹Директория процесса: **{Environment.CurrentDirectory}**\n" +
         $"🔹Имя системы: **{Environment.MachineName}**\n" +
         $"🔹OS: **{Environment.OSVersion}**\n" +
         $"🔹ОЗУ: **{Environment.SystemPageSize}mb**\n" +
         $"🔹Ядер процессора: **{Environment.ProcessorCount}**\n" +
         $"🔹Версия .NET Core: **{Environment.Version}**\n" 
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
        static string GetHeapSize() => Math.Round(GC.GetTotalMemory(true) / (1024.0 * 1024.0), 2).ToString(CultureInfo.CurrentCulture);
        var EmbedBuilder = new EmbedBuilder()
            .WithTitle("Основная информация о сервере 🤖")
            .WithDescription(
            $"🔹Название: ***`{Context.Guild.Name}`***\n" +
            $"🔹Описание: {dopchik} \n" +
            $"🔹Имя Бати: {Context.Guild.Owner.Username}#{Context.Guild.Owner.Discriminator} \n" +
            $"🔹ДР сервера: {Context.Guild.CreatedAt.UtcDateTime} \n" +
            $"🔹2FA: {Context.Guild.MfaLevel} \n" +
            $"🔹Размер кучки: {GetHeapSize()} мб\n" +
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

    [Command("зал славы")]
    public async Task HallOfGlory()
    {
        var versions = DataBase.GetAllVersions();
        String verText = "";   
        foreach (var v in versions)
        {
            verText += ($"\n{v.Version} — *{v.Date:yyyy-MM-dd}*");
        }
        var EmbedBuilder = new EmbedBuilder()
            .WithTitle("все версии сыночка")
            .WithDescription(verText)
            .WithImageUrl("https://sun9-15.userapi.com/impg/c857332/v857332436/15bc51/aBM7tGgnmY0.jpg?size=640x640&quality=96&sign=4ea7c6c8b39104be3a21ae7033cc0283&type=album")
            .WithFooter(footer =>
            {
                footer
                .WithText("part of Lois Media Group😋 \ndev by lucz@lois.media🏃")
                .WithIconUrl("https://cdn.betterttv.net/emote/5eef8ed979645a0dec34cc0a/3x");
            });
        Embed embed = EmbedBuilder.Build();
        await ReplyAsync(embed: embed);
    }


    [Command("эхо")]
    [Cooldown(5)]
    public Task EchoAsync([Remainder] string text)
             => ReplyAsync('\u200B' + text, true);


    [Command("почта")]
    public async Task SendMailAsync(SocketGuildUser user = null, [Remainder] string message = null)
    {
        if (user == null)
        {
            await ReplyAsync("Укажи пользователя через @упоминание");
            return;
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            await ReplyAsync("Укажи сообщение, которое нужно отправить");
            return;
        }

        try
        {
            await user.SendMessageAsync(message);
            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithDescription($"Сообщение: `{message}` отправлено в ЛС: {user.Mention}")
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
        var botzname = new EmbedAuthorBuilder()
        .WithName($"sblngavna ver {Utils.sblnver}");
        var copy = new EmbedFooterBuilder()
        .WithText("part of Lois Media Group😋 \ndev by lucz@lois.media🏃")
            .WithIconUrl("https://cdn.betterttv.net/emote/5eef8ed979645a0dec34cc0a/3x");
        var r = new EmbedFieldBuilder()
        .WithName("Discord.Net")
        .WithValue("***3.18.0 (API v10)***");
        var r2 = new EmbedFieldBuilder()
        .WithName("Victoria")
        .WithValue("***7.0.5*** ");
        var r3 = new EmbedFieldBuilder()
        .WithName("TwitchLib")
        .WithValue("***3.1.1*** ");
        var r4 = new EmbedFieldBuilder()
        .WithName("Lavalink + YTPlugin")
        .WithValue("***4.0.8*** // ***1.13.4*** ");
        var r5 = new EmbedFieldBuilder()
        .WithName("GovorNGN (beta)")
        .WithValue("***1.5*** ");
        var r6 = new EmbedFieldBuilder()
        .WithName("MariaDB")
        .WithValue("***11.8.2*** ");
        var embed = new EmbedBuilder()
            .AddField(r)
            .AddField(r2)
            .AddField(r4)
            .AddField(r3)
            .AddField(r5)
            .AddField(r6)
            .WithAuthor(botzname)
            .WithFooter(copy)
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
        SocketGuildUser user = (SocketGuildUser)u;
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
            .AddField("Состояние:", $"{user.Status}")
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

        var EmbedBuilder = new EmbedBuilder()
            .WithTitle("sbln кик <:roflanPominki:552795319516135424>")
            .WithDescription($":white_check_mark: {user.Mention} был кикнут с сервера **{Context.Guild.Name}** \n❓Причина: ***{reason}***")
            .WithCurrentTimestamp()
            .WithThumbnailUrl(user.GetAvatarUrl())
            .WithColor(Color.DarkRed)
            .WithFooter(footer =>
            {
                footer
                .WithText($"приговор вынес {Context.User.Username}")
                .WithIconUrl(Context.User.GetAvatarUrl());
            });
        Embed embed = EmbedBuilder.Build();
        await ReplyAsync(embed: embed);
    }

    [Command("бан")]
    [RequireUserPermission(GuildPermission.KickMembers, ErrorMessage = "тебе нельзя ты додик")]
    public async Task BanMember(SocketGuildUser user = null, [Remainder] string reason = null)
    {
        if (user == null)
        {
            await ReplyAsync("'выбери кентошарика'");
            return;
        }
        if (reason == null) reason = "воля администратора";

        await user.BanAsync();

        var EmbedBuilder = new EmbedBuilder()
            .WithTitle("sbln бан <:roflanPominki:552795319516135424>")
            .WithDescription($":white_check_mark: {user.Mention} был забанен на сервере **{Context.Guild.Name}** \n❓Причина: ***{reason}***")
            .WithCurrentTimestamp()
            .WithThumbnailUrl(user.GetAvatarUrl())
            .WithColor(Color.DarkRed)
            .WithFooter(footer =>
            {
                footer
                .WithText($"приговор вынес {Context.User.Username}")
                .WithIconUrl(Context.User.GetAvatarUrl());
            });
        Embed embed = EmbedBuilder.Build();
        await ReplyAsync(embed: embed);
    }
}

