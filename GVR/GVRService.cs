using Discord;
using Discord.Commands;
using Discord.WebSocket;
using sblngavnav5X.Core;
using sblngavnav5X.Data;
using System.Runtime.InteropServices;

namespace sblngavnav5X.GVR
{
    public class GVRService : ModuleBase<SocketCommandContext>
    {
        private readonly GovorConfig _govorilka;
        private readonly GuildConfig _guild;
        public string chips;
        public GVRService(GovorConfig govor, GuildConfig guild)
        {
            _govorilka = govor;
            _guild = guild;
        }
        [Command("говорилка"), Alias("говор")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SendHelp()
        {
            var builder = new EmbedBuilder()
            .WithTitle("комманды для лютой нейросетки")
            .WithColor(new Color(Color.DarkPurple))
            .WithAuthor(author =>
            {
                author
            .WithName("sbln говорилка🎤📓")
            .WithIconUrl("https://emojio.ru/images/apple-b/1f9e0.png");
            })
            .WithFooter("powered by GovorNGN (beta 1.5)")
            .AddField("доб+", "добавляет определенное кол-во сообщений, переписывая все что до этого было в бд")
            .AddField("доб", "добавляет определенное кол-во сообщений в бд")
            .AddField("чистись", "очищает все говно из бд")
            .AddField("настройкиговора", "показывает настройки нейросетки")
            .AddField("шаг рандома", "изменение шагов цепей рандома")
            .AddField("числов", "число слов в сообщени на выдаче")
            .AddField("шанс", "шанс что говорилка пропиздиться, роллится каждый раз, когда ты чето писюкаешь")
            .AddField("сообщкол", "количество сообщений для подзагрузки")
            .AddField("вр", "интервал через который произойдет подзагрузка сообщений")
            .AddField("верни", "включает особый режим вербальной нищеты **(идея шефа)**")
            .AddField("сброс", "сброс настроек на дефолт");
            var embed = builder.Build();
            await Context.Channel.SendMessageAsync(null, embed: embed).ConfigureAwait(false);
        }
        [Command("настройкиговора")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task GetSettings()
        {
            var cfg = _guild;
            var user = Context.User as SocketGuildUser;
            if (_govorilka.Rand == true)
            {
                chips = "рандом";        
            }
            else
            {
                chips = cfg.govorilka.Count.ToString();
            }
            var embed = new EmbedBuilder();
            embed.WithAuthor("sbln говорилка/настройки🎤📓", "https://emojio.ru/images/apple-b/1f9e0.png");
            embed.WithFooter("powered by GovorNGN (beta 1.5)");
            embed.AddField("шаг рандома", $"**{cfg.govorilka.Step}**", true);
            embed.AddField("число слов", $"**{chips}**", true);
            embed.AddField("шанс ролла", $"**{cfg.govorilka.Chance}%**", true);
            embed.AddField("кол-во сообщений подзагрузки", $"**{cfg.govorilka.Collection}**", true);
            embed.AddField("время подзагрузки", $"**{Utils.govorUpdTime/1000} сек**", true);
            embed.AddField("режим вербальной нищеты", $"**{Utils.verbalMode}**", true);

            await ReplyAsync("", false, embed.Build());
        }
        [Command("добавить"), Alias("доб")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task AppendData(uint amount)
        {
            var messages = this.Context.Channel.GetMessagesAsync((int)amount).Flatten();
            using (StreamWriter sw = new StreamWriter("messages.csv", append: true))
            {
                await foreach (IMessage message in messages)
                {
                    if (!message.Content.StartsWith("x "))
                    {
                            sw.WriteLine(message.Content.ToString());
                    }
                }
            }
            await RemoveDuplicates();
            var m = new EmbedBuilder()
            {
                Footer = new EmbedFooterBuilder()
                {
                    Text = "powered by GovorNGN (beta 1.5)"
                },
                Author = new EmbedAuthorBuilder()
                {
                    Name = "sbln говорилка🎤📓",
                },
                Color = Color.LighterGrey
            };
            m.AddField($"добавлено", $"***{amount} сообщений***", true);
            await ReplyAsync(embed: m.Build());
        }

        [Command("время"), Alias("вр")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task TimeMS(int amount)
        {
            CommandHandler.t.Interval = amount;
            Utils.govorUpdTime = amount;

            var m = new EmbedBuilder()
            {
                Footer = new EmbedFooterBuilder()
                {
                    Text = "powered by GovorNGN (beta 1.5)"
                },
                Author = new EmbedAuthorBuilder()
                {
                    Name = "sbln говорилка🎤📓",
                },
                Color = Color.LighterGrey
            };
            m.AddField($"время подзагрузки обновлено на", $"***{amount/1000} cекунд***", true);
            await ReplyAsync(embed: m.Build());
        }

        [Command("чистись"), Alias("чист")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task ClearFile()
        {
            File.Create("messages.csv").Close();
            var m = new EmbedBuilder()
            {
                Footer = new EmbedFooterBuilder()
                {
                    Text = "powered by GovorNGN (beta 1.5)"
                },
                Author = new EmbedAuthorBuilder()
                {
                    Name = "sbln говорилка🎤📓",
                },
                Color = Color.LighterGrey
            };
            m.AddField($"из бд очищено", $"***{File.Create("messages.csv").Length} говна***", true);
            await ReplyAsync(embed: m.Build());
        }
        [Command("добавить+"), Alias("доб+")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SeedFile(uint amount)
        {
            var messages = this.Context.Channel.GetMessagesAsync((int)amount).Flatten();
            using (StreamWriter sw = new StreamWriter("messages.csv"))
            {
                await foreach (IMessage message in messages)
                {
                    if (!message.Content.Contains("x ") && !message.Content.Contains("https://"))
                    sw.WriteLine(message.Content.ToString());
                }
            }
            await RemoveDuplicates();
            var m = new EmbedBuilder()
            {
                Footer = new EmbedFooterBuilder()
                {
                    Text = "powered by GovorNGN (beta 1.5)"
                },
                Author = new EmbedAuthorBuilder()
                {
                    Name = "sbln говорилка🎤📓",
                },
                Color = Color.LighterGrey
            };
            m.AddField($"ебнуто старое говно и добавлено", $"***{amount} сообщений***", true);
            await ReplyAsync(embed: m.Build());
        }
        [Command("шаг")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetStep(uint step)
        {
            if (step < 1 || step > 15)
            {
                await ReplyAsync($"в диапазоне от 1 до 15 чел");
            }
            else
            {
                _govorilka.Step = step;
                var m = new EmbedBuilder()
                {
                    Footer = new EmbedFooterBuilder()
                    {
                        Text = "powered by GovorNGN (beta 1.5)"
                    },
                    Author = new EmbedAuthorBuilder()
                    {
                        Name = "sbln говорилка🎤📓",
                    },
                    Color = Color.LighterGrey
                };
                m.AddField($"шаг установлен на", $"***{step}***", true);
                await ReplyAsync(embed: m.Build());
            }
        }

        [Command("числослов"), Alias("числов")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetCount(int count, [Optional] string hui)
        {
            if (hui != null)
            {
                _govorilka.Rand = true;
                var m = new EmbedBuilder()
                {
                    Footer = new EmbedFooterBuilder()
                    {
                        Text = "powered by GovorNGN (beta 1.5)"
                    },
                    Author = new EmbedAuthorBuilder()
                    {
                        Name = "sbln говорилка🎤📓",
                    },
                    Color = Color.LighterGrey
                };
                m.AddField($"установлено рандомное значение слов", true);
                await ReplyAsync(embed: m.Build());
                return;
            }
            else
            if (count < 3 || count > 50)
            {
                await ReplyAsync($"в диапазоне от 3 до 50 чел");
            }
            else
            {
                _govorilka.Rand = false;
                _govorilka.Count = count;
                var m = new EmbedBuilder()
                {
                    Footer = new EmbedFooterBuilder()
                    {
                        Text = "powered by GovorNGN (beta 1.5)"
                    },
                    Author = new EmbedAuthorBuilder()
                    {
                        Name = "sbln говорилка🎤📓",
                    },
                    Color = Color.LighterGrey
                };
                m.AddField($"число слов установлено на", $"***{count}***", true);
                await ReplyAsync(embed: m.Build());
            }
        }

        [Command("шанс")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetChance(uint chance)
        {
            if (chance > 100)
            {
                chance = 100;
            }
            _govorilka.Chance = chance;
            var m = new EmbedBuilder()
            {
                Footer = new EmbedFooterBuilder()
                {
                    Text = "powered by GovorNGN (beta 1.5)"
                },
                Author = new EmbedAuthorBuilder()
                {
                    Name = "sbln говорилка🎤📓",
                },
                Color = Color.LighterGrey
            };
            m.AddField($"шанс выдачи установлен на", $"***{chance}% ***", true);
            await ReplyAsync(embed: m.Build());

        }

        [Command("вербальная нищета"), Alias("верни")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task VerbalAbuse(string perekl)
        {
            try
            {
                var clr = perekl switch
                {
                    "вкл" => _govorilka.VerbalAbuseBySheff = true,
                    "выкл" => _govorilka.VerbalAbuseBySheff = false
                };
                var m = new EmbedBuilder()
                {
                    Footer = new EmbedFooterBuilder()
                    {
                        Text = "powered by GovorNGN (beta 1.5)"
                    },
                    Author = new EmbedAuthorBuilder()
                    {
                        Name = "sbln говорилка🎤📓",
                    },
                    Color = Color.LighterGrey
                };
                m.AddField($"режим вербальной нищеты переведен в положение", $"***{perekl}***", true);
                await ReplyAsync(embed: m.Build());
                Utils.verbalMode = perekl;
            }
            catch
            {
                await ReplyAsync($"только вкл/выкл чел");
            }

        }
        [Command("сообщкол")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetCollection(uint amount)
        {
            if (amount > 300)
            {
                await ReplyAsync($"не больше 300 чел");
            }
            else
            {
                _govorilka.Collection = amount;
                var m = new EmbedBuilder()
                {
                    Footer = new EmbedFooterBuilder()
                    {
                        Text = "powered by GovorNGN (beta 1.5)"
                    },
                    Author = new EmbedAuthorBuilder()
                    {
                        Name = "sbln говорилка🎤📓",
                    },
                    Color = Color.LighterGrey
                };
                m.AddField($"количество слов подзагрузки", $"***{amount}***", true);
                await ReplyAsync(embed: m.Build());
            }
        }

        [Command("сброс")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task Reset()
        {
            _guild.govorilka = new GovorConfig();
            var m = new EmbedBuilder()
            {
                Footer = new EmbedFooterBuilder()
                {
                    Text = "powered by GovorNGN (beta 1.5)"
                },
                Author = new EmbedAuthorBuilder()
                {
                    Name = "sbln говорилка🎤📓",
                },
                Color = Color.LighterGrey
            };
            m.AddField($"сбросил все на дефолтыч", true);
            await ReplyAsync(embed: m.Build());
        }
        public async Task RemoveDuplicates()
        {
            string[] lines = File.ReadAllLines("messages.csv");
            await File.WriteAllLinesAsync("messages.csv", lines.Distinct().ToArray());
        }
    }
}
