using Discord;
using Discord.Commands;
using sblngavnav6.Common;
using sblngavnav6.Core;
using sblngavnav6.Data;
using sblngavnav6.Services.Twitch;
using sblngavnav6.TwitchService;
using static sblngavnav6.Common.CommonUtils.Text;

namespace sblngavnav6.Commands.Twitch
{
    [RequireGuild]
    public sealed class TwitchCommands : ModuleBase<SocketCommandContext>
    {
        private const string Footer = "sbln твич📺 / powered by TwitchLib";
        private const string Author = "sbln стримеры📺";
        private const string Source = "стримеры";

        private static readonly Color Tint = new(191, 0, 255);

        private readonly StreamMonoService _lsms;
        private readonly StreamerFileHelper _streamers;

        public TwitchCommands(StreamMonoService lsms, StreamerFileHelper streamers)
        {
            _lsms = lsms;
            _streamers = streamers;
        }

        [RequireUserPermission(GuildPermission.ManageRoles)]
        [Command("добавить стримера")]
        public async Task AddStreamerAsync(string streamer)
        {
            if (await DisabledAsync())
                return;

            var outcome = await _streamers.TryAddStreamerAsync(streamer);

            if (outcome is StreamerChange.Done)
            {
                await _lsms.UpdateChannelsToMonitor();
                await ReplyAsync(embed: Report($"**{streamer}** добавлен ✅"));
                return;
            }

            await FailAsync(outcome switch
            {
                StreamerChange.AlreadyThere => $"**{streamer}** уже в списке",
                StreamerChange.NotFound => $"на твиче нет такого канала: **{streamer}**",
                _ => "не вышло, подробности в логе"
            });
        }

        [RequireUserPermission(GuildPermission.ManageRoles)]
        [Command("убрать стримера")]
        public async Task RemoveStreamerAsync(string streamer)
        {
            if (await DisabledAsync())
                return;

            var outcome = await _streamers.TryRemoveStreamerAsync(streamer);

            if (outcome is StreamerChange.Done)
            {
                await _lsms.UpdateChannelsToMonitor();
                await ReplyAsync(embed: Report($"**{streamer}** убран ❌"));
                return;
            }

            await FailAsync(outcome switch
            {
                StreamerChange.Missing => $"**{streamer}** в списке нет",
                _ => "не вышло, подробности в логе"
            });
        }

        [Command("стримеры")]
        [Alias("стримерши")]
        public async Task Streamers()
        {
            if (await DisabledAsync())
                return;

            var streamers = _lsms.StreamList;

            if (streamers.Count == 0)
            {
                await ReplyAsync(embed: Report("список пуст, добавь кого-нибудь через `добавить стримера`"));
                return;
            }

            var online = _lsms.StreamIds
                .Where(pair => _lsms.StreamsOnline.ContainsKey(pair.Value))
                .Select(pair => pair.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rows = streamers
                .OrderByDescending(online.Contains)
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => (IReadOnlyList<string>)[name, online.Contains(name) ? "в эфире" : "оффлайн"]);

            await ReplyAsync(embed: EmbedHandler.Authored(
                Author,
                CodeTable(rows, "`нет данных`"),
                Tint,
                $"{online.Count} из {streamers.Count} в эфире / мониторинг: {_lsms.StatusLsm()} / {Footer}"));
        }

        private async Task<bool> DisabledAsync()
        {
            if (Global.Vars.Cfg.streamsEnabled)
                return false;

            await FailAsync("модуль твича выключен, включается в конфиге: `System:StreamsEnabled`");
            return true;
        }

        private async Task FailAsync(string error) =>
            await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed(Source, error));

        private static Embed Report(string text) =>
            EmbedHandler.Authored(Author, text, Tint, Footer);
    }
}
