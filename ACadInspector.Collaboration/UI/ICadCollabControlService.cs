namespace ACadInspector.Collaboration.UI;

public interface ICadCollabControlService
{
    CadCollabConnectionOptions CurrentOptions { get; }

    ValueTask JoinAsync(CancellationToken cancellationToken = default);
    ValueTask LeaveAsync(CancellationToken cancellationToken = default);
    ValueTask<string> ShareAsync(CancellationToken cancellationToken = default);
    ValueTask ApplyConnectionOptionsAsync(CadCollabConnectionOptions options, CancellationToken cancellationToken = default);
    ValueTask ReconnectAsync(CancellationToken cancellationToken = default);
    ValueTask ResyncAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> ReapplyConflictAsync(string conflictId, CancellationToken cancellationToken = default);
}

public sealed class NullCadCollabControlService : ICadCollabControlService
{
    public CadCollabConnectionOptions CurrentOptions => CadCollabConnectionOptions.Default;

    public ValueTask JoinAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask LeaveAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask<string> ShareAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(string.Empty);
    }

    public ValueTask ApplyConnectionOptionsAsync(CadCollabConnectionOptions options, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask ReconnectAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask ResyncAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> ReapplyConflictAsync(string conflictId, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(false);
    }
}
