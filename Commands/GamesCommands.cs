using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Text;

namespace sblngavnav5X.Commands;

public class GamesCommands : ModuleBase<SocketCommandContext>
{
    List<string> apexWeapons = new List<string>
    {
            "R-301",
            "Alternator",
            "Rampage + Molly",
            "Flatline",
            "C.A.R",
            "Hemlok",
            "Devotion",
            "R-99",
            "Volt",
            "Bocek",
            "Prowler",
            "Spitfire",
            "L-STAR",
            "G7 Scout",
            "Triple Take",
            "Sentinel",
            "Longbow",
            "EVA-8",
            "Mastiff",
            "Nemesis",
            "Peacekeeper",
            "Kraber",
            "Wingman",
            "RE-45",
            "Mozambique x2",
            "P2020 x2"
    };

    string GetString(string[] emojis, int[] progresses)
    {
        string response = "";

        for (int i = 0; i < emojis.Length; i++)
        {
            response += new string('ㅤ', progresses[i]) +
                emojis[i] +
                new string('ㅤ', 50 - progresses[i]) +
                "||\n";
        }

        return response;
    }

    IEnumerable<int> GetBitIndices(int number)
    {
        for (int i = 0; i < 32; i++)
        {
            if ((number & (1 << i)) != 0)
            {
                yield return i;
            }
        }
    }
    
    [Command("гонка", RunMode = RunMode.Async)]
    public async Task Race([Remainder] string args)
    {
        string[] parts = args.Split(' ');

        if (parts.Length < 2)
        {
            await ReplyAsync("должно быть минимум два участника🏎🏎");
            return;
        }

        Random random = new Random();

        string[] emojis = new string[Math.Min(5, parts.Length)];
        int[] progresses = new int[emojis.Length];
        int[] strengths = new int[emojis.Length];

        for (int i = 0; i < emojis.Length; i++)
        {
            emojis[i] = parts[i];
            strengths[i] = random.Next(5, 8);
        }

        IUserMessage message = await ReplyAsync("на старт...");

        await Task.Delay(500);

        await message.ModifyAsync(properties =>
        {
            properties.Content = "внимание...";
        });

        await Task.Delay(500);

        await message.ModifyAsync(properties =>
        {
            properties.Content = "ПОГНАЛИ!";
        });

        await Task.Delay(500);

        int winner = 0;
        int winnerCount = 0;

        bool isFirst = true;

        while (winner == 0)
        {
            await message.ModifyAsync(properties =>
            {
                if (!isFirst)
                {
                    for (int i = 0; i < progresses.Length; i++)
                    {
                        progresses[i] += random.Next(1, strengths[i]);

                        if (progresses[i] >= 50)
                        {
                            progresses[i] = 50;
                            winner |= 1 << i;

                            winnerCount++;
                        }
                    }
                }

                isFirst = false;

                properties.Content = GetString(emojis, progresses);
            });

            await Task.Delay(1000);
        }

        await message.ModifyAsync(properties =>
        {
            if (winnerCount == 1)
            {
                properties.Content = "👑 Победитель " + emojis[GetBitIndices(winner).First()] + "!";
            }
            else
            {
                string winners = "";

                foreach (int index in GetBitIndices(winner))
                {
                    if (winners.Length != 0)
                    {
                        winners += " и ";
                    }

                    winners += emojis[index];
                }

                properties.Content = "🏁 Ничья между " + winners;
            }
        });
    }
    Dictionary<int, string> minesweeperValues = new Dictionary<int, string>()
{
    { -1 , ":bomb:" },
    {  0 , "<:slyr3head:779368192036306954>" },
    {  1 , ":one:" },
    {  2 , ":two:" },
    {  3 , ":three:" },
    {  4 , ":four:" },
    {  5 , ":five:" },
    {  6 , ":six:" },
    {  7 , ":seven:" },
    {  8 , ":eight:" },
};

