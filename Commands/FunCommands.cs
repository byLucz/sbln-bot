using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Threading.Tasks;
using sblngavnav5X.Data;
using sblngavnav5X.Core;

namespace sblngavnav5X.Commands
{
    public class FunCommands : ModuleBase<SocketCommandContext>
    {
        [Command("погладить")]
        public async Task Pat(string input)
        {
            var url = DataBase.GetRandomMeme("pat");
            var em = EmbedHandler.CreateFImgEmbed($"{Context.User.Mention} погладил {input}💕", url);

            await Context.Channel.SendMessageAsync(embed: await em);
        }

        [Command("чмокнуть")]
        public async Task Kiss(string input)
        {
            var url = DataBase.GetRandomMeme("kiss");
            var em = EmbedHandler.CreateFImgEmbed($"{Context.User.Mention} чмокнул {input}💕", url);

            await Context.Channel.SendMessageAsync(embed: await em);
        }

        [Command("обнять")]
        public async Task Hug(string input)
        {
            var url = DataBase.GetRandomMeme("hug");
            var em = EmbedHandler.CreateFImgEmbed($"{Context.User.Mention} обнял {input}💕", url);

            await Context.Channel.SendMessageAsync(embed: await em);
        }

        [Command("ф")]
        public async Task F(string input)
        {
            var url = DataBase.GetRandomMeme("fff");
            var em = EmbedHandler.CreateFImgEmbed($"{Context.User.Mention} дает респект {input} <:sadge:853604643456024576>", url);

            await Context.Channel.SendMessageAsync(embed: await em);
        }

        [Command("кусь")]
        public async Task Kus(string input)
        {
            var url = DataBase.GetRandomMeme("kus");
            var em = EmbedHandler.CreateFImgEmbed($"{Context.User.Mention} куснул {input}💕", url);

            await Context.Channel.SendMessageAsync(embed: await em);
        }

        [Command("бухнуть")]
        public async Task Buhat(string input)
        {
            var url = DataBase.GetRandomMeme("buhat");
            var em = EmbedHandler.CreateFImgEmbed($"{Context.User.Mention} хочет бухнуть с {input} \U0001f974", url);

            await Context.Channel.SendMessageAsync(embed: await em);
        }

        [Command("заткнуть")]
        [Alias("завали ебало")]
        public async Task Zavali(string input)
        {
            var url = DataBase.GetRandomMeme("ebalo");
            var em = EmbedHandler.CreateFImgEmbed($"{Context.User.Mention} затыкает {input} 🤐", url);

            await Context.Channel.SendMessageAsync(embed: await em);
        }
    }
}