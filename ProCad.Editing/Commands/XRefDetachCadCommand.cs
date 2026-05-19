using ProCad.Editing.Selection;
using ProCad.Editing.Sessions;

namespace ProCad.Editing.Commands;

public sealed class XRefDetachCadCommand : ICadCommandHandler
{
    private const string Usage = "Usage: XREFDETACH blockName";

    public string Name => "XREFDETACH";
    public IReadOnlyList<string> Aliases => ["XD", "-XREFDETACH"];

    public bool CanExecute(CadCommandContext context)
    {
        return context.Session is not null;
    }

    public ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
    {
        if (!CadCommandSessionHelper.TryGetSession(context, out var session, out var error))
        {
            return ValueTask.FromResult(error);
        }

        if (!CadXRefCommandHelpers.TryResolveXRefBlock(
                session,
                context.Arguments,
                Usage,
                out var block,
                out var resolveError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(resolveError));
        }

        var detachedInsertCount = CadXRefCommandHelpers.RemoveInsertReferences(session, block);
        var removed = session.Document.BlockRecords.Remove(block.Name);
        if (removed is null)
        {
            return ValueTask.FromResult(CadCommandResult.Fail($"XREFDETACH could not remove block '{block.Name}'."));
        }

        session.SetSelection(Array.Empty<object?>(), CadSelectionMode.Replace);
        session.MarkExternalMutation(rebuildEntityIndex: true);

        return ValueTask.FromResult(
            CadCommandResult.Ok(
                $"Detached xref '{block.Name}' and removed {detachedInsertCount} insert reference(s)."));
    }
}
