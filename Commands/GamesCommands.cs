using Discord;
using Discord.Commands;
using sblngavnav6.Core;
using sblngavnav6.Common;
using DiscordTelegramFrontier;
using static sblngavnav6.Common.CommonUtils.Chat;
using System.Text;

namespace sblngavnav6.Commands;

public class GamesCommands : ModuleBase<SocketCommandContext>
{
    private const int RaceLength = 50;
    private static readonly string[] ApexWeapons =
    [
        "R-301", "Alternator", "Rampage + Molly", "Flatline", "C.A.R", "Hemlok", "Devotion",
        "R-99", "Volt", "Bocek", "Prowler", "Spitfire", "L-STAR", "G7 Scout", "Triple Take",
        "Sentinel", "Longbow", "EVA-8", "Mastiff", "Nemesis", "Peacekeeper", "Kraber",
        "Wingman", "RE-45", "Mozambique x2", "P2020 x2"
    ];

    private static readonly string[] ApexUpgrades = ["Фулл обвес", "Прицел+маг", "Прицел", "Дефолт"];
    private static readonly string[] ApexShields = ["Синий", "Фиолетовый", "Красный", "Золотой"];
    private static readonly string[] ApexAbilities = ["Можно", "Нельзя"];

    private static readonly Dictionary<int, string> MinefieldTiles = new()
    {
        { -1, "💣" },
        { 0, "<:slyr3head:779368192036306954>" },
        { 1, "1️⃣" },
        { 2, "2️⃣" },
        { 3, "3️⃣" },
        { 4, "4️⃣" },
        { 5, "5️⃣" },
        { 6, "6️⃣" },
        { 7, "7️⃣" },
        { 8, "8️⃣" }
    };

    private static string Track(string[] racers, int[] progresses)
    {
        var track = new StringBuilder();

        for (var i = 0; i < racers.Length; i++)
        {
            track.Append(new string('ㅤ', progresses[i]))
                 .Append(racers[i])
                 .Append(new string('ㅤ', RaceLength - progresses[i]))
                 .AppendLine("||");
        }

        return track.ToString();
    }

    [Frontier]
    [Command("гонка")]
    public async Task Race([Remainder] string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            await ReplyAsync("должно быть минимум два участника🏎🏎");
            return;
        }

        var racers = parts.Take(5).ToArray();
        var progresses = new int[racers.Length];
        var strengths = new int[racers.Length];

        for (var i = 0; i < racers.Length; i++)
            strengths[i] = CommonUtils.RandomNumber(5, 7);

        var message = await ReplyAsync("на старт...");

        foreach (var phase in new[] { "внимание...", "ПОГНАЛИ!" })
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await message.ModifyAsync(properties => properties.Content = phase);
        }

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        var finished = new List<int>();

        while (finished.Count == 0)
        {
            for (var i = 0; i < progresses.Length; i++)
            {
                progresses[i] = Math.Min(RaceLength, progresses[i] + CommonUtils.RandomNumber(1, strengths[i] - 1));

                if (progresses[i] >= RaceLength)
                    finished.Add(i);
            }

            var frame = Track(racers, progresses);
            await message.ModifyAsync(properties => properties.Content = frame);

            if (finished.Count == 0)
                await Task.Delay(TimeSpan.FromSeconds(1));
        }

        var result = finished.Count == 1
            ? $"👑 Победитель {racers[finished[0]]}!"
            : $"🏁 Ничья между {string.Join(" и ", finished.Select(index => racers[index]))}";

        await message.ModifyAsync(properties => properties.Content = result);
    }

    [Frontier]
    [Command("сапер")]
    public async Task Minefield(int size = 9, float ratio = 0.2f)
    {
        if (size is < 2 or > 9)
        {
            await ReplyAsync("размер от 2 до 9, иначе не влезет");
            return;
        }

        if (ratio is < 0.05f or > 0.6f)
        {
            await ReplyAsync("плотность мин от 0.05 до 0.6");
            return;
        }

        var bombs = new bool[size, size];

        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                bombs[x, y] = Random.Shared.NextDouble() < ratio;

        var field = new StringBuilder();

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
                field.Append("||").Append(MinefieldTiles[bombs[x, y] ? -1 : CountNeighbours(bombs, size, x, y)]).Append("||");

            field.AppendLine();
        }

        await ReplyAsync(field.ToString());
    }

    private static int CountNeighbours(bool[,] bombs, int size, int x, int y)
    {
        var count = 0;

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                var nx = x + dx;
                var ny = y + dy;

                if (nx >= 0 && nx < size && ny >= 0 && ny < size && bombs[nx, ny])
                    count++;
            }
        }

        return count;
    }

    [Frontier]
    [Command("сетарех")]
    [Alias("дуэль")]
    public async Task RandomApexSet(IUser opponent = null)
    {
        var fields = new List<EmbedFieldSpec>
        {
            new("Первое оружие", $"{ApexWeapons.RandomList()} ({ApexUpgrades.RandomList()})"),
            new("Второе оружие", $"{ApexWeapons.RandomList()} ({ApexUpgrades.RandomList()})"),
            new("Щит", ApexShields.RandomList()),
            new("Способности", ApexAbilities.RandomList())
        };

        var duel = opponent is not null;

        var message = await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
        {
            Title = duel ? "1X1 APEX DUEL⚔️" : "APEX SET🗡",
            Description = duel ? $"***{Context.User.Username} VS {opponent.Username}***" : null,
            Color = duel ? Color.DarkRed : Color.DarkerGrey,
            Fields = fields,
            Footer = "sbln апекс🔫"
        }));

        await ReactAsync(message, "<:slyrCinema:1347218953604435998>");
    }
}
