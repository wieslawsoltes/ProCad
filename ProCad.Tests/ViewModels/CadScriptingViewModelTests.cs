using System.Reactive.Threading.Tasks;
using ProCad.Core;
using ProCad.Editing.Commands;
using ProCad.Editing.Prompt;
using ProCad.Editing.Sessions;
using ProCad.Editing.Undo;
using ProCad.Services;
using ProCad.Scripting;
using ProCad.ViewModels;
using ACadSharp;

namespace ProCad.Tests.ViewModels;

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

    [Fact]
    public async Task RunCommandScriptCommand_ForwardsPlaybackRangeOptions()
    {
        var harness = CreateHarness();
        harness.ViewModel.CommandScriptDocument.Text = "LINE 0,0 1,1";
        harness.ViewModel.ContinueOnCommandError = true;
        harness.ViewModel.CommandStartLine = "3";
        harness.ViewModel.CommandMaxCommands = "5";

        await harness.ViewModel.RunCommandScriptCommand.Execute().ToTask();

        var options = Assert.IsType<CadScriptCommandPlaybackOptions>(harness.CommandHost.Options);
        Assert.False(options.StopOnError);
        Assert.Equal(3, options.StartLine);
        Assert.Equal(5, options.MaxCommands);
    }

    [Fact]
    public async Task RunCommandScriptCommand_InvalidStartLine_ShowsValidationAndSkipsExecution()
    {
        var harness = CreateHarness();
        harness.ViewModel.CommandScriptDocument.Text = "LINE 0,0 1,1";
        harness.ViewModel.CommandStartLine = "0";

        await harness.ViewModel.RunCommandScriptCommand.Execute().ToTask();

        Assert.Contains("Start line must be an integer", harness.ViewModel.CommandStatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(harness.CommandHost.Options);
    }

    [Fact]
    public async Task MacroCommands_SaveApplyDeleteWorkflow()
    {
        var harness = CreateHarness();
        harness.ViewModel.MacroName = "LineMacro";
        harness.ViewModel.CommandScriptDocument.Text = "LINE 0,0 1,1";
        harness.ViewModel.ContinueOnCommandError = true;

        await harness.ViewModel.SaveMacroCommand.Execute().ToTask();
        var savedMacro = Assert.Single(harness.ViewModel.Macros);
        Assert.Equal("LineMacro", savedMacro.Name);

        harness.ViewModel.CommandScriptDocument.Text = "POINT 2,2";
        harness.ViewModel.ContinueOnCommandError = false;
        harness.ViewModel.SelectedMacro = savedMacro;
        await harness.ViewModel.ApplyMacroCommand.Execute().ToTask();

        Assert.Equal("LINE 0,0 1,1", harness.ViewModel.CommandScriptDocument.Text);
        Assert.True(harness.ViewModel.ContinueOnCommandError);

        await harness.ViewModel.DeleteMacroCommand.Execute().ToTask();
        Assert.Empty(harness.ViewModel.Macros);
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
        public CadScriptCommandPlaybackOptions? Options { get; private set; }

        public ValueTask<CadScriptCommandPlaybackResult> ExecuteAsync(
            string script,
            ICadEditorSession? session,
            CadScriptCommandPlaybackOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ScriptText = script;
            Options = options;
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
