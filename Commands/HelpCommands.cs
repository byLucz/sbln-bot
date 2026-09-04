using Discord.Commands;
using sblngavnav5X.Core;

public class HelpCommands : ModuleBase<SocketCommandContext>
{
    private readonly PaginatorService _pager;

    public HelpCommands(PaginatorService pager)
    {
        _pager = pager;
    }

    [Command("памаги")]
    public async Task HelpAll()
    {
        await _pager.SendAsync(Context.Channel, HelpEmbedService.GetHelpPages());
    }
}
