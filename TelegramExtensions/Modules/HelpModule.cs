using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using sblngavnav6.TelegramExtensions.Core;
using Telegram.Bot.Types.Enums;

namespace sblngavnav6.TelegramExtensions.Modules;

public sealed class HelpModule : TelegramModuleBase
{
    [Command("памаги")]
    [Alias("помоги", "помощь", "хелп")]
    public async Task HelpAsync()
    {
        var sections = Context.Services.GetService<ITelegramHelpSource>()?.Sections;

        if (sections is not { Count: > 0 })
        {
            await ReplyAsync("список команд пока недоступен 😕");
            return;
        }

        var text = new StringBuilder();

        foreach (var section in sections)
        {
            text.AppendLine().AppendLine()
                .Append("<b>").Append(WebUtility.HtmlEncode(section.Title)).AppendLine("</b>");

            foreach (var command in section.Commands)
                text.Append("<code>").Append(WebUtility.HtmlEncode(command.Command)).Append("</code> - ")
                    .AppendLine(WebUtility.HtmlEncode(command.About));
        }

        text.AppendLine().Append("<i>остальные команды доступны только в дискорде</i>");

        await ReplyAsync(text.ToString(), ParseMode.Html);
    }
}
