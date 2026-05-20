using ProCad.Commands;
using ProCad.Editing.Commands;
using ProCad.Editing.Sessions;
using ACadSharp;

namespace ProCad.Tests.Commands;

public sealed class ScriptFileCadCommandTests
{
    [Fact]
    public async Task ExecuteAsync_DefaultMode_UsesStopOnError()
    {
        var host = new RecordingScriptCommandHost
        {
            Result = new CadScriptCommandPlaybackResult(
                Success: true,
                ExecutedCount: 2,
                SucceededCount: 2,
                FailedCount: 0,
                Entries: Array.Empty<CadScriptCommandPlaybackEntry>())
        };
        var command = new ScriptFileCadCommand(host);
        var session = CreateSession();

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.scr");
        await File.WriteAllTextAsync(path, "LINE 0,0 1,0\nPOINT 2,2\n");

        try
        {
            var context = new CadCommandContext(
                session,
                $"SCRIPT {path}",
                "SCRIPT",
                [path],
                CancellationToken.None);

            var result = await command.ExecuteAsync(context);

            Assert.True(result.Success);
            Assert.NotNull(host.Options);
            Assert.True(host.Options!.StopOnError);
            Assert.Equal("LINE 0,0 1,0\nPOINT 2,2\n", host.ScriptText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ContinueFlag_DisablesStopOnError()
    {
        var host = new RecordingScriptCommandHost
        {
            Result = new CadScriptCommandPlaybackResult(
                Success: false,
                ExecutedCount: 3,
                SucceededCount: 2,
                FailedCount: 1,
                Entries:
                [
                    new CadScriptCommandPlaybackEntry(2, "UNKNOWN", CadCommandResult.Fail("Unknown command"))
                ])
        };
        var command = new ScriptFileCadCommand(host);
        var session = CreateSession();

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.scr");
        await File.WriteAllTextAsync(path, "LINE 0,0 1,0\nUNKNOWN\nPOINT 2,2\n");

        try
        {
            var context = new CadCommandContext(
                session,
                $"SCRIPT {path} CONTINUE",
                "SCRIPT",
                [path, "CONTINUE"],
                CancellationToken.None);

            var result = await command.ExecuteAsync(context);

            Assert.False(result.Success);
            Assert.Contains("line 2", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(host.Options);
            Assert.False(host.Options!.StopOnError);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_MissingFile_ReturnsFailure()
    {
        var host = new RecordingScriptCommandHost
        {
            Result = new CadScriptCommandPlaybackResult(
                Success: true,
                ExecutedCount: 0,
                SucceededCount: 0,
                FailedCount: 0,
                Entries: Array.Empty<CadScriptCommandPlaybackEntry>())
        };
        var command = new ScriptFileCadCommand(host);
        var session = CreateSession();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.scr");

        var context = new CadCommandContext(
            session,
            $"SCRIPT {path}",
            "SCRIPT",
            [path],
            CancellationToken.None);

        var result = await command.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Contains("was not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ICadEditorSession CreateSession()
    {
        return new CadEditorSessionFactory().Create(new CadDocument());
    }

    private sealed class RecordingScriptCommandHost : ICadScriptCommandHost
    {
        public CadScriptCommandPlaybackResult Result { get; set; } =
            new(true, 0, 0, 0, Array.Empty<CadScriptCommandPlaybackEntry>());

        public string? ScriptText { get; private set; }
        public ICadEditorSession? Session { get; private set; }
        public CadScriptCommandPlaybackOptions? Options { get; private set; }

        public ValueTask<CadScriptCommandPlaybackResult> ExecuteAsync(
            string script,
            ICadEditorSession? session,
            CadScriptCommandPlaybackOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ScriptText = script;
            Session = session;
            Options = options;
            return ValueTask.FromResult(Result);
        }
    }
}
