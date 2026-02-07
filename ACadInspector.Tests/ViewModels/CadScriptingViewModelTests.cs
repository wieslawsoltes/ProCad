using System.Reactive.Threading.Tasks;
using ACadInspector.Core;
using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Prompt;
using ACadInspector.Editing.Sessions;
using ACadInspector.Editing.Undo;
using ACadInspector.Services;
using ACadInspector.Scripting;
using ACadInspector.ViewModels;
using ACadSharp;

namespace ACadInspector.Tests.ViewModels;

public sealed class CadScriptingViewModelTests
{
    [Fact]
    public async Task LoadRecordingCommand_LoadsRecordedScriptIntoCommandEditor()
    {
        var harness = CreateHarness();
        harness.Recording.Start(clearExisting: true);
        harness.Recording.Record(new CadCommandExecutedEventArgs(
            Input: "LINE 0,0 1,1",
            CommandName: "LINE",
            Result: CadCommandResult.Ok("ok"),
            Source: CadUndoSource.CommandLine,
            IsTransparent: false,
            TimestampUtc: DateTimeOffset.UtcNow));
        harness.Recording.Stop();

        await harness.ViewModel.LoadRecordingCommand.Execute().ToTask();

        Assert.Contains("LINE 0,0 1,1", harness.ViewModel.CommandScriptDocument.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunCommandScriptCommand_ExecutesPlaybackHost()
    {
        var harness = CreateHarness();
        harness.ViewModel.CommandScriptDocument.Text = "LINE 0,0 1,1";

        await harness.ViewModel.RunCommandScriptCommand.Execute().ToTask();

        Assert.Equal("LINE 0,0 1,1", harness.CommandHost.ScriptText);
        Assert.Contains("completed", harness.ViewModel.CommandStatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static Harness CreateHarness()
    {
        var scriptHost = new FakeScriptHost();
        var commandHost = new FakeScriptCommandHost();
        var document = new CadDocument();
        var active = new CadDocumentViewModel(
            document,
            CadFileFormat.Dxf,
            path: null,
            displayName: "active.dxf",
            render: null!);
        var documentContext = new CadDocumentContextService
        {
            ActiveDocument = active
        };
        var selection = new CadSelectionService();
        var workspace = new CadScriptWorkspaceService();
        var sessionHost = new CadEditorSessionHostService(new CadEditorSessionFactory(), documentContext, selection);
        var recording = new CadCommandScriptRecordingService();
        var viewModel = new CadScriptingViewModel(
            scriptHost,
            commandHost,
            documentContext,
            selection,
            workspace,
            sessionHost,
            recording);
        return new Harness(viewModel, commandHost, recording);
    }

    private sealed record Harness(
        CadScriptingViewModel ViewModel,
        FakeScriptCommandHost CommandHost,
        CadCommandScriptRecordingService Recording);

    private sealed class FakeScriptHost : ICadScriptHost
    {
        public Task<CadScriptExecutionResult> ExecuteAsync(
            string script,
            CadScriptGlobals globals,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CadScriptExecutionResult.FromSuccess(
                returnValue: null,
                output: string.Empty,
                diagnostics: Array.Empty<string>(),
                duration: TimeSpan.Zero));
        }
    }

    private sealed class FakeScriptCommandHost : ICadScriptCommandHost
    {
        public string ScriptText { get; private set; } = string.Empty;

        public ValueTask<CadScriptCommandPlaybackResult> ExecuteAsync(
            string script,
            ICadEditorSession? session,
            CadScriptCommandPlaybackOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ScriptText = script;
            return ValueTask.FromResult(new CadScriptCommandPlaybackResult(
                Success: true,
                ExecutedCount: 1,
                SucceededCount: 1,
                FailedCount: 0,
                Entries:
                [
                    new CadScriptCommandPlaybackEntry(
                        LineNumber: 1,
                        Input: script,
                        Result: CadCommandResult.Ok("ok"))
                ]));
        }
    }
}
