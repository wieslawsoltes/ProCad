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
    public partial string ActiveCommand { get; set; } = "Command";

    [Reactive]
    public partial string ParameterHelp { get; set; } = "Use the tool panel or command line to start drawing commands.";

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
        StartToolCommand = ReactiveCommand.Create<string>(StartCommand);
        ApplyCompletionCommand = ReactiveCommand.CreateFromTask<CadCommandCompletionItemViewModel>(ApplyCompletionAsync);
        CancelCommand = ReactiveCommand.Create(CancelActiveCommand);
        DrawTools =
        [
            new("LINE", "Line", StartToolCommand),
            new("PLINE", "Polyline", StartToolCommand),
            new("XLINE", "XLine", StartToolCommand),
            new("RAY", "Ray", StartToolCommand),
            new("CIRCLE", "Circle", StartToolCommand),
            new("ARC", "Arc", StartToolCommand),
            new("ELLIPSE", "Ellipse", StartToolCommand),
            new("SPLINE", "Spline", StartToolCommand),
            new("POLYGON", "Polygon", StartToolCommand),
            new("RECTANG", "Rectangle", StartToolCommand),
            new("POINT", "Point", StartToolCommand),
            new("INSERT", "Insert", StartToolCommand),
            new("HATCH", "Hatch", StartToolCommand),
            new("BOUNDARY", "Boundary", StartToolCommand)
        ];
        ModifyTools =
        [
            new("MOVE", "Move", StartToolCommand),
            new("COPY", "Copy", StartToolCommand),
            new("ROTATE", "Rotate", StartToolCommand),
            new("SCALE", "Scale", StartToolCommand),
            new("MIRROR", "Mirror", StartToolCommand),
            new("STRETCH", "Stretch", StartToolCommand),
            new("ERASE", "Erase", StartToolCommand),
            new("OFFSET", "Offset", StartToolCommand),
            new("TRIM", "Trim", StartToolCommand),
            new("EXTEND", "Extend", StartToolCommand),
            new("BREAK", "Break", StartToolCommand),
            new("JOIN", "Join", StartToolCommand),
            new("FILLET", "Fillet", StartToolCommand),
            new("CHAMFER", "Chamfer", StartToolCommand),
            new("ARRAY", "Array", StartToolCommand),
            new("EXPLODE", "Explode", StartToolCommand),
            new("ALIGN", "Align", StartToolCommand),
            new("MATCHPROP", "MatchProp", StartToolCommand),
            new("COPYCLIP", "CopyClip", StartToolCommand),
            new("CUT", "Cut", StartToolCommand),
            new("PASTECLIP", "PasteClip", StartToolCommand)
        ];
        AnnotateTools =
        [
            new("TEXT", "Text", StartToolCommand),
            new("MTEXT", "MText", StartToolCommand),
            new("DIMLINEAR", "Dim Linear", StartToolCommand),
            new("DIMALIGNED", "Dim Aligned", StartToolCommand),
            new("DIMRADIUS", "Dim Radius", StartToolCommand),
            new("DIMDIAMETER", "Dim Diameter", StartToolCommand),
            new("DIMANGULAR", "Dim Angular", StartToolCommand),
            new("LEADER", "Leader", StartToolCommand),
            new("MLEADER", "MLeader", StartToolCommand)
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
        ActiveCommand = snapshot.Prompt;
        CanStartTools = snapshot.CanStartCommands;
        ParameterHelp = snapshot.ParameterHelp ?? string.Empty;
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
        ActiveCommand = state.Prompt;
        ParameterHelp = state.ParameterHelp ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(state.LastMessage))
        {
            SetStatusMessage(state.LastMessage);
        }

        _completions.Clear();
        foreach (var completion in state.Completions)
        {
            _completions.Add(new CadCommandCompletionItemViewModel(completion));
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
            ActiveCommand = "Command";
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
    public CadEditorToolActionViewModel(string command, string displayName, ICommand startCommand)
    {
        Command = command;
        DisplayName = displayName;
        StartCommand = startCommand;
    }

    public string Command { get; }
    public string DisplayName { get; }
    public ICommand StartCommand { get; }
}
