using ACadInspector.Editing.Sessions;
using ACadSharp.Blocks;

namespace ACadInspector.Editing.Commands;

public sealed class XRefReloadCadCommand : ICadCommandHandler
{
    private const string Usage = "Usage: XREFRELOAD blockName";

    public string Name => "XREFRELOAD";
    public IReadOnlyList<string> Aliases => ["XR", "-XREFRELOAD"];

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

        if (string.IsNullOrWhiteSpace(block.BlockEntity.XRefPath))
        {
            return ValueTask.FromResult(CadCommandResult.Fail($"XREFRELOAD cannot reload '{block.Name}' because the xref path is empty."));
        }

        block.IsUnloaded = false;
        block.Flags |= BlockTypeFlags.XRefResolved;
        session.MarkExternalMutation();
        return ValueTask.FromResult(CadCommandResult.Ok($"Reloaded xref '{block.Name}'."));
    }
}
