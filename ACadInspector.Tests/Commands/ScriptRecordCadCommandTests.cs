using ACadInspector.Commands;
using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Prompt;
using ACadInspector.Editing.Sessions;
using ACadInspector.Editing.Undo;
using ACadInspector.Services;
using ACadSharp;

namespace ACadInspector.Tests.Commands;

public sealed class ScriptRecordCadCommandTests
{
    [Fact]
    public async Task ScriptRec_StartAndStop_UpdatesRecorderState()
    {
        var service = new FakeRecorderService();
        var command = new ScriptRecordCadCommand(service);
        var session = CreateSession();

        var startContext = new CadCommandContext(
            session,
            "SCRIPTREC START",
            "SCRIPTREC",
            ["START"],
            CancellationToken.None);
        var stopContext = new CadCommandContext(
            session,
            "SCRIPTREC STOP",
            "SCRIPTREC",
            ["STOP"],
            CancellationToken.None);

        var startResult = await command.ExecuteAsync(startContext);
        var stopResult = await command.ExecuteAsync(stopContext);

        Assert.True(startResult.Success);
        Assert.True(stopResult.Success);
        Assert.Equal(1, service.StartCalls);
        Assert.Equal(1, service.StopCalls);
        Assert.False(service.IsRecording);
    }

    [Fact]
    public async Task ScriptRecSave_MissingPath_ReturnsFailure()
    {
        var service = new FakeRecorderService();
        var command = new ScriptRecordSaveCadCommand(service);
        var context = new CadCommandContext(
            session: CreateSession(),
            rawInput: "SCRIPTRECSAVE",
            commandName: "SCRIPTRECSAVE",
            arguments: Array.Empty<string>(),
            cancellationToken: CancellationToken.None);

        var result = await command.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Contains("Usage", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScriptRecSave_WritesFile()
    {
        var service = new FakeRecorderService
        {
            ScriptText = "LINE 0,0 1,1\n"
        };
        var command = new ScriptRecordSaveCadCommand(service);
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.scr");
        var context = new CadCommandContext(
            session: CreateSession(),
            rawInput: $"SCRIPTRECSAVE {path}",
            commandName: "SCRIPTRECSAVE",
            arguments: [path],
            cancellationToken: CancellationToken.None);

        try
        {
            var result = await command.ExecuteAsync(context);

            Assert.True(result.Success);
            Assert.True(File.Exists(path));
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("LINE 0,0 1,1", content, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ICadEditorSession CreateSession()
    {
        return new CadEditorSessionFactory().Create(new CadDocument());
    }

    private sealed class FakeRecorderService : ICadCommandScriptRecordingService
    {
        public bool IsRecording { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IncludeFailedCommands { get; set; }
        public bool IncludeMetadataComments { get; set; }
        public DateTimeOffset? StartedAtUtc { get; private set; }
        public DateTimeOffset? LastRecordedAtUtc { get; private set; }
        public int EntryCount => Entries.Count;
        public IReadOnlyList<CadScriptRecordingEntry> Entries { get; private set; } = Array.Empty<CadScriptRecordingEntry>();
        public string ScriptText { get; set; } = string.Empty;
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public CadScriptRecordingSnapshot Snapshot => new(
            IsRecording,
            IsPaused,
            IncludeFailedCommands,
            IncludeMetadataComments,
            StartedAtUtc,
            LastRecordedAtUtc,
            EntryCount,
            "status",
            Entries);

        public event EventHandler<CadScriptRecordingSnapshot>? SnapshotChanged;

        public void Start(bool clearExisting = false)
        {
            StartCalls++;
            IsRecording = true;
            IsPaused = false;
            StartedAtUtc = DateTimeOffset.UtcNow;
            SnapshotChanged?.Invoke(this, Snapshot);
        }

        public void Pause()
        {
            IsPaused = true;
            SnapshotChanged?.Invoke(this, Snapshot);
        }

        public void Resume()
        {
            IsPaused = false;
            SnapshotChanged?.Invoke(this, Snapshot);
        }

        public void Stop()
        {
            StopCalls++;
            IsRecording = false;
            IsPaused = false;
            SnapshotChanged?.Invoke(this, Snapshot);
        }

        public void Clear()
        {
            Entries = Array.Empty<CadScriptRecordingEntry>();
            ScriptText = string.Empty;
            SnapshotChanged?.Invoke(this, Snapshot);
        }

        public void Record(CadCommandExecutedEventArgs args)
        {
        }

        public string BuildScript(bool includeHeader = true)
        {
            return ScriptText;
        }

        public async ValueTask<CadScriptRecordingSaveResult> SaveAsync(
            string path,
            bool includeHeader = true,
            CancellationToken cancellationToken = default)
        {
            var fullPath = Path.GetFullPath(path);
            await File.WriteAllTextAsync(fullPath, ScriptText, cancellationToken);
            return new CadScriptRecordingSaveResult(fullPath, EntryCount, 1);
        }
    }
}
