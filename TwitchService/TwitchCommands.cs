using Discord.Commands;
using sblngavnav5X.Services.Twitch;
using sblngavnav5X.TwitchService;

namespace sblngavnav5X.Commands.Twitch
{
    public class TwitchCommands : ModuleBase<SocketCommandContext>
    {
        private readonly StreamMonoService _lsms;
        public TwitchCommands(StreamMonoService lsms)
        {
            _lsms = lsms;
        }

        [RequireUserPermission(Discord.GuildPermission.ManageRoles)]
        [Command("добавить стримера")]
        [Alias("добавить")]
        public async Task AddStreamerAsync(string streamer)
        {

            StreamerFileHelper sfh = new StreamerFileHelper(_lsms);
            int t = await sfh.TryAddStreamerAsync(streamer);
            try
            {
                if (t == 1)
                {
                    await ReplyAsync($"{streamer} - успешно добавлен✅");
                    await _lsms.UpdateChannelsToMonitor();
                }
                else if (t == 0)
                {
                    await ReplyAsync("такой кентик уже есть");
                }
                else if (t == -1)
                {
                    await ReplyAsync(
                        $"Такого кента не существует, это вообще че ебать ---> {streamer}, убери хуйню");
                }
                else
                {
                    await ReplyAsync(
                        "чето отьебнуло и теперь не работает =(");
                }
            }
            catch (Exception e)
            {
                await Console.Out.WriteLineAsync(e.Message);
                await ReplyAsync("стример не загружен =(");
            }
            finally
            {
                await _lsms.UpdateChannelsToMonitor();
            }
        }

        [RequireUserPermission(Discord.GuildPermission.ManageRoles)]
        [Command("убрать стримера")]
        [Alias("убрать")]
        public async Task RemoveStreamerAsync(string streamer)
        {
            StreamerFileHelper sfh = new StreamerFileHelper(_lsms);
            bool t = await sfh.TryRemoveStreamerAsync(streamer);

            try
            {
                if (t)
                {
                    await ReplyAsync($"{streamer} - успешно убран❌");
                    await _lsms.UpdateChannelsToMonitor();
                }
                else
                {
                    await ReplyAsync($"такого кента в списке нет, лол");
                }
            }
            catch (Exception e)
            {
                await Console.Out.WriteLineAsync(e.ToString());
                await ReplyAsync("чето отьебнуло и теперь не работает =(");
            }
        }

        [Command("стримеры")]
        [Alias("стримерши")]

        public async Task Streamers()
        {
            string stream_final = "```\n";

            for (int i = 0; i < _lsms.StreamList.Count; i++)
            {
                stream_final += $"{_lsms.StreamList[i]} ";
            }

            stream_final = stream_final.Insert(stream_final.Length - 1, "\n```");
            await ReplyAsync(stream_final, false);
        }
    }
}
