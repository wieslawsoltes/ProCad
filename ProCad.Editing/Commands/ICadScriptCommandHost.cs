using ProCad.Editing.Sessions;

namespace ProCad.Editing.Commands;

public interface ICadScriptCommandHost
{
    ValueTask<CadScriptCommandPlaybackResult> ExecuteAsync(
        string script,
        ICadEditorSession? session,
        CadScriptCommandPlaybackOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed record CadScriptCommandPlaybackOptions(
    bool StopOnError = true,
    int StartLine = 1,
    int? MaxCommands = null)
{
    public static CadScriptCommandPlaybackOptions Default { get; } = new();

    public int NormalizedStartLine => StartLine < 1 ? 1 : StartLine;

    public int? NormalizedMaxCommands => MaxCommands is > 0 ? MaxCommands : null;
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
