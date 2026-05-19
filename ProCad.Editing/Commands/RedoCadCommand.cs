namespace ProCad.Editing.Commands;

public sealed class RedoCadCommand : ICadCommandHandler
{
    public string Name => "REDO";
    public IReadOnlyList<string> Aliases => ["RE"];

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

        if (!session.TryRedo(session.SessionId.Value, out _))
        {
            return ValueTask.FromResult(CadCommandResult.Fail("Nothing to redo."));
        }

        return ValueTask.FromResult(CadCommandResult.Ok("Redo complete."));
    }
}
