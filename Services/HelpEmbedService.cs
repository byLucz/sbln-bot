using Discord;
using Discord.Commands;
using sblngavnav6.Common;

namespace sblngavnav6.Services;

public sealed record HelpCommand(string Names, string About, bool Telegram = false, string Args = null, string Example = null)
{
    public string[] Aliases { get; } = Names.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public string Label => Args is null ? Names : $"{Names} {Args}";

    public string Usage => Args is null ? Aliases[0] : $"{Aliases[0]} {Args}";
}

public static class HelpEmbedService
{
    private const string AuthorIcon = "https://assets.coingecko.com/coins/images/8758/large/ShitCoin.png";

    private static readonly (string Title, HelpCommand[] Commands)[] Sections =
    [
        ("Zдарова я сын гавна и это мои основные команды",
        [
            new("памаги|помоги|помощь|хелп", "список команд, ты тут"),
            new("инфа", "инфа о сервере и кнопка ⚙️ с панелью настроек"),
            new("чел", "инфа об участнике", Args: "[@юзер]", Example: "х чел @кент"),
            new("ава", "получить аватар участника", Args: "[1-3] [@юзер]", Example: "х ава 3 @кент"),
            new("пинг", "пинг гейтвея, апи и базы", true),
            new("версия", "версия бота и пакетов", true),
            new("зал славы", "зал славы лучших мемберов"),
            new("погода", "погода в горАде", true, "<город>", "х погода Москва"),
            new("курс|кс", "курс рубля к доллару/евро/тенге", true),
            new("биток|крипта", "стоимость популярной крипты", true),
            new("кал", "калькулятор выражений", true, "<выражение>", "х кал 2+2*2"),
            new("ролл", "рандом число в диапазоне", true, "<от> <до>", "х ролл 1 100"),
            new("книга", "найти книгу по названию", true, "<название>", "х книга мастер и маргарита"),
            new("карты|ротация", "ротация карт apex: паблики, ранкед и режим дня", true),
            new("напомни|н", "напоминалка в ЛС", Args: "<секунды> <текст>", Example: "х н 600 вынести мусор"),
            new("позови", "позвать любого кентика", Args: "<@юзер>", Example: "х позови @кент"),
            new("эхо", "дублирует сообщение в войс", Args: "<текст>", Example: "х эхо всем привет"),
            new("ембед", "собрать эмбед с цветом 1-4", Args: "[1-4] <заголовок> | <описание>", Example: "х ембед 2 сбор | завтра в 8")
        ]),

        ("Музыкальные команды Audio8",
        [
            new("играй|и", "играть песенку", Args: "<запрос или ссылка>", Example: "х и платина"),
            new("выйди|л", "лив с канала"),
            new("плейлист|лист", "очередь композиций"),
            new("скип|ск", "скипнуть трек или до номера в очереди", Args: "[номер]", Example: "х ск 3"),
            new("останови|стоп", "остановить и очистить плейлист"),
            new("пауза|пз", "приостановить"),
            new("продолжи|прод", "продолжить"),
            new("назад|пред|предыдущий", "вернуть предыдущий трек"),
            new("перейти|пр", "перейти по таймингу", Args: "<время>", Example: "х пр 1:30"),
            new("залупа|луп", "вкл/выкл повтор"),
            new("перемешай|шафл|перемешка", "перемешать очередь"),
            new("недавние|нед|последние", "последние плейлисты сервера"),
            new("громкость|гр", "громкость, без числа покажет текущую", Args: "[1-500]", Example: "х гр 150"),
            new("басс|бс", "басс буст, 1 выключает", Args: "[1-4]", Example: "х бс 3"),
            new("фильтр|фильтры|эффект", "звуковой фильтр", Args: "[найткор | слоу | 8д | караоке | вибрато | выкл]", Example: "х фильтр найткор"),
            new("источник|сорс|деф", "дефолтный источник поиска", Args: "[ютуб | спотик | склауд]", Example: "х источник спотик"),
            new("озвучь|ттс", "произнести текст в голосовом канале", Args: "<текст>", Example: "х озвучь всем привет"),
            new("голосование|голос", "выбор из вариантов с озвучкой", Args: "<a | b | c>", Example: "х голос платина | кишлак | мэйби бэйби"),
            new("лавастат", "статус музыкального lavalink"),
            new("резерв", "повторить через резервный источник, без запроса берёт последний ненайденный", Args: "[запрос]", Example: "х резерв")
        ]),

        ("Команды для фанчика",
        [
            new("выбери", "выбор из нескольких вариантов", true, "<a | b | c>", "х выбери пицца | шаурма | спать"),
            new("паста", "рандомная паста из карбонары", true),
            new("шутка|анек", "рандомная шутка по категории", true, "[1-3]", "х шутка 2"),
            new("рецепт", "случайный рецепт от шефчика", true),
            new("кит|кот", "показать рандомного котика", true),
            new("лейм", "узнать уровень лейминга", true, "[@юзер]", "х лейм @кент"),
            new("гонка", "гонка смайликов, от 2 до 5 участников", true, "<участники>", "х гонка 🐌 🐢 🦆"),
            new("обоссать", "гифка, где ты обоссываешь кента", Args: "[@юзер]", Example: "х обоссать @кент"),
            new("монетка|монета|коин", "3D монетка с аватарками, решает споры", Args: "[@юзер] [@юзер]", Example: "х монетка @кент"),
            new("демотиватор|демот", "демотиватор: заголовок | подпись с картинкой, без текста рандом из GVR", true, "[заголовок | подпись]", "х демот когда | понял"),
            new("шефограм|шфгр", "превращает все лица на картинке в шефа (или в @юзера)", true, "[@юзер]", "х шфгр @кент"),
            new("скан|сканер", "распознать текст на картинке", true, "[рус | енг]", "х скан енг"),
            new("сапер", "классический сапёр", Args: "[размер] [доля мин]", Example: "х сапер 9"),
            new("сетарех|дуэль", "выдача сета/дуэли в арехе", true, "[@юзер]", "х дуэль @кент"),
            new("маг7", "какой ты сегодня максим", true),
            new("пососи", "пососи (команда SHEFFZ)", true),
            new("волк", "рандом волк", true),
            new("8 яиц|?", "аналог шара-восьмерки", true, "[вопрос]", "х ? сегодня повезёт"),
            new("погладить", "погладить участника", true, "<@юзер>", "х погладить @кент"),
            new("чмокнуть", "чмокнуть участника", true, "<@юзер>", "х чмокнуть @кент"),
            new("обнять", "обнять участника", true, "<@юзер>", "х обнять @кент"),
            new("кусь", "кусануть участника", true, "<@юзер>", "х кусь @кент"),
            new("бухнуть", "позвать бухать", true, "<@юзер>", "х бухнуть @кент"),
            new("ф", "прожать F в чат", true, "<кому>", "х ф за деда"),
            new("заткнуть|завали ебало", "жестко заткнуть кента", true, "<@юзер>", "х заткнуть @кент")
        ]),

        ("Мои вип/мод команды😎",
        [
            new("кик", "выгнать кентошарика", Args: "<@юзер> [причина]", Example: "х кик @кент флуд"),
            new("бан", "забанить кентошарика", Args: "<@юзер> [причина]", Example: "х бан @кент спам"),
            new("удоли", "удалить последние сообщения", Args: "<количество>", Example: "х удоли 20"),
            new("анонс", "анонс в канал обновлений сервера", Args: "<текст>", Example: "х анонс завтра сходка"),
            new("ст|статус", "панель статуса и активности бота"),
            new("инфа разрабов|ир", "состояние процесса, базы и модулей"),
            new("почта", "внутренняя почта, письмо челиксу в лс", Args: "<@юзер> <текст>", Example: "х почта @кент привет"),
            new("стримеры|стримерши", "список отслеживаемых стримеров")
        ])
    ];

