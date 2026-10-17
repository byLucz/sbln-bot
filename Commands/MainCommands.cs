using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DiscordTelegramFrontier;
using sblngavnav6.Audio8;
using sblngavnav6.Common;
using sblngavnav6.Core;
using sblngavnav6.Data;
using sblngavnav6.GVR;
using sblngavnav6.Services;
using System.Data;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using static sblngavnav6.Common.CommonUtils.Chat;
using static sblngavnav6.Common.CommonUtils.Text;
using static sblngavnav6.Common.CommonUtils.Time;

namespace sblngavnav6.Commands;

public class MainCommands : ModuleBase<SocketCommandContext>
{
    private const string ReleaseFeed = "https://lois.media/sbln/rss.xml";

    private readonly DiscordSocketClient _client;
    private readonly CommandHandler _commandHandler;
    private readonly Audio8Service _audio;
    private readonly GVRDb _gvr;
    private readonly IHttpClientFactory _httpClientFactory;

    public MainCommands(DiscordSocketClient client, CommandHandler commandHandler, Audio8Service audio, GVRDb gvr, IHttpClientFactory httpClientFactory)
    {
        _client = client;
        _commandHandler = commandHandler;
        _audio = audio;
        _gvr = gvr;
        _httpClientFactory = httpClientFactory;
    }

    private static readonly Regex MathSafeRegex = new(@"^[\d\s\+\-\*\/\(\)\.]+$", RegexOptions.Compiled);

