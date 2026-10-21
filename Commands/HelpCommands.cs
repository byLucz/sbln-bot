using Discord.Commands;
using sblngavnav5X.Services;

public class HelpCommands : ModuleBase<SocketCommandContext>
{
    private readonly ReactionPager _pager;

    public HelpCommands(ReactionPager pager)
    {
        _pager = pager;
    }

    [Command("памаги")]
    public Task HelpAll() => _pager.SendAsync(Context.Channel, HelpEmbedService.GetHelpPages());
}
