using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProCad.Editing.Commands;

namespace ProCad.Commands;

public sealed class HelpCadCommand : ICadDescribedCommandHandler
{
    private readonly Func<IReadOnlyList<string>> _commandsProvider;

    public HelpCadCommand(Func<IReadOnlyList<string>> commandsProvider)
    {
        _commandsProvider = commandsProvider;
    }

    public string Name => "HELP";
    public IReadOnlyList<string> Aliases => ["?", "H"];
    public CadCommandDescriptor Descriptor => new(
        Name,
        Aliases,
        "Lists registered CAD commands.",
        new[]
        {
            new CadCommandSyntax(
                Usage: "HELP",
                Description: "Lists all registered commands.",
                Parameters: Array.Empty<CadCommandParameterDescriptor>(),
                Keywords: Array.Empty<CadCommandKeywordDescriptor>())
        });

    public bool CanExecute(CadCommandContext context)
    {
        return true;
    }

    public ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
    {
        var commands = _commandsProvider();
        var text = commands.Count == 0
            ? "No commands registered."
            : $"Available: {string.Join(", ", commands)}";
        return ValueTask.FromResult(CadCommandResult.Ok(text));
    }
}
