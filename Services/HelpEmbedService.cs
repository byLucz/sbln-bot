using Discord;
using sblngavnav6.Common;

public static class HelpEmbedService
{
    private const string AuthorIcon = "https://assets.coingecko.com/coins/images/8758/large/ShitCoin.png";
    private const string FooterIcon = "https://cdn.betterttv.net/emote/5eef8ed979645a0dec34cc0a/3x";
    private const string FooterText = "dev by lucz@lois.media🏃";

    private static readonly (string Title, (string Command, string About, bool Telegram)[] Commands)[] Sections =
    [
        ("Zдарова я сын гавна и это мои основные команды",
        [
            ("памаги|помоги|помощь|хелп", "список команд, ты тут", false),
            ("инфа", "инфа о сервере и кнопка ⚙️ с панелью настроек", false),
            ("чел", "инфа об участнике", false),
            ("ава", "получить аватар участника", false),
            ("пинг", "пинг гейтвея, апи и базы", true),
            ("апт", "аптайм бота", true),
            ("версия", "версия бота и пакетов", true),
            ("зал славы", "зал славы лучших мемберов", true),
            ("погода", "погода в горАде", true),
            ("курс|кс", "курс рубля к доллару/евро/тенге", true),
            ("биток|монетки|мон", "стоимость популярной крипты", true),
            ("кал", "калькулятор выражений", true),
            ("ролл", "рандом число в диапазоне", true),
            ("книга", "найти книгу по названию", true),
            ("напомни|н", "напоминалка в ЛС", false),
            ("позови", "позвать любого кентика", false),
            ("эхо", "дублирует сообщение в войс", false),
            ("ембед", "собрать эмбед: цвет, заголовок | описание", false)
        ]),

        ("Музыкальные команды Audio8",
        [
            ("играй|и", "играть песенку", false),
            ("выйди|л", "лив с канала", false),
            ("плейлист|лист", "очередь композиций", false),
            ("скип|ск", "скипнуть трек", false),
            ("останови|стоп", "остановить и очистить плейлист", false),
            ("пауза|пз", "приостановить", false),
            ("продолжи|прод", "продолжить", false),
            ("назад|пред|предыдущий", "вернуть предыдущий трек", false),
            ("перейти|пр", "перейти по таймингу", false),
            ("залупа|луп", "вкл/выкл повтор", false),
            ("перемешай|шафл|перемешка", "перемешать очередь", false),
            ("недавние|нед|последние", "последние плейлисты сервера", false),
            ("громкость|гр", "громкость 1-500, без аргумента — текущая", false),
            ("басс|бс", "басс буст, ступени 1-4 (1 — выкл)", false),
            ("фильтр|фильтры|эффект", "найткор | слоу | 8д | караоке | вибрато | выкл", false),
            ("источник|сорс|деф", "дефолтный источник поиска", false),
            ("озвучь|ттс", "произнести текст в голосовом канале", false),
            ("голосование|голос", "выбор из вариантов с озвучкой", false),
            ("лавастат", "статус музыкального lavalink", false)
        ]),

        ("Команды для фанчика",
        [
            ("выбери", "выбор из нескольких вариантов", true),
            ("паста", "рандомная паста из карбонары", true),
            ("шутка|анек", "рандомная шутка по категории (1-3)", true),
            ("рецепт", "случайный рецепт от шефчика", true),
            ("кит|кот", "показать рандомного котика", true),
            ("лейм", "узнать уровень лейминга", true),
            ("гонка", "гонка смайликов", true),
            ("сапер", "классический сапёр", false),
            ("сетарех|дуэль", "выдача сета/дуэли в арехе", true),
            ("маг7", "какой ты сегодня максим", true),
            ("пососи", "пососи (команда SHEFFZ)", true),
            ("волк", "рандом волк", true),
            ("8 яиц|?", "аналог шара-восьмерки", true),
            ("погладить", "погладить участника", true),
            ("чмокнуть", "чмокнуть участника", true),
            ("обнять", "обнять участника", true),
            ("кусь", "кусануть участника", true),
            ("бухнуть", "позвать бухать", true),
            ("ф", "прожать F в чат", true),
            ("заткнуть|завали ебало", "жестко заткнуть кента", true)
        ]),

        ("Мои вип/мод команды😎",
        [
            ("кик", "выгнать кентошарика", false),
            ("бан", "забанить кентошарика", false),
            ("удоли", "удалить сообщения <кол-во>", false),
            ("анонс", "анонс в канал обновлений сервера", false),
            ("ст", "меняет статус бота", false),
            ("актив", "ставит активность бота", false),
            ("инфа разрабов|ир", "состояние процесса, базы и модулей", false),
            ("пг", "панель pgAPI", false),
            ("111", "девлог текущей версии", false),
            ("клуб", "книжный клуб: правила, оценки и свои команды", false),
            ("говор|говорилка|гвр", "говорилка: справка, настройки и подкоманды", false),
            ("почта @ник", "внутренняя почта, письмо челиксу в лс", false),
            ("печкин|ппм|емейл", "панель почтовых ящиков PPM", false),
            ("добавить стримера", "добавить twitch-стримера в отслеживание", false),
            ("убрать стримера", "убрать twitch-стримера из отслеживания", false),
            ("стримеры|стримерши", "список отслеживаемых стримеров", false)
        ])
    ];

    public static List<Embed> GetHelpPages() => Sections.Select(Page).ToList();

    public static IReadOnlyList<(string Title, IReadOnlyList<(string Command, string About)> Commands)> TelegramSections() => Sections
        .Select(section => (section.Title, (IReadOnlyList<(string, string)>)section.Commands
            .Where(command => command.Telegram)
            .Select(command => (command.Command, command.About))
            .ToArray()))
        .Where(section => section.Item2.Count > 0)
        .ToArray();

    private static Embed Page((string Title, (string Command, string About, bool Telegram)[] Commands) section) =>
        EmbedHandler.Build(new EmbedSpec
        {
            AuthorName = section.Title,
            AuthorIconUrl = AuthorIcon,
            Color = Color.DarkBlue,
            Fields = section.Commands
                .Select(command => new EmbedFieldSpec(command.Telegram ? $"{command.Command} (Tg)" : command.Command, command.About))
                .ToArray(),
            Footer = FooterText,
            FooterIconUrl = FooterIcon
        });
}
