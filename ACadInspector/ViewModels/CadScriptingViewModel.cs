using System;
using System.Collections.Generic;
using System.Reactive;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Services;
using ACadInspector.Scripting;
using AvaloniaEdit.Document;
using Dock.Model.ReactiveUI.Controls;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadScriptingViewModel : Tool
{
    private readonly ICadScriptHost _scriptHost;
    private readonly CadDocumentContextService _documentContext;
    private readonly CadSelectionService _selectionService;
    private readonly CadScriptWorkspaceService _workspace;
    private CancellationTokenSource? _execution;

    public TextDocument ScriptDocument { get; } = new();

    public TextDocument OutputDocument { get; } = new();

    [Reactive]
    public partial string StatusMessage { get; set; } = "Ready.";

    [Reactive]
    public partial bool IsRunning { get; set; }

    public ReactiveCommand<Unit, Unit> RunCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearOutputCommand { get; }

    public ReactiveCommand<Unit, Unit> ResetTemplateCommand { get; }

    public CadScriptingViewModel(
        ICadScriptHost scriptHost,
        CadDocumentContextService documentContext,
        CadSelectionService selectionService,
        CadScriptWorkspaceService workspace)
    {
        _scriptHost = scriptHost;
        _documentContext = documentContext;
        _selectionService = selectionService;
        _workspace = workspace;

        InitializeScriptText();

        var canRun = this.WhenAnyValue(x => x.IsRunning, running => !running);
        var canCancel = this.WhenAnyValue(x => x.IsRunning);

        RunCommand = ReactiveCommand.CreateFromTask(RunAsync, canRun);
        CancelCommand = ReactiveCommand.Create(CancelExecution, canCancel);
        ClearOutputCommand = ReactiveCommand.Create(ClearOutput, canRun);
        ResetTemplateCommand = ReactiveCommand.Create(LoadTemplate, canRun);
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

        var globals = CreateGlobals(token);
        var result = await _scriptHost.ExecuteAsync(ScriptDocument.Text, globals, token).ConfigureAwait(true);

        AppendOutput(result);
        StatusMessage = result.Success
            ? $"Completed in {result.Duration.TotalMilliseconds:F0} ms."
            : "Script failed.";

        IsRunning = false;
        _execution.Dispose();
        _execution = null;
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
}
