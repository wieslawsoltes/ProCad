using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ProCad.Editing.Commands;
using ProCad.Services;
using ProCad.Scripting;
using AvaloniaEdit.Document;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class CadScriptingViewModel : CadToolViewModelBase, IDisposable
{
    private readonly ICadScriptHost _scriptHost;
    private readonly ICadScriptCommandHost _scriptCommandHost;
    private readonly CadDocumentContextService _documentContext;
    private readonly CadSelectionService _selectionService;
    private readonly CadScriptWorkspaceService _workspace;
    private readonly CadEditorSessionHostService _sessionHost;
    private readonly ICadCommandScriptRecordingService _recordingService;
    private readonly ObservableCollection<CadScriptMacroEntryViewModel> _macros = new();
    private CancellationTokenSource? _execution;
    private CancellationTokenSource? _commandExecution;
    private readonly IDisposable _savePathSubscription;
    private readonly IDisposable _includeFailedSubscription;
    private readonly IDisposable _includeMetadataSubscription;
    private readonly IDisposable _includeTimestampSubscription;
    private readonly IDisposable _commandStartLineSubscription;
    private readonly IDisposable _commandMaxCommandsSubscription;
    private readonly IDisposable _macroNameSubscription;
    private bool _disposed;

    public TextDocument ScriptDocument { get; } = new();

    public TextDocument OutputDocument { get; } = new();

    public TextDocument CommandScriptDocument { get; } = new();

    public TextDocument CommandOutputDocument { get; } = new();

    [Reactive]
    public partial string StatusMessage { get; set; } = "Ready.";

    [Reactive]
    public partial bool IsRunning { get; set; }

    [Reactive]
    public partial string CommandStatusMessage { get; set; } = "Ready.";

    [Reactive]
    public partial bool IsCommandScriptRunning { get; set; }

    [Reactive]
    public partial bool ContinueOnCommandError { get; set; }

    [Reactive]
    public partial string RecordingStatusMessage { get; set; } = "Script recorder idle.";

    [Reactive]
    public partial bool IsRecording { get; set; }

    [Reactive]
    public partial bool IsRecordingPaused { get; set; }

    [Reactive]
    public partial int RecordedCommandCount { get; set; }

    [Reactive]
    public partial bool IncludeFailedRecordedCommands { get; set; }

    [Reactive]
    public partial bool IncludeRecordingMetadataComments { get; set; } = true;

    [Reactive]
    public partial bool IncludeRecordingTimestampComments { get; set; } = true;

    [Reactive]
    public partial string RecordingSavePath { get; set; } = string.Empty;

    [Reactive]
    public partial string CommandStartLine { get; set; } = "1";

    [Reactive]
    public partial string CommandMaxCommands { get; set; } = string.Empty;

    [Reactive]
    public partial string MacroName { get; set; } = string.Empty;

    [Reactive]
    public partial CadScriptMacroEntryViewModel? SelectedMacro { get; set; }

    public ReadOnlyObservableCollection<CadScriptMacroEntryViewModel> Macros { get; }

    public ReactiveCommand<Unit, Unit> RunCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearOutputCommand { get; }

    public ReactiveCommand<Unit, Unit> ResetTemplateCommand { get; }

    public ReactiveCommand<Unit, Unit> RunCommandScriptCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCommandScriptCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearCommandOutputCommand { get; }

    public ReactiveCommand<Unit, Unit> ResetCommandTemplateCommand { get; }

    public ReactiveCommand<Unit, Unit> StartRecordingCommand { get; }

    public ReactiveCommand<Unit, Unit> PauseRecordingCommand { get; }

    public ReactiveCommand<Unit, Unit> ResumeRecordingCommand { get; }

    public ReactiveCommand<Unit, Unit> StopRecordingCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearRecordingCommand { get; }

    public ReactiveCommand<Unit, Unit> LoadRecordingCommand { get; }

    public ReactiveCommand<Unit, Unit> SaveRecordingCommand { get; }

    public ReactiveCommand<Unit, Unit> SaveMacroCommand { get; }

    public ReactiveCommand<Unit, Unit> DeleteMacroCommand { get; }

    public ReactiveCommand<Unit, Unit> ApplyMacroCommand { get; }

    public CadScriptingViewModel(
        ICadScriptHost scriptHost,
        ICadScriptCommandHost scriptCommandHost,
        CadDocumentContextService documentContext,
        CadSelectionService selectionService,
        CadScriptWorkspaceService workspace,
        CadEditorSessionHostService sessionHost,
        ICadCommandScriptRecordingService recordingService)
    {
        _scriptHost = scriptHost;
        _scriptCommandHost = scriptCommandHost;
        _documentContext = documentContext;
        _selectionService = selectionService;
        _workspace = workspace;
        _sessionHost = sessionHost;
        _recordingService = recordingService;
        Macros = new ReadOnlyObservableCollection<CadScriptMacroEntryViewModel>(_macros);

        InitializeScriptText();
        InitializeCommandScriptText();
        InitializeRecordingState();
        InitializeCommandPlaybackState();
        InitializeMacros();

        var canRun = this.WhenAnyValue(x => x.IsRunning, running => !running);
        var canCancel = this.WhenAnyValue(x => x.IsRunning);
        var canRunCommandScript = this.WhenAnyValue(x => x.IsCommandScriptRunning, running => !running);
        var canCancelCommandScript = this.WhenAnyValue(x => x.IsCommandScriptRunning);
        var canStartRecording = this.WhenAnyValue(x => x.IsRecording, x => x.IsRecordingPaused, (recording, paused) => !recording || paused);
        var canPauseRecording = this.WhenAnyValue(x => x.IsRecording, x => x.IsRecordingPaused, (recording, paused) => recording && !paused);
        var canResumeRecording = this.WhenAnyValue(x => x.IsRecording, x => x.IsRecordingPaused, (recording, paused) => recording && paused);
        var canStopRecording = this.WhenAnyValue(x => x.IsRecording);
        var canSaveRecording = this.WhenAnyValue(x => x.RecordedCommandCount, count => count > 0);
        var hasSelectedMacro = this.WhenAnyValue(x => x.SelectedMacro)
            .Select(macro => macro is not null);
        var canSaveMacro = this.WhenAnyValue(x => x.MacroName)
            .Select(name => !string.IsNullOrWhiteSpace(name));

        RunCommand = ReactiveCommand.CreateFromTask(RunAsync, canRun);
        CancelCommand = ReactiveCommand.Create(CancelExecution, canCancel);
        ClearOutputCommand = ReactiveCommand.Create(ClearOutput, canRun);
        ResetTemplateCommand = ReactiveCommand.Create(LoadTemplate, canRun);
        RunCommandScriptCommand = ReactiveCommand.CreateFromTask(RunCommandScriptAsync, canRunCommandScript);
        CancelCommandScriptCommand = ReactiveCommand.Create(CancelCommandScriptExecution, canCancelCommandScript);
        ClearCommandOutputCommand = ReactiveCommand.Create(ClearCommandOutput, canRunCommandScript);
        ResetCommandTemplateCommand = ReactiveCommand.Create(LoadCommandTemplate, canRunCommandScript);
        StartRecordingCommand = ReactiveCommand.Create(StartRecording, canStartRecording);
        PauseRecordingCommand = ReactiveCommand.Create(PauseRecording, canPauseRecording);
        ResumeRecordingCommand = ReactiveCommand.Create(ResumeRecording, canResumeRecording);
        StopRecordingCommand = ReactiveCommand.Create(StopRecording, canStopRecording);
        ClearRecordingCommand = ReactiveCommand.Create(ClearRecording);
        LoadRecordingCommand = ReactiveCommand.Create(LoadRecordingIntoCommandScript, canSaveRecording);
        SaveRecordingCommand = ReactiveCommand.CreateFromTask(SaveRecordingAsync, canSaveRecording);
        SaveMacroCommand = ReactiveCommand.Create(SaveMacro, canSaveMacro);
        DeleteMacroCommand = ReactiveCommand.Create(DeleteSelectedMacro, hasSelectedMacro);
        ApplyMacroCommand = ReactiveCommand.Create(ApplySelectedMacro, hasSelectedMacro);

        _recordingService.SnapshotChanged += OnRecordingSnapshotChanged;

        _savePathSubscription = this.WhenAnyValue(x => x.RecordingSavePath)
            .Skip(1)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(path => _workspace.RecordingSavePath = path);

        _includeFailedSubscription = this.WhenAnyValue(x => x.IncludeFailedRecordedCommands)
            .Skip(1)
            .Subscribe(value => _recordingService.IncludeFailedCommands = value);

        _includeMetadataSubscription = this.WhenAnyValue(x => x.IncludeRecordingMetadataComments)
            .Skip(1)
            .Subscribe(value => _recordingService.IncludeMetadataComments = value);

        _includeTimestampSubscription = this.WhenAnyValue(x => x.IncludeRecordingTimestampComments)
            .Skip(1)
            .Subscribe(value => _recordingService.IncludeTimestampComments = value);

        _commandStartLineSubscription = this.WhenAnyValue(x => x.CommandStartLine)
            .Skip(1)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(value => _workspace.CommandStartLine = value);

        _commandMaxCommandsSubscription = this.WhenAnyValue(x => x.CommandMaxCommands)
            .Skip(1)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(value => _workspace.CommandMaxCommands = value);

        _macroNameSubscription = this.WhenAnyValue(x => x.MacroName)
            .Skip(1)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(value => _workspace.LastMacroName = value);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        StatusMessage = "Running script...";
        _execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _execution.Token;
        try
        {
            var globals = CreateGlobals(token);
            var result = await _scriptHost.ExecuteAsync(ScriptDocument.Text, globals, token).ConfigureAwait(true);

            AppendOutput(result);
            StatusMessage = result.Success
                ? $"Completed in {result.Duration.TotalMilliseconds:F0} ms."
                : "Script failed.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Script execution canceled.";
        }
        finally
        {
            IsRunning = false;
            _execution.Dispose();
            _execution = null;
        }
    }

    private void CancelExecution()
    {
        _execution?.Cancel();
        StatusMessage = "Cancelling...";
    }

    private void ClearOutput()
    {
        OutputDocument.Text = string.Empty;
        StatusMessage = "Output cleared.";
    }

    private async Task RunCommandScriptAsync(CancellationToken cancellationToken)
    {
        if (IsCommandScriptRunning)
        {
            return;
        }

        var commandScript = CommandScriptDocument.Text;
        if (string.IsNullOrWhiteSpace(commandScript))
        {
            CommandStatusMessage = "Command script is empty.";
            return;
        }

        var activeDocument = _documentContext.ActiveDocument?.Document;
        if (activeDocument is null)
        {
            CommandStatusMessage = "No active document.";
            return;
        }

        IsCommandScriptRunning = true;
        CommandStatusMessage = "Running command script...";
        _commandExecution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _commandExecution.Token;

        try
        {
            var session = _sessionHost.GetOrCreate(activeDocument);
            _sessionHost.SyncSelectionToSession(session);
            if (!TryCreatePlaybackOptions(out var options, out var parseError))
            {
                CommandStatusMessage = parseError;
                return;
            }

            var result = await _scriptCommandHost
                .ExecuteAsync(commandScript, session, options, token)
                .ConfigureAwait(true);

            AppendCommandOutput(result);
            _sessionHost.SyncSelectionToUi(session);
            if (HasOperations(result))
            {
                _sessionHost.NotifySessionChanged(session);
            }

            CommandStatusMessage = result.Success
                ? string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Command script completed. Executed {result.ExecutedCount} command(s).")
                : string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Command script finished with {result.FailedCount} failure(s).");
        }
        catch (OperationCanceledException)
        {
            CommandStatusMessage = "Command script canceled.";
        }
        catch (Exception ex)
        {
            CommandStatusMessage = $"Command script failed: {ex.Message}";
        }
        finally
        {
            IsCommandScriptRunning = false;
            _commandExecution?.Dispose();
            _commandExecution = null;
        }
    }

    private void CancelCommandScriptExecution()
    {
        _commandExecution?.Cancel();
        CommandStatusMessage = "Cancelling command script...";
    }

    private void ClearCommandOutput()
    {
        CommandOutputDocument.Text = string.Empty;
        CommandStatusMessage = "Command output cleared.";
    }

    private void LoadTemplate()
    {
        SetScriptText("// Script globals: Document, Documents, Selection, Format, DocumentName, DocumentPath, Log, CancellationToken\n" +
                      "if (Document == null)\n" +
                      "{\n" +
                      "    Log?.Invoke(\"No active document.\");\n" +
                      "    return;\n" +
                      "}\n" +
                      "Log?.Invoke($\"Entities: {Document.Entities?.Count ?? 0}\");\n");
    }

    private void LoadCommandTemplate()
    {
        SetCommandScriptText(
            "; AutoCAD-style command script\n" +
            "; One command per line\n" +
            "LINE 0,0 100,0\n" +
            "CIRCLE 50,25 15\n");
        CommandStatusMessage = "Command template loaded.";
    }

    private CadScriptGlobals CreateGlobals(CancellationToken token)
    {
        var active = _documentContext.ActiveDocument;
        var documents = _documentContext.GetDocuments();

        return new CadScriptGlobals
        {
            Document = active?.Document,
            Documents = documents,
            Selection = _selectionService.SelectedObject,
            Format = active?.Format,
            DocumentName = active?.Title,
            DocumentPath = active?.Path,
            CancellationToken = token
        };
    }

    private void AppendOutput(CadScriptExecutionResult result)
    {
        var builder = new StringBuilder();
        if (result.Diagnostics.Count > 0)
        {
            builder.AppendLine("Diagnostics:");
            foreach (var diag in result.Diagnostics)
            {
                builder.AppendLine(diag);
            }
        }

        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            builder.AppendLine("Output:");
            builder.AppendLine(result.Output);
        }

        if (result.ReturnValue is not null)
        {
            builder.AppendLine("Return:");
            builder.AppendLine(result.ReturnValue.ToString());
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.AppendLine("Error:");
            builder.AppendLine(result.Error);
        }

        if (builder.Length == 0)
        {
            builder.AppendLine(result.Success ? "Completed with no output." : "Script failed.");
        }

        if (!string.IsNullOrWhiteSpace(OutputDocument.Text))
        {
            OutputDocument.Text += Environment.NewLine;
        }

        OutputDocument.Text += builder.ToString();
    }

    private void AppendCommandOutput(CadScriptCommandPlaybackResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Executed={result.ExecutedCount}, Succeeded={result.SucceededCount}, Failed={result.FailedCount}"));

        foreach (var entry in result.Entries)
        {
            var state = entry.Result.Success ? "OK" : "FAIL";
            builder.Append('[');
            builder.Append(state);
            builder.Append("] L");
            builder.Append(entry.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(": ");
            builder.Append(entry.Input);
            if (!string.IsNullOrWhiteSpace(entry.Result.Message))
            {
                builder.Append(" => ");
                builder.Append(entry.Result.Message);
            }

            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(CommandOutputDocument.Text))
        {
            CommandOutputDocument.Text += Environment.NewLine;
        }

        CommandOutputDocument.Text += builder.ToString();
    }

    private void InitializeScriptText()
    {
        if (string.IsNullOrWhiteSpace(_workspace.ScriptText))
        {
            LoadTemplate();
        }
        else
        {
            ScriptDocument.Text = _workspace.ScriptText;
        }

        ScriptDocument.Changed += (_, _) =>
        {
            _workspace.ScriptText = ScriptDocument.Text;
        };
    }

    private void SetScriptText(string text)
    {
        ScriptDocument.Text = text;
        _workspace.ScriptText = text;
    }

    private void InitializeCommandScriptText()
    {
        if (string.IsNullOrWhiteSpace(_workspace.CommandScriptText))
        {
            LoadCommandTemplate();
        }
        else
        {
            CommandScriptDocument.Text = _workspace.CommandScriptText;
        }

        CommandScriptDocument.Changed += (_, _) =>
        {
            _workspace.CommandScriptText = CommandScriptDocument.Text;
        };
    }

    private void SetCommandScriptText(string text)
    {
        CommandScriptDocument.Text = text;
        _workspace.CommandScriptText = text;
    }

    private void InitializeRecordingState()
    {
        if (string.IsNullOrWhiteSpace(_workspace.RecordingSavePath))
        {
            _workspace.RecordingSavePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "procad_recording.scr");
        }

        RecordingSavePath = _workspace.RecordingSavePath;
        IncludeFailedRecordedCommands = _recordingService.IncludeFailedCommands;
        IncludeRecordingMetadataComments = _recordingService.IncludeMetadataComments;
        IncludeRecordingTimestampComments = _recordingService.IncludeTimestampComments;
        ApplyRecordingSnapshot(_recordingService.Snapshot);
    }

    private void InitializeCommandPlaybackState()
    {
        CommandStartLine = string.IsNullOrWhiteSpace(_workspace.CommandStartLine)
            ? "1"
            : _workspace.CommandStartLine;
        CommandMaxCommands = _workspace.CommandMaxCommands ?? string.Empty;
    }

    private void InitializeMacros()
    {
        LoadMacrosFromWorkspace();
        MacroName = _workspace.LastMacroName;
        if (_macros.Count > 0)
        {
            SelectedMacro = _macros[0];
            if (string.IsNullOrWhiteSpace(MacroName))
            {
                MacroName = SelectedMacro.Name;
            }
        }
    }

    private void SaveMacro()
    {
        var name = MacroName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            CommandStatusMessage = "Macro name is required.";
            return;
        }

        var script = CommandScriptDocument.Text;
        if (string.IsNullOrWhiteSpace(script))
        {
            CommandStatusMessage = "Macro script is empty.";
            return;
        }

        for (var index = 0; index < _macros.Count; index++)
        {
            if (!string.Equals(_macros[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _macros[index].Script = script;
            _macros[index].ContinueOnError = ContinueOnCommandError;
            SelectedMacro = _macros[index];
            PersistMacros();
            CommandStatusMessage = $"Updated macro '{name}'.";
            return;
        }

        var macro = new CadScriptMacroEntryViewModel(name, script, ContinueOnCommandError);
        _macros.Add(macro);
        SelectedMacro = macro;
        PersistMacros();
        CommandStatusMessage = $"Saved macro '{name}'.";
    }

    private void DeleteSelectedMacro()
    {
        var selected = SelectedMacro;
        if (selected is null)
        {
            return;
        }

        var removed = _macros.Remove(selected);
        if (!removed)
        {
            return;
        }

        SelectedMacro = _macros.Count == 0 ? null : _macros[0];
        PersistMacros();
        CommandStatusMessage = $"Deleted macro '{selected.Name}'.";
    }

    private void ApplySelectedMacro()
    {
        var selected = SelectedMacro;
        if (selected is null)
        {
            return;
        }

        SetCommandScriptText(selected.Script);
        ContinueOnCommandError = selected.ContinueOnError;
        MacroName = selected.Name;
        CommandStatusMessage = $"Loaded macro '{selected.Name}'.";
    }

    private void LoadMacrosFromWorkspace()
    {
        _macros.Clear();
        var json = _workspace.MacroCatalogJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var definitions = JsonSerializer.Deserialize<List<CadScriptMacroDefinition>>(json);
            if (definitions is null)
            {
                return;
            }

            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                if (string.IsNullOrWhiteSpace(definition.Name) ||
                    string.IsNullOrWhiteSpace(definition.Script))
                {
                    continue;
                }

                _macros.Add(new CadScriptMacroEntryViewModel(
                    definition.Name.Trim(),
                    definition.Script,
                    definition.ContinueOnError));
            }
        }
        catch (JsonException)
        {
            _workspace.MacroCatalogJson = string.Empty;
        }
    }

    private void PersistMacros()
    {
        var definitions = new List<CadScriptMacroDefinition>(_macros.Count);
        for (var index = 0; index < _macros.Count; index++)
        {
            var macro = _macros[index];
            definitions.Add(new CadScriptMacroDefinition(
                macro.Name,
                macro.Script,
                macro.ContinueOnError));
        }

        _workspace.MacroCatalogJson = JsonSerializer.Serialize(definitions);
    }

    private bool TryCreatePlaybackOptions(
        out CadScriptCommandPlaybackOptions options,
        out string error)
    {
        options = CadScriptCommandPlaybackOptions.Default;
        error = string.Empty;

        if (!TryParsePositiveInteger(CommandStartLine, defaultValue: 1, out var startLine))
        {
            error = "Start line must be an integer greater than or equal to 1.";
            return false;
        }

        int? maxCommands = null;
        if (!string.IsNullOrWhiteSpace(CommandMaxCommands))
        {
            if (!TryParsePositiveInteger(CommandMaxCommands, defaultValue: 1, out var parsedMax))
            {
                error = "Max commands must be empty or an integer greater than or equal to 1.";
                return false;
            }

            maxCommands = parsedMax;
        }

        options = new CadScriptCommandPlaybackOptions(
            StopOnError: !ContinueOnCommandError,
            StartLine: startLine,
            MaxCommands: maxCommands);
        return true;
    }

    private static bool TryParsePositiveInteger(string text, int defaultValue, out int value)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            value = defaultValue;
            return true;
        }

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return value >= 1;
    }

    private async Task SaveRecordingAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(RecordingSavePath))
        {
            CommandStatusMessage = "Specify a path to save script recording.";
            return;
        }

        try
        {
            var result = await _recordingService
                .SaveAsync(RecordingSavePath, includeHeader: true, cancellationToken)
                .ConfigureAwait(true);
            CommandStatusMessage = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Saved recording: {result.EntryCount} command(s), {result.LineCount} line(s).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            CommandStatusMessage = $"Unable to save recording: {ex.Message}";
        }
    }

    private void StartRecording()
    {
        _recordingService.Start();
    }

    private void PauseRecording()
    {
        _recordingService.Pause();
    }

    private void ResumeRecording()
    {
        _recordingService.Resume();
    }

    private void StopRecording()
    {
        _recordingService.Stop();
    }

    private void ClearRecording()
    {
        _recordingService.Clear();
    }

    private void LoadRecordingIntoCommandScript()
    {
        var text = _recordingService.BuildScript(includeHeader: true);
        SetCommandScriptText(text);
        CommandStatusMessage = "Loaded recording into command script editor.";
    }

    private void OnRecordingSnapshotChanged(object? sender, CadScriptRecordingSnapshot snapshot)
    {
        ApplyRecordingSnapshot(snapshot);
    }

    private void ApplyRecordingSnapshot(CadScriptRecordingSnapshot snapshot)
    {
        IsRecording = snapshot.IsRecording;
        IsRecordingPaused = snapshot.IsPaused;
        RecordedCommandCount = snapshot.EntryCount;
        RecordingStatusMessage = snapshot.StatusMessage;
    }

    private static bool HasOperations(CadScriptCommandPlaybackResult result)
    {
        for (var index = 0; index < result.Entries.Count; index++)
        {
            if (result.Entries[index].Result.Operations is { Count: > 0 })
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _recordingService.SnapshotChanged -= OnRecordingSnapshotChanged;
        _savePathSubscription.Dispose();
        _includeFailedSubscription.Dispose();
        _includeMetadataSubscription.Dispose();
        _includeTimestampSubscription.Dispose();
        _commandStartLineSubscription.Dispose();
        _commandMaxCommandsSubscription.Dispose();
        _macroNameSubscription.Dispose();
        _execution?.Dispose();
        _commandExecution?.Dispose();
    }
}

public sealed partial class CadScriptMacroEntryViewModel : ViewModelBase
{
    public CadScriptMacroEntryViewModel(string name, string script, bool continueOnError)
    {
        Name = name;
        Script = script;
        ContinueOnError = continueOnError;
    }

    [Reactive]
    public partial string Name { get; set; }

    [Reactive]
    public partial string Script { get; set; }

    [Reactive]
    public partial bool ContinueOnError { get; set; }
}

public sealed record CadScriptMacroDefinition(
    string Name,
    string Script,
    bool ContinueOnError);