    private static readonly (string Title, HelpCommand[] Commands) DevSection =
        ("Команды экосистемы Lois Media",
        [
            new("пг", "панель pgAPI"),
            new("рв|ремна", "панель Remnawave, ноды", true),
            new("рв диаг", "диагностика Remnawave: API, бэкенд, упавшие ноды, падения за 24ч", true),
            new("рв тех", "закрыть ноду на тех. работы, снять кнопкой в карточке", true, "<нода> [текст]", "х рв тех de1 обновление"),
            new("рв рагент", "R-WATCHER agent на нодах: диск, скан SNI, очистка диска", true),
            new("печкин|ппм|емейл", "панель почтовых ящиков PPM"),
            new("говор|говорилка|гвр", "говорилка: справка, настройки и подкоманды"),
            new("клуб", "книжный клуб: правила, оценки и свои команды"),
            new("добавить стримера", "добавить twitch-стримера в отслеживание", Args: "<ник>", Example: "х добавить стримера lucz"),
            new("убрать стримера", "убрать twitch-стримера из отслеживания", Args: "<ник>", Example: "х убрать стримера lucz")
        ]);

    public static IReadOnlyList<(string Title, HelpCommand[] Commands)> PublicSections => Sections;

    public static HelpCommand Find(CommandInfo command) =>
        Sections.Append(DevSection)
            .SelectMany(section => section.Commands)
            .FirstOrDefault(help => command.Aliases.Prepend(command.Name)
                .Any(alias => help.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase)));

    public static List<Embed> GetHelpPages(bool devGuild = false) =>
        (devGuild ? Sections.Append(DevSection) : Sections).Select(Page).ToList();

    public static IReadOnlyList<(string Title, IReadOnlyList<(string Command, string About)> Commands)> TelegramSections(bool dev = false)
        => (dev ? Sections.Append(DevSection) : Sections)
        .Select(section => (section.Title, (IReadOnlyList<(string, string)>)section.Commands
            .Where(command => command.Telegram)
            .Select(command => (command.Label, command.About))
            .ToArray()))
        .Where(section => section.Item2.Count > 0)
        .ToArray();

    private static Embed Page((string Title, HelpCommand[] Commands) section) =>
        EmbedHandler.Build(new EmbedSpec
        {
            AuthorName = section.Title,
            AuthorIconUrl = AuthorIcon,
            Color = Color.DarkBlue,
            Fields = section.Commands
                .Select(command => new EmbedFieldSpec(command.Telegram ? $"{command.Label} (Tg)" : command.Label, command.About))
                .ToArray(),
            Footer = EmbedHandler.DevFooter,
            FooterIconUrl = EmbedHandler.DevIcon
        });
}
