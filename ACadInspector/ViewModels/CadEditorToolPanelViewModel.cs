using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ACadInspector.Editing.Controllers;
using ACadInspector.Editing.Prompt;
using ACadInspector.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadEditorToolPanelViewModel : CadToolViewModelBase, IDisposable
{
    private readonly CadEditorControllerHostService _controllerHost;
    private readonly CadDocumentContextService _documentContext;
    private readonly ObservableCollection<CadCommandCompletionItemViewModel> _completions = new();
    private ICadEditorController? _activeController;
    private ICadEditorContextSnapshotProvider? _snapshotProvider;
    private ICadCommandRuntime? _commandRuntime;
    private readonly IDisposable _activeDocumentSubscription;
    private bool _disposed;

    public IReadOnlyList<CadEditorToolActionViewModel> DrawTools { get; }
    public IReadOnlyList<CadEditorToolActionViewModel> ModifyTools { get; }
    public IReadOnlyList<CadEditorToolActionViewModel> AnnotateTools { get; }
    public IReadOnlyList<CadCommandCompletionItemViewModel> Completions => _completions;

    [Reactive]
    public partial string ActiveCommand { get; set; } = "Selection";

    [Reactive]
    public partial bool IsCommandActive { get; set; }

    [Reactive]
    public partial string ParameterHelp { get; set; } = string.Empty;

    [Reactive]
    public partial string StatusMessage { get; set; } = string.Empty;

    [Reactive]
    public partial bool CanStartTools { get; set; }

    public bool HasCompletions => _completions.Count > 0;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public ReactiveCommand<string, Unit> StartToolCommand { get; }
    public ReactiveCommand<CadCommandCompletionItemViewModel, Unit> ApplyCompletionCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public CadEditorToolPanelViewModel(
        CadEditorControllerHostService controllerHost,
        CadDocumentContextService documentContext)
    {
        _controllerHost = controllerHost ?? throw new ArgumentNullException(nameof(controllerHost));
        _documentContext = documentContext ?? throw new ArgumentNullException(nameof(documentContext));
        StartToolCommand = ReactiveCommand.Create<string>(
            StartCommand,
            this.WhenAnyValue(x => x.CanStartTools));
        ApplyCompletionCommand = ReactiveCommand.CreateFromTask<CadCommandCompletionItemViewModel>(ApplyCompletionAsync);
        CancelCommand = ReactiveCommand.Create(CancelActiveCommand);
        DrawTools =
        [
            CreateTool("LINE", "Line", StartToolCommand),
            CreateTool("PLINE", "Polyline", StartToolCommand),
            CreateTool("XLINE", "XLine", StartToolCommand),
            CreateTool("RAY", "Ray", StartToolCommand),
            CreateTool("CIRCLE", "Circle", StartToolCommand),
            CreateTool("ARC", "Arc", StartToolCommand),
            CreateTool("ELLIPSE", "Ellipse", StartToolCommand),
            CreateTool("SPLINE", "Spline", StartToolCommand),
            CreateTool("POLYGON", "Polygon", StartToolCommand),
            CreateTool("RECTANG", "Rectangle", StartToolCommand),
            CreateTool("POINT", "Point", StartToolCommand),
            CreateTool("INSERT", "Insert", StartToolCommand),
            CreateTool("HATCH", "Hatch", StartToolCommand),
            CreateTool("BOUNDARY", "Boundary", StartToolCommand)
        ];
        ModifyTools =
        [
            CreateTool("MOVE", "Move", StartToolCommand),
            CreateTool("COPY", "Copy", StartToolCommand),
            CreateTool("ROTATE", "Rotate", StartToolCommand),
            CreateTool("SCALE", "Scale", StartToolCommand),
            CreateTool("MIRROR", "Mirror", StartToolCommand),
            CreateTool("STRETCH", "Stretch", StartToolCommand),
            CreateTool("ERASE", "Erase", StartToolCommand),
            CreateTool("OFFSET", "Offset", StartToolCommand),
            CreateTool("TRIM", "Trim", StartToolCommand),
            CreateTool("EXTEND", "Extend", StartToolCommand),
            CreateTool("BREAK", "Break", StartToolCommand),
            CreateTool("JOIN", "Join", StartToolCommand),
            CreateTool("FILLET", "Fillet", StartToolCommand),
            CreateTool("CHAMFER", "Chamfer", StartToolCommand),
            CreateTool("ARRAY", "Array", StartToolCommand),
            CreateTool("EXPLODE", "Explode", StartToolCommand),
            CreateTool("ALIGN", "Align", StartToolCommand),
            CreateTool("MATCHPROP", "MatchProp", StartToolCommand),
            CreateTool("COPYCLIP", "CopyClip", StartToolCommand),
            CreateTool("CUT", "Cut", StartToolCommand),
            CreateTool("PASTECLIP", "PasteClip", StartToolCommand)
        ];
        AnnotateTools =
        [
            CreateTool("TEXT", "Text", StartToolCommand),
            CreateTool("MTEXT", "MText", StartToolCommand),
            CreateTool("DIMLINEAR", "Dim Linear", StartToolCommand),
            CreateTool("DIMALIGNED", "Dim Aligned", StartToolCommand),
            CreateTool("DIMRADIUS", "Dim Radius", StartToolCommand),
            CreateTool("DIMDIAMETER", "Dim Diameter", StartToolCommand),
            CreateTool("DIMANGULAR", "Dim Angular", StartToolCommand),
            CreateTool("LEADER", "Leader", StartToolCommand),
            CreateTool("MLEADER", "MLeader", StartToolCommand)
        ];

        _activeDocumentSubscription = _documentContext.WhenAnyValue(x => x.ActiveDocument)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => BindActiveController());

        BindActiveController();
    }

    private void StartCommand(string? commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName) || !CanStartTools)
        {
            return;
        }

        _activeController?.BeginCommand(commandName);
        if (_commandRuntime is not null)
        {
            ApplyPromptState(_commandRuntime.State);
        }
    }

    private void OnSnapshotChanged(object? sender, CadEditorContextSnapshot snapshot)
    {
        IsCommandActive = snapshot.IsCommandActive;
        ActiveCommand = snapshot.IsCommandActive
            ? ResolveActiveCommandDisplay(snapshot.Prompt, snapshot.ActiveCommand)
            : "Selection";
        CanStartTools = snapshot.CanStartCommands;
        ParameterHelp = snapshot.IsCommandActive ? snapshot.ParameterHelp ?? string.Empty : string.Empty;
        if (!string.IsNullOrWhiteSpace(snapshot.LastMessage))
        {
            SetStatusMessage(snapshot.LastMessage);
        }
    }

    private void OnRuntimeStateChanged(object? sender, CadPromptState state)
    {
        ApplyPromptState(state);
    }

    private void ApplyPromptState(CadPromptState state)
    {
        IsCommandActive = state.IsActive;
        ActiveCommand = state.IsActive
            ? ResolveActiveCommandDisplay(state.Prompt, state.ActiveCommand)
            : "Selection";
        ParameterHelp = state.IsActive ? state.ParameterHelp ?? string.Empty : string.Empty;
        if (!string.IsNullOrWhiteSpace(state.LastMessage))
        {
            SetStatusMessage(state.LastMessage);
        }

        _completions.Clear();
        if (state.IsActive)
        {
            foreach (var completion in state.Completions)
            {
                if (IsCommandCompletionKind(completion.Kind))
                {
                    continue;
                }

                _completions.Add(new CadCommandCompletionItemViewModel(completion, ApplyCompletionCommand));
            }
        }

        this.RaisePropertyChanged(nameof(HasCompletions));
        this.RaisePropertyChanged(nameof(Completions));
    }

    private async Task ApplyCompletionAsync(CadCommandCompletionItemViewModel? completion)
    {
        if (completion is null || _activeController is null)
        {
            return;
        }

        if (IsCommandCompletionKind(completion.Kind))
        {
            _activeController.BeginCommand(completion.Value);
            if (_commandRuntime is not null)
            {
                ApplyPromptState(_commandRuntime.State);
            }

            return;
        }

        if (!_activeController.CommandRuntime.State.IsActive)
        {
            _activeController.BeginCommand(completion.Value);
            if (_commandRuntime is not null)
            {
                ApplyPromptState(_commandRuntime.State);
            }

            return;
        }

        var tokenType = string.Equals(completion.Kind, "Keyword", StringComparison.OrdinalIgnoreCase)
            ? CadPromptTokenType.Keyword
            : CadPromptTokenType.Text;
        var resolution = await _activeController
            .SubmitTokenAsync(new CadPromptToken(tokenType, completion.Value), commit: false)
            .ConfigureAwait(true);
        ApplyPromptState(resolution.State);
    }

    private void CancelActiveCommand()
    {
        if (_activeController is null)
        {
            return;
        }

        _activeController.CancelCommand();
        if (_commandRuntime is not null)
        {
            ApplyPromptState(_commandRuntime.State);
        }
    }

    private static bool IsCommandCompletionKind(string kind)
    {
        return string.Equals(kind, "Command", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(kind, "Alias", StringComparison.OrdinalIgnoreCase);
    }

    private static CadEditorToolActionViewModel CreateTool(string command, string displayName, ICommand startCommand)
    {
        return new CadEditorToolActionViewModel(
            command,
            displayName,
            ResolveToolIconResourceKey(command),
            startCommand);
    }

    private static string ResolveToolIconResourceKey(string command)
    {
        return command.ToUpperInvariant() switch
        {
            "LINE" => "LineIconPath",
            "PLINE" => "LineIconPath",
            "XLINE" => "LineIconPath",
            "RAY" => "LocationArrowIconPath",
            "CIRCLE" => "CircleIconPath",
            "ARC" => "RotateClockwiseIconPath",
            "ELLIPSE" => "OvalIconPath",
            "SPLINE" => "LineIconPath",
            "POLYGON" => "ShapeExcludeIconPath",
            "RECTANG" => "RectangleIconPath",
            "POINT" => "PointScanIconPath",
            "INSERT" => "InsertIconPath",
            "HATCH" => "PaintBrushIconPath",
            "BOUNDARY" => "ShapeExcludeIconPath",
            "MOVE" => "ArrowMoveIconPath",
            "COPY" => "CopyIconPath",
            "ROTATE" => "RotateClockwiseIconPath",
            "SCALE" => "ScaleFitIconPath",
            "MIRROR" => "FlipHorizontalIconPath",
            "STRETCH" => "ArrowExpandIconPath",
            "ERASE" => "DeleteIconPath",
            "OFFSET" => "ArrowMoveInwardIconPath",
            "TRIM" => "CutIconPath",
            "EXTEND" => "ArrowExpandIconPath",
            "BREAK" => "SplitHorizontalIconPath",
            "JOIN" => "LinkIconPath",
            "FILLET" => "CircleLineIconPath",
            "CHAMFER" => "ShapeExcludeIconPath",
            "ARRAY" => "GridIconPath",
            "EXPLODE" => "SplitHorizontalIconPath",
            "ALIGN" => "AlignStraightenIconPath",
            "MATCHPROP" => "PaintBrushIconPath",
            "COPYCLIP" => "CopyIconPath",
            "CUT" => "CutIconPath",
            "PASTECLIP" => "ClipboardPasteIconPath",
            "TEXT" => "TextTIconPath",
            "MTEXT" => "TextTIconPath",
            "DIMLINEAR" => "RulerIconPath",
            "DIMALIGNED" => "AlignStraightenIconPath",
            "DIMRADIUS" => "CircleIconPath",
            "DIMDIAMETER" => "CircleIconPath",
            "DIMANGULAR" => "RotateClockwiseIconPath",
            "LEADER" => "LocationArrowIconPath",
            "MLEADER" => "LocationArrowIconPath",
            _ => "CodeIconPath"
        };
    }

    private void BindActiveController()
    {
        if (_commandRuntime is not null)
        {
            _commandRuntime.StateChanged -= OnRuntimeStateChanged;
        }

        if (_snapshotProvider is not null)
        {
            _snapshotProvider.SnapshotChanged -= OnSnapshotChanged;
        }

        _activeController = _controllerHost.GetActiveController();
        _commandRuntime = _activeController?.CommandRuntime;
        _snapshotProvider = _activeController?.ContextSnapshots;
        CanStartTools = _snapshotProvider?.Current.CanStartCommands ?? (_commandRuntime is not null);

        if (_commandRuntime is null)
        {
            ActiveCommand = "Selection";
            IsCommandActive = false;
            ParameterHelp = "Open a drawing to start commands.";
            SetStatusMessage(string.Empty);
            _completions.Clear();
            this.RaisePropertyChanged(nameof(HasCompletions));
            this.RaisePropertyChanged(nameof(Completions));
            return;
        }

        _commandRuntime.StateChanged += OnRuntimeStateChanged;

        if (_snapshotProvider is not null)
        {
            _snapshotProvider.SnapshotChanged += OnSnapshotChanged;
            ActiveCommand = _snapshotProvider.Current.Prompt;
            ParameterHelp = _snapshotProvider.Current.ParameterHelp ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(_snapshotProvider.Current.LastMessage))
            {
                SetStatusMessage(_snapshotProvider.Current.LastMessage);
            }
            ApplyPromptState(_commandRuntime.State);
            return;
        }

        ApplyPromptState(_commandRuntime.State);
    }

    private static string ResolveActiveCommandDisplay(string prompt, string? activeCommand)
    {
        if (!string.IsNullOrWhiteSpace(activeCommand))
        {
            return activeCommand!.ToUpperInvariant();
        }

        return string.IsNullOrWhiteSpace(prompt)
            ? "Command"
            : prompt.ToUpperInvariant();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_commandRuntime is not null)
        {
            _commandRuntime.StateChanged -= OnRuntimeStateChanged;
            _commandRuntime = null;
        }

        if (_snapshotProvider is not null)
        {
            _snapshotProvider.SnapshotChanged -= OnSnapshotChanged;
            _snapshotProvider = null;
        }

        _activeDocumentSubscription.Dispose();
    }

    private void SetStatusMessage(string? message)
    {
        StatusMessage = message ?? string.Empty;
        this.RaisePropertyChanged(nameof(HasStatusMessage));
    }
}

public sealed class CadEditorToolActionViewModel
{
    public CadEditorToolActionViewModel(
        string command,
        string displayName,
        string iconResourceKey,
        ICommand startCommand)
    {
        Command = command;
        DisplayName = displayName;
        IconResourceKey = iconResourceKey;
        StartCommand = startCommand;
    }

    public string Command { get; }
    public string DisplayName { get; }
    public string IconResourceKey { get; }
    public ICommand StartCommand { get; }
}
