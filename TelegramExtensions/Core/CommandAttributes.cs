using Telegram.Bot.Types.Enums;

namespace sblngavnav6.TelegramExtensions.Core;

[AttributeUsage(AttributeTargets.Method)]
public sealed class CommandAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class AliasAttribute(params string[] aliases) : Attribute
{
    public IReadOnlyList<string> Aliases { get; } = aliases;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public abstract class PreconditionAttribute : Attribute
{
    public abstract Task<string?> CheckAsync(TelegramCommandContext context);
}

public sealed class RequireGroupAttribute : PreconditionAttribute
{
    public override Task<string?> CheckAsync(TelegramCommandContext context)
        => Task.FromResult(context.Message.Chat.Type is ChatType.Group or ChatType.Supergroup or ChatType.Channel
            ? null : "напиши эту команду в группе или канале 😋");
}
