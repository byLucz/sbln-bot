using sblngavnav5X.Audio;
using sblngavnav5X.Commands;
using sblngavnav5X.Commands.Twitch;
using sblngavnav5X.Data;
using sblngavnav5X.GVR;

namespace sblngavnav5X.Core
{
    public static class ModuleToggle
    {
        private static readonly Dictionary<string, bool> _modules = new()
        {
            [nameof(AudioSeven)] = Utils.Modules.Music,
            [nameof(GVRService)] = Utils.Modules.Govor,
            [nameof(TwitchCommands)] = Utils.Modules.Streams,
            [nameof(BooksClubCommands)] = Utils.Modules.Books,
            [nameof(WeatherCommand)] = Utils.Modules.Weather,
            [nameof(MealCommands)] = Utils.Modules.Meal,
            [nameof(FunCommands)] = Utils.Modules.Fun,
            [nameof(SheffCommands)] = Utils.Modules.Fun,
            [nameof(GamesCommands)] = Utils.Modules.Games
        };

        public static bool IsEnabled(string moduleName) =>
            !_modules.TryGetValue(moduleName, out var enabled) || enabled;

        public static string Summary() =>
            string.Join(", ", new (string Name, bool On)[]
            {
                ("музыка", Utils.Modules.Music),
                ("говорилка", Utils.Modules.Govor),
                ("твич", Utils.Modules.Streams),
                ("книги", Utils.Modules.Books),
                ("погода", Utils.Modules.Weather),
                ("велком", Utils.Modules.Welcome),
                ("рецепты", Utils.Modules.Meal),
                ("фан", Utils.Modules.Fun),
                ("игры", Utils.Modules.Games)
            }.Select(m => $"{m.Name} {(m.On ? "вкл" : "выкл")}"));
    }
}
