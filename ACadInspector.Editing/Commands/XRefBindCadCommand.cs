using ACadInspector.Editing.Sessions;

namespace ACadInspector.Editing.Commands;

public sealed class XRefBindCadCommand : ICadCommandHandler
{
    private const string Usage = "Usage: XREFBIND blockName";

    public string Name => "XREFBIND";
    public IReadOnlyList<string> Aliases => ["XBIND", "-XREFBIND"];

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

        CadXRefCommandHelpers.BindToLocalBlock(block);
        session.MarkExternalMutation();

        return ValueTask.FromResult(CadCommandResult.Ok($"Bound xref '{block.Name}' as a local block."));
    }
}
