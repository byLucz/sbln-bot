using Discord;
using Discord.Commands;
using Discord.WebSocket;
using sblngavnav5X.Core;
using sblngavnav5X.Common;
using sblngavnav5X.Data;
using System.Runtime.InteropServices;

namespace sblngavnav5X.GVR
{
    public class GVRService : ModuleBase<SocketCommandContext>
    {
        private readonly GovorConfig _govorilka;
        private readonly GuildConfig _guild;

        private const string GovorAuthor = "sbln говорилка🎤📓";
        private const string GovorFooter = "powered by GovorNGN";
        private const string GovorIcon = "https://emojio.ru/images/apple-b/1f9e0.png";

        private static EmbedBuilder GovorEmbed() =>
            EmbedHandler.FieldsEmbed(GovorAuthor, Color.LighterGrey, GovorFooter);

        public GVRService(GovorConfig govor, GuildConfig guild)
        {
            _govorilka = govor;
            _guild = guild;
        }

        [Command("говорилка"), Alias("говор")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SendHelp()
        {
            var embed = EmbedHandler.FieldsEmbed(GovorAuthor, new Color(Color.DarkPurple), GovorFooter, authorIconUrl: GovorIcon)
                .WithTitle("комманды для лютой нейросетки")
                .AddField("доб+", "добавляет определенное кол-во сообщений, переписывая все что до этого было в бд")
                .AddField("доб", "добавляет определенное кол-во сообщений в бд")
                .AddField("чистись", "прогоняет базу через все фильтры и чистит дубли")
                .AddField("настройкиговора", "показывает настройки нейросетки")
                .AddField("шаг рандома", "изменение шагов цепей рандома")
                .AddField("числов", "число слов в сообщени на выдаче")
                .AddField("шанс", "шанс что говорилка пропиздиться, роллится каждый раз, когда ты чето писюкаешь")
                .AddField("сообщкол", "количество сообщений для подзагрузки")
                .AddField("вр", "интервал через который произойдет подзагрузка сообщений")
                .AddField("верни", "включает особый режим вербальной нищеты **(идея шефа)**")
                .AddField("сброс", "сброс настроек на дефолт")
                .Build();
            await Context.Channel.SendMessageAsync(null, embed: embed).ConfigureAwait(false);
        }

        [Command("настройкиговора")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task GetSettings()
        {
            var chips = _govorilka.Rand ? "рандом" : _guild.govorilka.Count.ToString();
            var embed = EmbedHandler.FieldsEmbed("sbln говорилка/настройки🎤📓", Color.LighterGrey, GovorFooter, authorIconUrl: GovorIcon)
                .AddField("шаг рандома", $"**{_guild.govorilka.Step}**", true)
                .AddField("число слов", $"**{chips}**", true)
                .AddField("шанс ролла", $"**{_guild.govorilka.Chance}%**", true)
                .AddField("кол-во сообщений подзагрузки", $"**{_guild.govorilka.Collection}**", true)
                .AddField("время подзагрузки", $"**{Global.Vars.BuiltIn.govorUpdTime / 1000} сек**", true)
                .AddField("режим вербальной нищеты", $"**{Global.Vars.BuiltIn.govorVM}**", true)
                .Build();
            await ReplyAsync(embed: embed);
        }

        [Command("добавить"), Alias("доб")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task AppendData(uint amount)
        {
            var messages = this.Context.Channel.GetMessagesAsync((int)amount).Flatten();
            using (StreamWriter sw = new StreamWriter(Global.Vars.Cfg.messagesFilePath, append: true))
            {
                await foreach (IMessage message in messages)
                {
                    if (message.Author.IsBot) continue;
                    var content = message.Content.Trim();
                    if (string.IsNullOrWhiteSpace(content)) continue;
                    if (content.StartsWith(Global.Vars.Cfg.pref1, StringComparison.OrdinalIgnoreCase)) continue;
                    if (content.StartsWith(Global.Vars.Cfg.pref2, StringComparison.OrdinalIgnoreCase)) continue;
                    if (content.Contains("https://", StringComparison.OrdinalIgnoreCase)) continue;
                    sw.WriteLine(content);
                }
            }
            await RemoveDuplicates();
            await ReplyAsync(embed: GovorEmbed()
                .AddField("добавлено", $"***{amount} сообщений***", true)
                .Build());
        }

        [Command("время"), Alias("вр")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task TimeMS(int amount)
        {
            if (amount <= 0)
            {
                await ReplyAsync("Укажи значение больше 0.");
                return;
            }
            CommandHandler.UpdateTimerInterval(amount);
            await ReplyAsync(embed: GovorEmbed()
                .AddField("время подзагрузки обновлено на", $"***{amount / 1000.0:0.##} секунд***", true)
                .Build());
        }

        [Command("чистись"), Alias("чист")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task ClearFile()
        {
            int before = 0, after = 0;

            if (File.Exists(Global.Vars.Cfg.messagesFilePath))
            {
                var lines = await File.ReadAllLinesAsync(Global.Vars.Cfg.messagesFilePath);
                before = lines.Length;

                var cleaned = lines
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Where(l => !l.Contains("https://", StringComparison.OrdinalIgnoreCase))
                    .Where(l => !l.StartsWith(Global.Vars.Cfg.pref1, StringComparison.OrdinalIgnoreCase))
                    .Where(l => !l.StartsWith(Global.Vars.Cfg.pref2, StringComparison.OrdinalIgnoreCase))
                    .Distinct()
                    .ToArray();

                after = cleaned.Length;
                await File.WriteAllLinesAsync(Global.Vars.Cfg.messagesFilePath, cleaned);
            }

            await ReplyAsync(embed: GovorEmbed()
                .AddField("было строк", $"***{before}***", true)
                .AddField("осталось", $"***{after}***", true)
                .AddField("удалено говна", $"***{before - after}***", true)
                .Build());
        }

        [Command("добавить+"), Alias("доб+")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SeedFile(uint amount)
        {
            var messages = this.Context.Channel.GetMessagesAsync((int)amount).Flatten();
            using (StreamWriter sw = new StreamWriter(Global.Vars.Cfg.messagesFilePath))
            {
                await foreach (IMessage message in messages)
                {
                    if (message.Author.IsBot) continue;
                    var content = message.Content.Trim();
                    if (string.IsNullOrWhiteSpace(content)) continue;
                    if (content.StartsWith(Global.Vars.Cfg.pref1, StringComparison.OrdinalIgnoreCase)) continue;
                    if (content.StartsWith(Global.Vars.Cfg.pref2, StringComparison.OrdinalIgnoreCase)) continue;
                    if (content.Contains("https://", StringComparison.OrdinalIgnoreCase)) continue;
                    sw.WriteLine(content);
                }
            }
            await RemoveDuplicates();
            await ReplyAsync(embed: GovorEmbed()
                .AddField("ебнуто старое говно и добавлено", $"***{amount} сообщений***", true)
                .Build());
        }

        [Command("шаг")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetStep(uint step)
        {
            if (step < 1 || step > 15)
            {
                await ReplyAsync("в диапазоне от 1 до 15 чел");
                return;
            }
            _govorilka.Step = step;
            await ReplyAsync(embed: GovorEmbed()
                .AddField("шаг установлен на", $"***{step}***", true)
                .Build());
        }

        [Command("числослов"), Alias("числов")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetCount(int count, [Optional] string hui)
        {
            if (hui != null)
            {
                _govorilka.Rand = true;
                await ReplyAsync(embed: GovorEmbed()
                    .AddField("установлено рандомное значение слов", true)
                    .Build());
                return;
            }
            if (count < 3 || count > 50)
            {
                await ReplyAsync("в диапазоне от 3 до 50 чел");
                return;
            }
            _govorilka.Rand = false;
            _govorilka.Count = count;
            await ReplyAsync(embed: GovorEmbed()
                .AddField("число слов установлено на", $"***{count}***", true)
                .Build());
        }

        [Command("шанс")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetChance(uint chance)
        {
            if (chance > 100) chance = 100;
            _govorilka.Chance = chance;
            await ReplyAsync(embed: GovorEmbed()
                .AddField("шанс выдачи установлен на", $"***{chance}%***", true)
                .Build());
        }

        [Command("вербальная нищета"), Alias("верни")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task VerbalAbuse(string perekl)
        {
            try
            {
                _govorilka.VerbalAbuseBySheff = perekl switch
                {
                    "вкл" => true,
                    "выкл" => false,
                    _ => false
                };
                Global.Vars.BuiltIn.govorVM = perekl;
                await ReplyAsync(embed: GovorEmbed()
                    .AddField("режим вербальной нищеты переведен в положение", $"***{perekl}***", true)
                    .Build());
            }
            catch
            {
                await ReplyAsync("только вкл/выкл чел");
            }
        }

        [Command("сообщкол")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetCollection(uint amount)
        {
            if (amount > 300)
            {
                await ReplyAsync("не больше 300 чел");
                return;
            }
            _govorilka.Collection = amount;
            await ReplyAsync(embed: GovorEmbed()
                .AddField("количество сообщений подзагрузки", $"***{amount}***", true)
                .Build());
        }

        [Command("сброс")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task Reset()
        {
            _guild.govorilka = new GovorConfig();
            await ReplyAsync(embed: GovorEmbed()
                .AddField("сбросил все на дефолтыч", true)
                .Build());
        }

        public async Task RemoveDuplicates()
        {
            var lines = (await File.ReadAllLinesAsync(Global.Vars.Cfg.messagesFilePath))
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .Distinct()
                .ToArray();
            await File.WriteAllLinesAsync(Global.Vars.Cfg.messagesFilePath, lines);
        }
    }
}
