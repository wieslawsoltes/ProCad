using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProCad.Editing.Commands;
using ProCad.Services;

namespace ProCad.Commands;

public sealed class ClearSelectionCadCommand : ICadDescribedCommandHandler
{
    private readonly CadSelectionService _selectionService;

    public ClearSelectionCadCommand(CadSelectionService selectionService)
    {
        _selectionService = selectionService;
    }

    public string Name => "CLEARSEL";
    public IReadOnlyList<string> Aliases => ["CS"];
    public CadCommandDescriptor Descriptor => new(
        Name,
        Aliases,
        "Clears the current selection set.",
        new[]
        {
            new CadCommandSyntax(
                Usage: "CLEARSEL",
                Description: "Clears all selected entities.",
                Parameters: Array.Empty<CadCommandParameterDescriptor>(),
                Keywords: Array.Empty<CadCommandKeywordDescriptor>())
        });

    public bool CanExecute(CadCommandContext context)
    {
        return true;
    }

    public ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
    {
        _selectionService.ClearSelection();
        return ValueTask.FromResult(CadCommandResult.Ok("Selection cleared."));
    }
}
