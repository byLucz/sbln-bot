using Discord;
using sblngavnav6.Common;
using static sblngavnav6.Common.CommonUtils.Chat;
using Discord.Commands;
using Discord.Interactions;
using sblngavnav6.Data;
using DiscordTelegramFrontier;

namespace sblngavnav6.Commands
{
    internal static class SheffEmbeds
    {
        public const string Footer = "sbln шефчик🧑‍🍳";

        public static readonly TimeSpan FrameDelay = TimeSpan.FromSeconds(2.5);

        private static readonly string[] Greetings =
        {
            "поздравляю!",
            "соболезную!"
        };

        private static readonly string[] RotatingHeads =
        {
            "<:slyrHead:779359060225949757>",
            "<:slyr2head:779363223467458571>",
            "<:slyrGdetvoyasamoironiya:800698140021358612>"
        };

        public static IReadOnlyList<Embed> Frames() => RotatingHeads
            .Select(head => EmbedHandler.Build(new EmbedSpec
            {
                Description = $"какой ты макс сегодня? 🎲 {head}",
                Color = Color.Orange,
                Footer = Footer
            }))
            .ToArray();

        public static async Task<Embed> FinalAsync() => EmbedHandler.Build(new EmbedSpec
        {
            Description = $"сегодня ты 🎲 {await DataBase.GetRandomEmote()}\n{Greetings.RandomList()}",
            Color = Color.Gold,
            Footer = Footer
        });

        public static Embed Glory() => EmbedHandler.Build(new EmbedSpec
        {
            Title = "THE ONE AND ONLY SHEFFZ COMMAND🧑‍🍳",
            Description = "**пососи пососи пососи пососи**",
            Color = Color.Gold,
            Footer = "внимание, команда сделана шефчиком!!",
            FooterIconUrl = "https://sun9-11.userapi.com/impg/N2d0Y9MQMZDjMXJpaNU9D2lFiN18XqHUAfx1FQ/Zn5gTZTzx38.jpg?size=1600x1200&quality=95&sign=35fc9e6441da758ff4a7b00cd4a75dcb&type=album"
        });
    }

    public class SheffSlashModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("шефчик", "узнай какой ты сегодня шефчик")]
        public async Task SlashSheffMag7()
        {
            await DeferAsync();

            var message = await AnimateAsync(Context.Channel, SheffEmbeds.Frames(), SheffEmbeds.FrameDelay);

            await AnimateAsync(Context.Channel, [await SheffEmbeds.FinalAsync()], SheffEmbeds.FrameDelay, message);
            await FollowupAsync("готово 😎");
        }

        [SlashCommand("пососи", "невероятный блесс разработанный эксклюзивно шефом")]
        public Task SheffCommand() => RespondAsync(embed: SheffEmbeds.Glory());
    }

    public class SheffCommands : ModuleBase<SocketCommandContext>
    {
        [Frontier]
        [Command("пососи")]
        public Task SheffCommand() => ReplyAsync(embed: SheffEmbeds.Glory());

        [FrontierAsImage]
        [Command("маг7", RunMode = Discord.Commands.RunMode.Async)]
        public async Task SheffMag7()
        {
            var message = await AnimateAsync(Context.Channel, SheffEmbeds.Frames(), SheffEmbeds.FrameDelay);

            await AnimateAsync(Context.Channel, [await SheffEmbeds.FinalAsync()], SheffEmbeds.FrameDelay, message);
        }
    }
}
