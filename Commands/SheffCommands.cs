using Discord;
using Discord.Commands;
using Discord.Interactions;
using sblngavnav5X.Data;

namespace sblngavnav5X.Commands
{
    public class SheffSlashModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("шефчик", "узнай какой ты сегодня шефчик")]
        public async Task SlashSheffMag7()
        {
            await DeferAsync();

            IUserMessage message = null;

            foreach (var rot in Utils.rotatingNumbers)
            {
                var embedAnim = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithDescription($"какой ты макс сегодня? :game_die: {rot}")
                    .WithFooter("sbln шефчик🧑‍🍳")
                    .Build();

                if (message == null)
                {
                    message = await Context.Channel.SendMessageAsync(embed: embedAnim);
                }
                else
                {
                    await message.ModifyAsync(x => x.Embed = embedAnim);
                }

                await Task.Delay(TimeSpan.FromSeconds(2.5));
            }

            int greetings = Utils.RandomNumber(0, 2);

            var finalEmbed = new EmbedBuilder()
                .WithColor(Color.Gold)
                .WithDescription($"сегодня ты :game_die: {DataBase.GetRandomEmote()}\n{Utils.greetList[greetings]}")
                .WithFooter("sbln шефчик🧑‍🍳")
                .Build();

            await message.ModifyAsync(msg => msg.Embed = finalEmbed);
            await FollowupAsync("готово 😎");
        }

        [SlashCommand("пососи", "невероятный блесс разработанный эксклюзивно шефом")]
        public async Task BossesGloryCommand()
        {
            var EmbedBuilder = new EmbedBuilder()
                .WithTitle("THE ONE AND ONLY SHEFFZ COMMAND <:slyrRadost:845217466765279263>")
                .WithDescription("**пососи пососи пососи пососи**")
                .WithFooter(footer =>
                {
                    footer
                    .WithIconUrl("https://sun9-11.userapi.com/impg/N2d0Y9MQMZDjMXJpaNU9D2lFiN18XqHUAfx1FQ/Zn5gTZTzx38.jpg?size=1600x1200&quality=95&sign=35fc9e6441da758ff4a7b00cd4a75dcb&type=album")
                    .WithText($"внимание, команда сделана шефчиком!!");
                });
            Embed embed = EmbedBuilder.Build();
            await RespondAsync(embed: embed);
        }
    }
    public class SheffCommands : ModuleBase<SocketCommandContext>
    {
        [Command("пососи")]
        public async Task BossesGloryCommand()
        {
            var EmbedBuilder = new EmbedBuilder()
                .WithTitle("THE ONE AND ONLY SHEFFZ COMMAND <:slyrRadost:845217466765279263>")
                .WithDescription("**пососи пососи пососи пососи**")
                .WithFooter(footer =>
                {
                    footer
                    .WithIconUrl("https://sun9-11.userapi.com/impg/N2d0Y9MQMZDjMXJpaNU9D2lFiN18XqHUAfx1FQ/Zn5gTZTzx38.jpg?size=1600x1200&quality=95&sign=35fc9e6441da758ff4a7b00cd4a75dcb&type=album")
                    .WithText($"внимание, команда сделана шефчиком!!");
                });
            Embed embed = EmbedBuilder.Build();
            await ReplyAsync(embed: embed);
        }

        [Command("маг7")]
        public async Task SheffMag7()
        {
            IUserMessage message = null;

            foreach (var rot in Utils.rotatingNumbers)
            {
                var embedAnim = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithDescription($"какой ты макс сегодня? :game_die: {rot}")
                    .WithFooter("sbln шефчик🧑‍🍳")
                    .Build();

                if (message == null)
                {
                    message = await Context.Channel.SendMessageAsync(embed: embedAnim);
                    await Task.Delay(TimeSpan.FromSeconds(2.5));
                }
                else
                {
                    await message.ModifyAsync(x => x.Embed = embedAnim);
                    await Task.Delay(TimeSpan.FromSeconds(2.5));
                }
            }

            int greetings = Utils.RandomNumber(0, 2);

            var finalEmbed = new EmbedBuilder()
                .WithColor(Color.Gold)
                .WithDescription($"сегодня ты :game_die: {DataBase.GetRandomEmote()}\n{Utils.greetList[greetings]}")
                .WithFooter("sbln шефчик🧑‍🍳")
                .Build();

            await message.ModifyAsync(msg => msg.Embed = finalEmbed);
        }
    }
}
