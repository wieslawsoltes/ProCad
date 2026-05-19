using ProCad.Editing.Sessions;

namespace ProCad.Editing.Commands;

public sealed class CadScriptCommandHost : ICadScriptCommandHost
{
    private readonly Func<ICadCommandRegistry> _commandRegistryAccessor;

    public CadScriptCommandHost(ICadCommandRegistry commandRegistry)
        : this(() => commandRegistry)
    {
    }

    public CadScriptCommandHost(Func<ICadCommandRegistry> commandRegistryAccessor)
    {
        _commandRegistryAccessor = commandRegistryAccessor;
    }

    public async ValueTask<CadScriptCommandPlaybackResult> ExecuteAsync(
        string script,
        ICadEditorSession? session,
        CadScriptCommandPlaybackOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var commandRegistry = _commandRegistryAccessor();

        if (string.IsNullOrWhiteSpace(script))
        {
            return new CadScriptCommandPlaybackResult(
                Success: true,
                ExecutedCount: 0,
                SucceededCount: 0,
                FailedCount: 0,
                Entries: Array.Empty<CadScriptCommandPlaybackEntry>());
        }

        var playbackOptions = options ?? CadScriptCommandPlaybackOptions.Default;
        var startLine = playbackOptions.NormalizedStartLine;
        var maxCommands = playbackOptions.NormalizedMaxCommands;
        var entries = new List<CadScriptCommandPlaybackEntry>();
        var executedCount = 0;
        var succeededCount = 0;
        var failedCount = 0;

        using var reader = new StringReader(script);
        string? rawLine;
        var lineNumber = 0;
        while ((rawLine = reader.ReadLine()) is not null)
        {
            lineNumber++;
            cancellationToken.ThrowIfCancellationRequested();

            if (lineNumber < startLine)
            {
                continue;
            }

            var input = NormalizeInput(rawLine);
            if (input is null)
            {
                continue;
            }

            if (maxCommands.HasValue && executedCount >= maxCommands.Value)
            {
                break;
            }

            executedCount++;
            var result = await commandRegistry.ExecuteAsync(input, session, cancellationToken).ConfigureAwait(false);
            entries.Add(new CadScriptCommandPlaybackEntry(lineNumber, input, result));

            if (result.Success)
            {
                succeededCount++;
                continue;
            }

            failedCount++;
            if (playbackOptions.StopOnError)
            {
                break;
            }
        }

        return new CadScriptCommandPlaybackResult(
            Success: failedCount == 0,
            ExecutedCount: executedCount,
            SucceededCount: succeededCount,
            FailedCount: failedCount,
            Entries: entries);
    }

    private static string? NormalizeInput(string rawLine)
    {
        var trimmed = rawLine.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.StartsWith("#", StringComparison.Ordinal) ||
            trimmed.StartsWith(";", StringComparison.Ordinal) ||
            trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        return trimmed;
    }
}