    [Command("сапер")]
    public async Task Title(int size = 9, float ratio = 0.2f)
    {
        if (size > 9)
        {
            await ReplyAsync("больше 9 низя!");
            return;
        }

        int[,] data = new int[size + 2, size + 2];

        Random random = new Random();

        for (int iy = 1; iy <= size; iy++)
        {
            for (int ix = 1; ix <= size; ix++)
            {
                if (random.NextDouble() < ratio)
                {
                    data[ix, iy] = -1;

                    if (data[ix - 1, iy - 1] >= 0)
                    {
                        data[ix - 1, iy - 1]++;
                    }

                    if (data[ix, iy - 1] >= 0)
                    {
                        data[ix, iy - 1]++;
                    }

                    if (data[ix + 1, iy - 1] >= 0)
                    {
                        data[ix + 1, iy - 1]++;
                    }

                    if (data[ix - 1, iy] >= 0)
                    {
                        data[ix - 1, iy]++;
                    }

                    data[ix + 1, iy]++;
                    data[ix - 1, iy + 1]++;
                    data[ix, iy + 1]++;
                    data[ix + 1, iy + 1]++;
                }
            }
        }

        StringBuilder result = new StringBuilder();

        for (int iy = 1; iy <= size; iy++)
        {
            for (int ix = 1; ix <= size; ix++)
            {
                result.Append("||");
                result.Append(minesweeperValues[data[ix, iy]]);
                result.Append("||");
            }

            result.AppendLine();
        }

        await ReplyAsync(result.ToString());
    }

    [Command("сетарех")]
    [Alias("дуэль")]
    public async Task RandomApexSet(IUser opponent = null)
    {
        var rand = new Random();

        var upgrades = new List<string>
        {
            "Фулл обвес",
            "Прицел+маг",
            "Прицел",
            "Дефолт"
        };

        var shieldOptions = new List<string>
        {
            "Синий",
            "Фиолетовый",
            "Красный",
            "Золотой"
        };

        var abilityOptions = new List<string>
        {
            "Можно",
            "Нельзя"
        };

        var primaryWeapon = apexWeapons[rand.Next(apexWeapons.Count)];
        var primaryUpgrade = upgrades[rand.Next(upgrades.Count)];
        var secondaryWeapon = apexWeapons[rand.Next(apexWeapons.Count)];
        var secondaryUpgrade = upgrades[rand.Next(upgrades.Count)];
        var shieldChoice = shieldOptions[rand.Next(shieldOptions.Count)];
        var abilitiesChoice = abilityOptions[rand.Next(abilityOptions.Count)];

        var embedBuilder = new EmbedBuilder();

        if (opponent != null)
        {
            embedBuilder
                .WithTitle($"1X1 APEX DUEL⚔️")
                .WithDescription($"***{Context.User.Username} VS {opponent.Username}***")
                .AddField("Первое оружие", $"{primaryWeapon} ({primaryUpgrade})", false)
                .AddField("Второе оружие", $"{secondaryWeapon} ({secondaryUpgrade})", false)
                .AddField("Щит", shieldChoice, false)
                .AddField("Способности", abilitiesChoice, false)
                .WithColor(Color.DarkRed)
                .WithFooter("sbln апекс🔫");
        }
        else
        {
            embedBuilder
                .WithTitle("APEX SET🗡")
                .AddField("Первое оружие", $"{primaryWeapon} ({primaryUpgrade})", false)
                .AddField("Второе оружие", $"{secondaryWeapon} ({secondaryUpgrade})", false)
                .AddField("Щит", shieldChoice, false)
                .AddField("Способности", abilitiesChoice, false)
                .WithColor(Color.DarkerGrey)
                .WithFooter("sbln апекс🔫");
        }

        var embed = embedBuilder.Build();
        var message = await ReplyAsync(embed: embed);
        var emote = Emote.Parse("<:slyrCinema:1347218953604435998>");
        await message.AddReactionAsync(emote);

    }

}

