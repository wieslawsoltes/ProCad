namespace ProCad.Editing.Commands;

public sealed class UndoCadCommand : ICadCommandHandler
{
    public string Name => "UNDO";
    public IReadOnlyList<string> Aliases => ["U"];

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

        if (!session.TryUndo(session.SessionId.Value, out _))
        {
            return ValueTask.FromResult(CadCommandResult.Fail("Nothing to undo."));
        }

        return ValueTask.FromResult(CadCommandResult.Ok("Undo complete."));
    }
}
