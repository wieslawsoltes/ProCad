using ACadInspector.Editing.Sessions;

namespace ACadInspector.Editing.Commands;

public interface ICadScriptCommandHost
{
    ValueTask<CadScriptCommandPlaybackResult> ExecuteAsync(
        string script,
        ICadEditorSession? session,
        CadScriptCommandPlaybackOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed record CadScriptCommandPlaybackOptions(bool StopOnError = true)
{
    public static CadScriptCommandPlaybackOptions Default { get; } = new();
}

public sealed record CadScriptCommandPlaybackEntry(
    int LineNumber,
    string Input,
    CadCommandResult Result);

public sealed record CadScriptCommandPlaybackResult(
    bool Success,
    int ExecutedCount,
    int SucceededCount,
    int FailedCount,
    IReadOnlyList<CadScriptCommandPlaybackEntry> Entries);
