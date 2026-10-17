using System.Linq.Expressions;
using System.Reflection;

namespace sblngavnav6.TelegramExtensions.Core;

internal sealed class CommandCatalog
{
    private readonly Dictionary<string, CommandDescriptor> _commands = new(StringComparer.OrdinalIgnoreCase);

    public CommandCatalog(IEnumerable<Type> moduleTypes)
    {
        foreach (var moduleType in moduleTypes)
        {
            var moduleChecks = moduleType.GetCustomAttributes<PreconditionAttribute>(inherit: true).ToArray();
            foreach (var method in moduleType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
            {
                if (method.GetCustomAttribute<CommandAttribute>() is not { } command) continue;
                if (method.IsStatic || method.ContainsGenericParameters || method.GetParameters().Length != 0 ||
                    !typeof(Task).IsAssignableFrom(method.ReturnType))
                    throw new InvalidOperationException($"Telegram command {moduleType.Name}.{method.Name} must be an instance method returning Task without parameters. Read arguments from Context.Arguments.");

                var module = Expression.Parameter(typeof(TelegramModuleBase));
                var call = Expression.Call(Expression.Convert(module, moduleType), method);
                var execute = Expression.Lambda<Func<TelegramModuleBase, Task>>(
                    Expression.Convert(call, typeof(Task)), module).Compile();
                var checks = moduleChecks.Concat(method.GetCustomAttributes<PreconditionAttribute>(inherit: true)).ToArray();
                var descriptor = new CommandDescriptor(command.Name, moduleType, execute, checks);
                var aliases = method.GetCustomAttribute<AliasAttribute>()?.Aliases ?? [];
                foreach (var name in new[] { command.Name }.Concat(aliases))
                {
                    if (string.IsNullOrWhiteSpace(name) || name.Any(c => char.IsWhiteSpace(c) || c is '/' or '@'))
                        throw new InvalidOperationException($"Invalid Telegram command name in {moduleType.Name}.{method.Name}.");
                    if (!_commands.TryAdd(name, descriptor))
                        throw new InvalidOperationException($"Duplicate Telegram command or alias: {name}.");
                }
            }
        }
    }

    public bool TryGet(string name, out CommandDescriptor command) => _commands.TryGetValue(name, out command!);
}

internal sealed record CommandDescriptor(string Name, Type ModuleType, Func<TelegramModuleBase, Task> Execute,
    IReadOnlyList<PreconditionAttribute> Preconditions);
