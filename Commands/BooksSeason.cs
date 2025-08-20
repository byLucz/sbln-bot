using Discord;
using Discord.Commands;
using Discord.WebSocket;
using sblngavnav5X.Core;
using sblngavnav5X.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace sblngavnav5X.Commands
{
    public class BooksSeason : ModuleBase<SocketCommandContext>
    {
        private static ulong _msgId;
        private static List<Embed> _pages;
        private static int _currentPage = 0;
        private static bool _active = false;
        private static readonly Dictionary<ulong, DateTime> _lastClick = new();

        private readonly DiscordSocketClient _client;
        public BooksSeason(DiscordSocketClient client)
        {
            _client = client;
            _client.ReactionAdded -= OnReactionAdded;
            _client.ReactionAdded += OnReactionAdded;
        }

        [Command("рейтинг")]
        public async Task ShowSeasonRatingAsync(int? season = null)
        {
            _pages = BuildSeasonEmbeds();
            if (_pages.Count == 0)
                _pages = new() { new EmbedBuilder().WithTitle("---").WithColor(Color.DarkGrey).Build() };

            if (season.HasValue && season.Value >= 1)
                _currentPage = Math.Clamp(season.Value - 1, 0, _pages.Count - 1);
            else
                _currentPage = 0;

            var msg = await ReplyAsync(embed: _pages[_currentPage]);
            _msgId = msg.Id;
            _active = true;

            await msg.AddReactionAsync(new Emoji("◀"));
            await msg.AddReactionAsync(new Emoji("▶"));
        }

        private async Task OnReactionAdded(
            Cacheable<IUserMessage, ulong> message,
            Cacheable<IMessageChannel, ulong> channel,
            SocketReaction reaction)
        {
            try
            {
                if (reaction.UserId == _client.CurrentUser.Id) return;
                if (!_active) return;
                if (reaction.MessageId != _msgId) return;

                var now = DateTime.UtcNow;
                if (_lastClick.TryGetValue(reaction.UserId, out var prev) && (now - prev).TotalSeconds < 1)
                    return;
                _lastClick[reaction.UserId] = now;

                var msg = await message.GetOrDownloadAsync();
                if (msg is null) return;

                if (reaction.Emote.Name == "◀")
                {
                    if (_currentPage > 0) _currentPage--;
                }
                else if (reaction.Emote.Name == "▶")
                {
                    if (_currentPage < _pages.Count - 1) _currentPage++;
                }
                else return;

                var user = msg.Channel.GetUserAsync(reaction.UserId).Result;
                await msg.RemoveReactionAsync(reaction.Emote, user);

                await msg.ModifyAsync(m => m.Embed = _pages[_currentPage]);
            }
            catch { await EmbedHandler.CreateErrorEmbed("knizniyklub", "ошибка пагинации"); }

        }

        private static List<Embed> BuildSeasonEmbeds()
        {
            var list = new List<Embed>();

            int max = DataBase.GetMaxSeason();
            int startSeason = Math.Max(1, max);
            var seasons = new List<int>();

            if (max >= 1)
                seasons.AddRange(Enumerable.Range(1, max));
            else
                seasons.Add(1);

            int nextSeason = Math.Max(1, max) + 1;

            foreach (var s in seasons)
                list.Add(BuildSeasonEmbed(s, max));

            list.Add(BuildSeasonEmbed(nextSeason, max));

            int idx = list.FindIndex(e => e.Title?.EndsWith($"сезон {startSeason}") == true);
            if (idx > 0)
            {
                var first = list[idx];
                list.RemoveAt(idx);
                list.Insert(0, first);
            }

            return list;
        }

        private static Embed BuildSeasonEmbed(int season, int maxSeason)
        {
            var eb = new EmbedBuilder()
                .WithTitle($"<:KKLOGO:1352283192014409869> Рейтинг клуба SZN#{season}")
                .WithColor(Color.Gold)
                .WithFooter("knizhniy klub📖");

            var books = DataBase.GetBooksWithRatings(season);
            if (books == null || books.Count == 0)
            {
                if (season > maxSeason && maxSeason >= 0)
                    eb.WithDescription($"📭COMING SOON!");
                return eb.Build();
            }

            foreach (var b in books)
            {
                var emoji = Utils.GetScoreEmoji(b.AvgScore);
                eb.AddField(
                    $"📖 {b.Title} ({b.Authors})",
                    $"👤 {b.SuggestedBy}\n⭐ Средняя оценка: {b.AvgScore:F1} // {emoji} ({b.Votes} голосов)",
                    inline: false
                );
            }

            return eb.Build();
        }
    }
}