    [Command("рсс")]
    [RequireOwner(ErrorMessage = "недостаточно прав")]
    public async Task ReleaseAsync(string version = null)
    {
        try { await Context.Message.DeleteAsync(); }
        catch (Exception ex) when (ex is Discord.Net.HttpException or TimeoutException) { }

        version = (version ?? Versioning.Version).TrimStart('v', 'V');

        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(15);

        System.Xml.Linq.XElement item;
        try
        {
            var feed = System.Xml.Linq.XDocument.Parse(await http.GetStringAsync(ReleaseFeed));
            item = feed.Descendants("item").FirstOrDefault(entry =>
                ((string)entry.Element("link") ?? "").TrimEnd('/').EndsWith($"/v{version}", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            await FailAsync("рсс", $"не смог прочитать ленту сайта: {ex.Message}");
            return;
        }

        if (item is null)
        {
            await FailAsync("рсс", $"поста v{version} на сайте нет, проверь что он опубликован");
            return;
        }

        var title = ((string)item.Element("title") ?? "").Split(": ", 2) is [_, var rest] ? rest : (string)item.Element("title");
        var points = ((string)item.Element("description") ?? "")
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.StartsWith('•') ? line : $"• {line}");

        await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
        {
            Description =
                $"{Format.Bold($"devlog ver {version}")} — {title}\n" +
                string.Join("\n", points) +
                $"\n• [Полный чендж-лог доступен на сайте 🌀]({(string)item.Element("link")})",
            Footer = EmbedHandler.DevFooter,
            FooterIconUrl = EmbedHandler.DevIcon
        }));
    }

    [RequireGuild]
    [Command("ава")]
    public async Task ShowAvatar([Optional] string size, [Optional] IGuildUser user)
    {
        var resolved = size switch
        {
            null or "" => (ushort)128,
            "1" => (ushort)64,
            "2" => (ushort)256,
            "3" => (ushort)512,
            _ => (ushort)0
        };

        if (resolved == 0)
        {
            await FailAsync("аватарки", "такого размера нет, используй значение от 1 до 3😤");
            return;
        }

        user ??= (IGuildUser)Context.User;

        await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
        {
            AuthorName = "sbln аватарки👨‍🦲",
            ImageUrl = GuildAvatar(user),
            Color = Color.Red
        }));
    }

    [RequireSuperuser]
    [Command("инфа разрабов")]
    [Alias("ир")]
    public async Task InfoDev()
    {
        using var process = Process.GetCurrentProcess();
        var collector = GCSettings.IsServerGC ? "server" : "workstation";

        var watch = Stopwatch.StartNew();
        var alive = await DataBase.CanConnect();
        watch.Stop();

        var (corpus, _) = await _gvr.StampAsync();
        var pictures = await _gvr.ImageCountAsync();

        var database = alive ? $"жива, {watch.ElapsedMilliseconds}ms" : "недоступна";
        var twitch = Global.Vars.Cfg.streamsEnabled ? "вкл" : "выкл";
        var ppm = Global.Vars.Cfg.ppmEnabled ? "вкл" : "выкл";
        var remna = Global.Vars.Cfg.remnaEnabled ? "вкл" : "выкл";
        var telegram = string.IsNullOrWhiteSpace(Global.Vars.Cfg.telegramToken) ? "выкл" : "вкл";
        var members = _client.Guilds.Sum(guild => guild.MemberCount);
        var players = _audio.GetActiveGuildIds().Count();
        var next = _commandHandler.NextCollection;

        await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
        {
            Title = $"🤖 sblngavna {Versioning.Full}",
            Description =
                $"Аптайм: **{FormatAge(Uptime)}**, запущен {Stamp(Started)}\n" +
                $".NET **{Environment.Version}**, GC **{collector}**, PID **{Environment.ProcessId}**",
            Color = Color.Blue,
            ThumbnailUrl = Avatar(_client.CurrentUser),
            Footer = "sbln статистикс🔭",
            Fields =
            [
                new EmbedFieldSpec("🌐 Гейтвей",
                    $"Пинг: **{_client.Latency}ms**\n" +
                    $"Состояние: **{_client.ConnectionState}**\n" +
                    $"Серверов: **{_client.Guilds.Count}**\n" +
                    $"Людей: **{members}**", true),
                new EmbedFieldSpec("🧠 Процесс",
                    $"Память: **{process.WorkingSet64 / 1024 / 1024}mb**\n" +
                    $"Куча GC: **{GC.GetTotalMemory(false) / 1024 / 1024}mb**\n" +
                    $"Сборки: **{GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}**\n" +
                    $"Ядер: **{Environment.ProcessorCount}**", true),
                new EmbedFieldSpec("⚙️ Пул потоков",
                    $"Потоков: **{ThreadPool.ThreadCount}**\n" +
                    $"В очереди: **{ThreadPool.PendingWorkItemCount}**\n" +
                    $"Выполнено: **{ThreadPool.CompletedWorkItemCount}**", true),
                new EmbedFieldSpec("🗄️ Данные",
                    $"База: **{database}**\n" +
                    $"Корпус GVR: **{corpus}**, картинок **{pictures}**\n" +
                    $"Подзагрузка GVR: {(next is { } at ? Stamp(at) : "**выкл**")}", true),
                new EmbedFieldSpec("🎧 Звук",
                    $"Плееров: **{players}**\n" +
                    $"Нода: **{Global.Vars.Cfg.lavaHost}:{Global.Vars.Cfg.lavaPort}**\n" +
                    $"Движок: **{EmbedHandler.AudioEngine}**", true),
                new EmbedFieldSpec("🧩 Модули",
                    $"Twitch: **{twitch}**\n" +
                    $"PPM: **{ppm}**\n" +
                    $"Remnawave: **{remna}**\n" +
                    $"Telegram: **{telegram}**", true)
            ]
        }));
    }

    [RequireGuild]
    [Command("инфа")]
    public async Task Info()
    {
        var guild = Context.Guild;

        var embed = EmbedHandler.Build(new EmbedSpec
        {
            Title = $"ℹ️ {guild.Name}",
            Description = string.IsNullOrWhiteSpace(guild.Description) ? "-" : guild.Description,
            Color = Color.Blue,
            ThumbnailUrl = guild.IconUrl,
            Footer = "sbln инфа🔭",
            Fields =
            [
                new EmbedFieldSpec("📅 Основное",
                    $"Владелец: {guild.Owner.Mention}\n" +
                    $"Создан: {Stamp(guild.CreatedAt, 'D')}\n" +
                    $"Участников: **{guild.MemberCount}**", true),
                new EmbedFieldSpec("💬 Структура",
                    $"Текстовых: **{guild.TextChannels.Count}**\n" +
                    $"Голосовых: **{guild.VoiceChannels.Count}**\n" +
                    $"Ролей: **{guild.Roles.Count}**\n" +
                    $"Эмодзи: **{guild.Emotes.Count}**", true),
                new EmbedFieldSpec("🛡️ Прочее",
                    $"Бустов: **{guild.PremiumSubscriptionCount}**\n" +
                    $"2FA: {guild.MfaLevel}\n" +
                    $"NSFW: {guild.NsfwLevel}\n" +
                    $"AFK: {guild.AFKTimeout}с", true)
            ]
        });

        var components = new ComponentBuilder()
            .WithButton("⚙️ Настройки сервера", "setopen", ButtonStyle.Secondary)
            .Build();

        await Context.Channel.SendMessageAsync(embed: embed, components: components);
    }

    [RequireSuperuser]
    [Command("анонс")]
    [Cooldown(10)]
    public async Task AnnounceMessage([Remainder] string message)
    {
        var channel = Context.Guild.PublicUpdatesChannel as ISocketMessageChannel ?? Context.Channel;

        await channel.SendMessageAsync(embed: EmbedHandler.Build(new EmbedSpec
        {
            Title = "анонс сына гавна",
            Description = message,
            ThumbnailUrl = Avatar(Context.User),
            Footer = $"@{Context.User.Username}"
        }));
    }

    [RequireGuild]
    [Command("удоли")]
    [RequireUserPermission(GuildPermission.ManageMessages, ErrorMessage = "нужны права на управление сообщениями")]
    [RequireBotPermission(GuildPermission.ManageMessages)]
    public async Task Clean(int max)
    {
        if (max is < 1 or > 100)
        {
            await FailAsync("чистка", "от 1 до 100 сообщений за раз");
            return;
        }

        var notice = await ReplyAsync("легчайшее");
        var removed = await PurgeAsync(Context.Channel, max + 2, notice.Id);

        await notice.ModifyAsync(properties => properties.Content = $"снесено {removed} сообщений");
    }

    [Command("зал славы")]
    public async Task HallOfGlory()
    {
        var versions = await DataBase.GetAllVersions();
        var last = versions.LastOrDefault();

        var fields = versions
            .Select(version => (Entry: version, Line: version.Stage?.Contains('X', StringComparison.OrdinalIgnoreCase) == true ? version.Stage.Trim() : null))
            .GroupBy(item => item.Line ?? item.Entry.Version.Split('.')[0])
            .Select(line =>
            {
                var from = line.Min(item => item.Entry.Date.Year);
                var to = line.Max(item => item.Entry.Date.Year);

                return new EmbedFieldSpec(
                    $"v{line.Key} ({(from == to ? $"{from}" : $"{from}-{to}")})",
                    string.Join("\n", line.Select(item =>
                        $"`{item.Entry.Version}` {item.Entry.Date:dd.MM.yy}{(item.Line is not null || string.IsNullOrWhiteSpace(item.Entry.Stage) ? "" : $" {item.Entry.Stage}")}")),
                    true);
            })
            .ToList();

        await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
        {
            Title = "🏆 все версии сыночка",
            Description = last is null
                ? "пока пусто"
                : $"Выпущено версий: **{versions.Count}**\nПоследняя **v{last.Version}** ({Stamp(last.Date, 'R')})",
            Fields = fields,
            ThumbnailUrl = "https://sun9-15.userapi.com/impg/c857332/v857332436/15bc51/aBM7tGgnmY0.jpg?size=640x640&quality=96&sign=4ea7c6c8b39104be3a21ae7033cc0283&type=album",
            Footer = EmbedHandler.DevFooter,
            FooterIconUrl = EmbedHandler.DevIcon
        }));
    }

    [Command("эхо")]
    [Cooldown(5)]
    public Task EchoAsync([Remainder] string text) => ReplyAsync('​' + text, true);

    [Frontier]
    [Command("версия")]
    public async Task BotVersionInfo()
    {
        var external = await DataBase.GetAllPackageVersions();

        var fields = new List<EmbedFieldSpec>
        {
            new("🧩 Внутренние модули", CodeTable(
                Versioning.Modules.Select(module => (module.Name, module.Version)),
                "`нет данных`")),
            new("📦 Пакеты", CodeTable(
                Versioning.Packages.Select(package => (package.Name, package.Version)),
                "`нет данных`"))
        };

        if (external.Count > 0)
        {
            fields.Add(new EmbedFieldSpec("🛠️ Внешние сервисы", CodeTable(
                external.Select(item => (item.PackageName, item.PackageVersion)),
                "`нет данных`")));
        }

        await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
        {
            Title = $"🤖 sblngavna {Versioning.Full}",
            Color = Color.LighterGrey,
            Fields = fields,
            Footer = EmbedHandler.DevFooter,
            FooterIconUrl = EmbedHandler.DevIcon
        }));
    }

    [RequireGuild]
    [Command("позови")]
    [Cooldown(10)]
    public Task CallUser(SocketGuildUser user) =>
        ReplyAsync(string.Join(" ", Enumerable.Repeat($"{user.Mention} ЗАЙДИ В ДС", 8)));

    [Frontier]
    [Command("пинг")]
    public async Task Ping()
    {
        var restWatch = Stopwatch.StartNew();
        var message = await ReplyAsync("🏓 понг...");
        restWatch.Stop();

        var dbWatch = Stopwatch.StartNew();
        var dbAlive = await DataBase.CanConnect();
        dbWatch.Stop();

        var database = dbAlive ? $"{dbWatch.ElapsedMilliseconds}ms" : "лежит";

        await message.ModifyAsync(properties => properties.Content =
            $"🏓 понг\n" +
            $"гейтвей `{_client.Latency}ms`, ответ `{restWatch.ElapsedMilliseconds}ms`, база `{database}`\n" +
            $"состояние `{_client.ConnectionState}`, аптайм `{FormatAge(Uptime)}`");
    }

    [RequireGuild]
    [Command("чел")]
    public async Task UserInfo(SocketGuildUser user = null)
    {
        user ??= (SocketGuildUser)Context.User;

        IUser profile = null;
        try { profile = await _client.Rest.GetUserAsync(user.Id); }
        catch (Exception ex) when (ex is Discord.Net.HttpException or TimeoutException) { }

        var roles = user.Roles.Where(role => !role.IsEveryone).OrderByDescending(role => role.Position).ToList();
        var colored = roles.FirstOrDefault(role => role.Colors.PrimaryColor != Color.Default);

        var identity = new List<string>
        {
            $"Ник: **{user.Username}**",
            $"Имя: **{user.GlobalName ?? user.Username}**"
        };

        if (!string.IsNullOrEmpty(user.Nickname))
            identity.Add($"На сервере: **{user.Nickname}**");

        identity.Add($"ID: `{user.Id}`");

        if (user.Id == Context.Guild.OwnerId)
            identity.Add("👑 владелец сервера");
        else if (user.GuildPermissions.Administrator)
            identity.Add("🛡️ администратор");

        if (user.IsBot)
            identity.Add("🤖 бот");

        var devices = user.ActiveClients.Select(client => client switch
        {
            ClientType.Desktop => "🖥️ пк",
            ClientType.Mobile => "📱 телефон",
            ClientType.Web => "🌐 браузер",
            _ => null
        }).Where(device => device is not null).ToList();

        if (devices.Count > 0)
            identity.Add($"Сидит с: {string.Join(", ", devices)}");

        var fields = new List<EmbedFieldSpec>
        {
            new("🪪 Профиль", string.Join("\n", identity), true)
        };

        var dates = $"Аккаунт: {DateAndAgo(user.CreatedAt)}";

        if (user.JoinedAt is { } joined)
        {
            dates += $"\nНа сервере: {DateAndAgo(joined)}";

            if (Context.Guild.HasAllMembers)
            {
                var position = Context.Guild.Users.Count(member => member.JoinedAt < joined) + 1;
                dates += $"\nЗашёл **#{position}** из {Context.Guild.MemberCount}";
            }
        }

        fields.Add(new EmbedFieldSpec("📅 Даты", dates));

        if (user.PremiumSince is { } boosting)
            fields.Add(new EmbedFieldSpec("💎 Бустит", $"с {Stamp(boosting, 'D')}", true));

        if (user.TimedOutUntil is { } timeout && timeout > DateTimeOffset.UtcNow)
            fields.Add(new EmbedFieldSpec("🔇 В муте", $"до {Stamp(timeout)}", true));

        if (user.VoiceChannel is { } voice)
        {
            var flags = new List<string>();
            if (user.IsSelfMuted || user.IsMuted) flags.Add("🔇");
            if (user.IsSelfDeafened || user.IsDeafened) flags.Add("🎧");
            if (user.IsStreaming) flags.Add("📺");
            if (user.IsVideoing) flags.Add("📷");

            fields.Add(new EmbedFieldSpec("🎙️ В войсе", $"{voice.Mention} {string.Join(" ", flags)}".TrimEnd(), true));
        }

        fields.Add(new EmbedFieldSpec(
            $"🎭 Роли ({roles.Count})",
            roles.Count > 0 ? string.Join(" ", roles.Select(role => role.Mention)) : "нет"));

        await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
        {
            Title = user.DisplayName,
            ThumbnailUrl = GuildAvatar(user),
            ImageUrl = (profile as Discord.Rest.RestUser)?.GetBannerUrl(size: 1024),
            Color = colored?.Colors.PrimaryColor ?? (profile as Discord.Rest.RestUser)?.AccentColor ?? Color.Default,
            Fields = fields,
            Footer = "sbln статистикс🔭"
        }));
    }

    [RequireGuild]
    [Command("кик")]
    [RequireUserPermission(GuildPermission.KickMembers, ErrorMessage = "тебе нельзя ты додик")]
    [RequireBotPermission(GuildPermission.KickMembers)]
    public async Task KickMember(SocketGuildUser user = null, [Remainder] string reason = null)
    {
        if (!await CanModerateAsync(user, "кикать"))
            return;

        reason ??= "воля администратора";
        await user.KickAsync(reason);

        await ReplyAsync(embed: EmbedHandler.Moderation(
            "sbln кик <:roflanPominki:552795319516135424>",
            $"✅ {user.Mention} был кикнут с сервера **{Context.Guild.Name}** \n❓Причина: ***{reason}***",
            Avatar(user),
            Context.User.Username,
            Avatar(Context.User)));
    }

    [RequireGuild]
    [Command("бан")]
    [RequireUserPermission(GuildPermission.BanMembers, ErrorMessage = "тебе нельзя ты додик")]
    [RequireBotPermission(GuildPermission.BanMembers)]
    public async Task BanMember(SocketGuildUser user = null, [Remainder] string reason = null)
    {
        if (!await CanModerateAsync(user, "банить"))
            return;

        reason ??= "воля администратора";
        await user.BanAsync(reason: reason);

        await ReplyAsync(embed: EmbedHandler.Moderation(
            "sbln бан <:roflanPominki:552795319516135424>",
            $"✅ {user.Mention} был забанен на сервере **{Context.Guild.Name}** \n❓Причина: ***{reason}***",
            Avatar(user),
            Context.User.Username,
            Avatar(Context.User)));
    }
    private async Task<bool> CanModerateAsync(SocketGuildUser target, string action)
    {
        if (target is null)
        {
            await FailAsync("модерация", "выбери кентошарика");
            return false;
        }

        if (target.Id == Context.User.Id)
        {
            await FailAsync("модерация", "себя трогать не надо");
            return false;
        }

        if (target.Id == Context.Client.CurrentUser.Id)
        {
            await FailAsync("модерация", "меня трогать тем более не надо");
            return false;
        }

        if (target.IsBot && !await SuperuserGate.IsBotOwnerAsync(Context.Client, Context.User))
        {
            await FailAsync("модерация", $"{target.Mention} это бот, {action} его может только владелец");
            return false;
        }

        if (Context.Guild.CurrentUser is { } bot && target.Hierarchy >= bot.Hierarchy)
        {
            await FailAsync("модерация", $"{target.Mention} выше меня по ролям, {action} его я не могу");
            return false;
        }

        return true;
    }

    [Frontier]
    [Command("кал")]
    public async Task MathAsync([Remainder] string math)
    {
        if (!MathSafeRegex.IsMatch(math))
        {
            await FailAsync("калькулятор", "недопустимые символы в выражении");
            return;
        }

        try
        {
            using var table = new DataTable();
            var result = table.Compute(math, null);

            if (result is double value && !double.IsFinite(value))
                throw new DivideByZeroException();

            await ReplyAsync(embed: EmbedHandler.Authored(
                "sbln калькулятор📚📐",
                $"{math} = {result}",
                Color.DarkerGrey,
                null));
        }
        catch (DivideByZeroException)
        {
            await FailAsync("калькулятор", "на ноль делить нельзя");
        }
        catch (Exception ex) when (ex is EvaluateException or SyntaxErrorException or OverflowException)
        {
            await FailAsync("калькулятор", ex.Message);
        }
    }

    [Command("напомни")]
    [Alias("н")]
    public async Task Remind(int seconds, [Remainder] string remindMsg)
    {
        const int maxSeconds = 24 * 60 * 60;

        if (seconds is < 1 or > maxSeconds)
        {
            await FailAsync("напоминалка", $"от 1 секунды до {maxSeconds} секунд (сутки)");
            return;
        }

        await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
        {
            Title = "sbln напоминалка⏰",
            Description = $"😎 ок, я отправлю тебе вот это --- **{remindMsg}**\n⌚ через **{seconds}** сек.",
            Color = Color.DarkGreen
        }));

        await ReminderService.RemindAsyncSeconds(Context.User, Context.Channel, seconds, remindMsg);
    }

    [Command("ембед")]
    public async Task CmdEmbedMessage([Remainder] string msg = "")
    {
        msg = (msg ?? string.Empty).Trim();

        var color = 0;
        var space = msg.IndexOf(' ');
        var head = space < 0 ? msg : msg[..space];

        if (head.Length == 1 && head[0] is >= '1' and <= '4')
        {
            color = head[0] - '0';
            msg = space < 0 ? string.Empty : msg[(space + 1)..];
        }

        var input = msg.Split('|');
        var title = input.Length > 0 ? input[0].Trim() : string.Empty;
        var description = input.Length > 1 ? input[1].Trim() : string.Empty;

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description))
        {
            await FailAsync("ембед", "нужен текст: `ембед [1-4] заголовок | описание`");
            return;
        }

        await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
        {
            Title = title,
            Description = description,
            Color = color switch
            {
                1 => Color.Red,
                2 => Color.Green,
                3 => Color.Blue,
                4 => Color.Gold,
                _ => Color.Default
            }
        }));
    }

    private async Task FailAsync(string source, string reason) =>
        await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed(source, reason));

}
