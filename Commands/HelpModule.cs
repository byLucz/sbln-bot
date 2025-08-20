using Discord.Commands;
using Discord.WebSocket;
using Discord;
using sblngavnav5X.Commands;
using TwitchLib.Communication.Interfaces;

public class HelpAllModule : ModuleBase<SocketCommandContext>
{
    private static ulong _helpMessageId;
    private static List<Embed> _pages;
    private static int _currentPage = 0;
    private static bool _paginationActive = false;
    private readonly DiscordSocketClient _client;

    public HelpAllModule(DiscordSocketClient client)
    {
        _client = client;
        _client.ReactionAdded += OnReactionAdded;
    }


    [Command("памаги")]
    public async Task HelpAll()
    {
        _pages = HelpCommands.GetAllHelpPages();
        _currentPage = 0;

        var msg = await ReplyAsync(embed: _pages[_currentPage]);

        _helpMessageId = msg.Id;
        _paginationActive = true;

        await msg.AddReactionAsync(new Emoji("◀"));
        await msg.AddReactionAsync(new Emoji("▶"));
    }

    private static readonly Dictionary<ulong, DateTime> _lastClickTime
    = new Dictionary<ulong, DateTime>();

    private async Task OnReactionAdded(
        Cacheable<IUserMessage, ulong> message,
        Cacheable<IMessageChannel, ulong> channel,
        SocketReaction reaction)
    {
        if (reaction.UserId == _client.CurrentUser.Id)
            return;

        var now = DateTime.UtcNow;
        if (_lastClickTime.TryGetValue(reaction.UserId, out var prevTime))
        {
            if ((now - prevTime).TotalSeconds < 1)
            {
                return;
            }
        }
        _lastClickTime[reaction.UserId] = now;

        if (!_paginationActive) return;
        if (reaction.MessageId != _helpMessageId) return;

        var msg = await message.GetOrDownloadAsync();

        if (reaction.Emote.Name == "◀")
        {
            if (_currentPage > 0) _currentPage--;
        }
        else if (reaction.Emote.Name == "▶")
        {
            if (_currentPage < _pages.Count - 1) _currentPage++;
        }
        else
        {
            return;
        }

        var user = msg.Channel.GetUserAsync(reaction.UserId).Result;
        await msg.RemoveReactionAsync(reaction.Emote, user);

        await msg.ModifyAsync(m => m.Embed = _pages[_currentPage]);
    }


}
