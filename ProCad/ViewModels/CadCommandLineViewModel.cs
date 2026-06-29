using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Input;
using ProCad.Editing.Controllers;
using ProCad.Editing.Interaction;
using ProCad.Editing.Commands;
using ProCad.Editing.Prompt;
using ProCad.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class CadCommandLineViewModel : CadToolViewModelBase, IDisposable
{
    private readonly ObservableCollection<string> _history = new();
    private readonly ObservableCollection<CadCommandCompletionItemViewModel> _completions = new();
    private readonly List<string> _commandHistory = new();
    private readonly CadEditorControllerHostService _controllerHost;
    private ICadEditorController? _activeController;
    private ICadCommandRuntime? _commandRuntime;
    private readonly CadDocumentContextService _documentContext;
    private readonly CadEditorSessionHostService _sessionHost;
    private readonly ICadInteractiveCommandAdapterRegistry _interactiveAdapters;
    private readonly CadCollaborationWorkspaceService? _collaborationWorkspace;
    private readonly IDisposable _inputSubscription;
    private readonly IDisposable _activeDocumentSubscription;
    private int _historyIndex = -1;
    private bool _disposed;

    public IReadOnlyList<string> History => _history;
    public IReadOnlyList<CadCommandCompletionItemViewModel> Completions => _completions;

    [Reactive]
    public partial string Prompt { get; set; } = "Command";

    [Reactive]
    public partial string Input { get; set; } = string.Empty;

    [Reactive]
    public partial bool IsBusy { get; set; }

    [Reactive]
    public partial string ParameterHelp { get; set; } = "Type a command. Use Tab/Shift+Tab to cycle completions.";

    [Reactive]
    public partial string StatusMessage { get; set; } = string.Empty;

    [Reactive]
    public partial int SelectedCompletionIndex { get; set; } = -1;

    public bool IsCompletionOpen => _completions.Count > 0;

    public ReactiveCommand<Unit, Unit> SubmitCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearHistoryCommand { get; }
    public ReactiveCommand<Unit, Unit> CompleteNextCommand { get; }
    public ReactiveCommand<Unit, Unit> CompletePreviousCommand { get; }
    public ReactiveCommand<Unit, Unit> AcceptCompletionCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> HistoryPreviousCommand { get; }
    public ReactiveCommand<Unit, Unit> HistoryNextCommand { get; }

    public CadCommandLineViewModel(
        CadEditorControllerHostService controllerHost,
        CadDocumentContextService documentContext,
        CadEditorSessionHostService sessionHost,
        ICadInteractiveCommandAdapterRegistry interactiveAdapters,
        CadCollaborationWorkspaceService? collaborationWorkspace = null)
    {
        _controllerHost = controllerHost ?? throw new ArgumentNullException(nameof(controllerHost));
        _documentContext = documentContext ?? throw new ArgumentNullException(nameof(documentContext));
        _sessionHost = sessionHost;
        _interactiveAdapters = interactiveAdapters;
        _collaborationWorkspace = collaborationWorkspace;

        SubmitCommand = ReactiveCommand.CreateFromTask(SubmitAsync);
        ClearHistoryCommand = ReactiveCommand.Create(ClearHistory);
        CompleteNextCommand = ReactiveCommand.Create(() => CycleCompletion(forward: true));
        CompletePreviousCommand = ReactiveCommand.Create(() => CycleCompletion(forward: false));
        AcceptCompletionCommand = ReactiveCommand.Create(AcceptCompletion);
        CancelCommand = ReactiveCommand.Create(CancelActiveCommand);
        HistoryPreviousCommand = ReactiveCommand.Create(RecallPreviousHistory);
        HistoryNextCommand = ReactiveCommand.Create(RecallNextHistory);

        _inputSubscription = this.WhenAnyValue(viewModel => viewModel.Input)
            .Subscribe(_ => RefreshPromptState());

        _activeDocumentSubscription = _documentContext.WhenAnyValue(x => x.ActiveDocument)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => BindActiveController());
        BindActiveController();
    }

    private async System.Threading.Tasks.Task SubmitAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var activeController = _activeController;
        if (activeController is null || _commandRuntime is null)
        {
            StatusMessage = "No active document.";
            _history.Add(StatusMessage);
            this.RaisePropertyChanged(nameof(History));
            return;
        }

        IsBusy = true;
        var rawInput = Input;

        try
        {
            if (!string.IsNullOrWhiteSpace(rawInput))
            {
                _history.Add($"> {rawInput}");
                _commandHistory.Add(rawInput.Trim());
                _historyIndex = _commandHistory.Count;
            }

            var session = activeController.Session;
            if (session is not null)
            {
                _sessionHost.SyncSelectionToSession(session);
            }

            var trimmedInput = rawInput?.Trim() ?? string.Empty;
            if (TryBeginInteractiveCommand(trimmedInput))
            {
                Input = string.Empty;
                StatusMessage = _commandRuntime.State.ParameterHelp ?? _commandRuntime.State.Prompt;
                _history.Add(StatusMessage);
                ApplyPromptState(_commandRuntime.State);
                return;
            }

            var resolution = await activeController
                .SubmitAsync(rawInput ?? string.Empty)
                .ConfigureAwait(true);
            var result = resolution.Result;
            if (session is not null)
            {
                _sessionHost.SyncSelectionToUi(session);
                if (result?.Operations is { Count: > 0 })
                {
                    _sessionHost.NotifySessionChanged(session);
                    if (_collaborationWorkspace is not null)
                    {
                        await _collaborationWorkspace
                            .PublishLocalOperationsAsync(session, result.Operations)
                            .ConfigureAwait(true);
                    }
                }
            }

            if (result is not null)
            {
                _history.Add(result.Message);
                StatusMessage = result.Message;
            }
            else if (!resolution.Handled)
            {
                StatusMessage = resolution.State.LastMessage ?? "Command was not handled.";
                _history.Add(StatusMessage);
            }

            Input = string.Empty;
            ApplyPromptState(resolution.State);
        }
        catch (Exception ex)
        {
            var message = $"Command failed: {ex.Message}";
            _history.Add(message);
            StatusMessage = message;
            RefreshPromptState();
        }
        finally
        {
            this.RaisePropertyChanged(nameof(History));
            IsBusy = false;
        }
    }

    private void ClearHistory()
    {
        _history.Clear();
        _commandHistory.Clear();
        _historyIndex = -1;
        StatusMessage = string.Empty;
        this.RaisePropertyChanged(nameof(History));
    }

    private void CancelActiveCommand()
    {
        if (_commandRuntime is null)
        {
            return;
        }

        _activeController?.CancelCommand();
        Input = string.Empty;
        var state = _commandRuntime.State;
        ApplyPromptState(state);
        StatusMessage = state.LastMessage ?? "*Cancel*";
        _history.Add(StatusMessage);
        this.RaisePropertyChanged(nameof(History));
    }

    private void RecallPreviousHistory()
    {
        if (_commandHistory.Count == 0)
        {
            return;
        }

        if (_historyIndex < 0)
        {
            _historyIndex = _commandHistory.Count;
        }

        _historyIndex = Math.Max(0, _historyIndex - 1);
        Input = _commandHistory[_historyIndex];
    }

    private void RecallNextHistory()
    {
        if (_commandHistory.Count == 0)
        {
            return;
        }

        if (_historyIndex < 0)
        {
            return;
        }

        _historyIndex = Math.Min(_commandHistory.Count, _historyIndex + 1);
        Input = _historyIndex >= _commandHistory.Count ? string.Empty : _commandHistory[_historyIndex];
    }

    private void CycleCompletion(bool forward)
    {
        if (_completions.Count == 0)
        {
            RefreshPromptState();
            if (_completions.Count == 0)
            {
                return;
            }
        }

        if (SelectedCompletionIndex < 0)
        {
            SelectedCompletionIndex = 0;
        }
        else if (forward)
        {
            SelectedCompletionIndex = (SelectedCompletionIndex + 1) % _completions.Count;
        }
        else
        {
            SelectedCompletionIndex = (SelectedCompletionIndex - 1 + _completions.Count) % _completions.Count;
        }

        AcceptCompletion();
    }

    private void AcceptCompletion()
    {
        if (_completions.Count == 0)
        {
            return;
        }

        var index = SelectedCompletionIndex;
        if (index < 0 || index >= _completions.Count)
        {
            index = 0;
        }

        var completion = _completions[index];
        Input = ReplaceLastToken(Input, completion.Value, completion.Kind);
        SelectedCompletionIndex = -1;
    }

    private void RefreshPromptState()
    {
        if (_commandRuntime is null)
        {
            Prompt = "Command";
            ParameterHelp = "Open a drawing to start entering commands.";
            _completions.Clear();
            SelectedCompletionIndex = -1;
            this.RaisePropertyChanged(nameof(IsCompletionOpen));
            this.RaisePropertyChanged(nameof(Completions));
            return;
        }

        var state = _commandRuntime.Preview(Input, Input?.Length ?? 0);
        ApplyPromptState(state);
    }

    private void ApplyPromptState(CadPromptState state)
    {
        Prompt = state.Prompt;
        ParameterHelp = state.ParameterHelp ?? string.Empty;

        _completions.Clear();
        foreach (var item in state.Completions)
        {
            _completions.Add(new CadCommandCompletionItemViewModel(item));
        }

        if (SelectedCompletionIndex >= _completions.Count)
        {
            SelectedCompletionIndex = _completions.Count - 1;
        }

        this.RaisePropertyChanged(nameof(IsCompletionOpen));
        this.RaisePropertyChanged(nameof(Completions));
    }

    private bool TryBeginInteractiveCommand(string input)
    {
        if (_commandRuntime is null ||
            string.IsNullOrWhiteSpace(input) ||
            _commandRuntime.State.IsActive)
        {
            return false;
        }

        if (input.IndexOfAny([' ', '\t']) >= 0)
        {
            return false;
        }

        if (!_interactiveAdapters.TryGet(input, out _))
        {
            return false;
        }

        _activeController?.BeginCommand(input);
        return true;
    }

    private static string ReplaceLastToken(string input, string replacement, string kind)
    {
        input ??= string.Empty;
        replacement ??= string.Empty;

        var trimmedEnd = input.TrimEnd();
        if (trimmedEnd.Length == 0)
        {
            return replacement + " ";
        }

        var separatorIndex = trimmedEnd.LastIndexOfAny([' ', '\t']);
        if (separatorIndex < 0)
        {
            return replacement + " ";
        }

        var prefix = trimmedEnd[..(separatorIndex + 1)];
        var suffix = kind is "Command" or "Alias" ? " " : string.Empty;
        return prefix + replacement + suffix;
    }

    private void OnRuntimeStateChanged(object? sender, CadPromptState state)
    {
        ApplyPromptState(state);
    }

    private void BindActiveController()
    {
        if (_commandRuntime is not null)
        {
            _commandRuntime.StateChanged -= OnRuntimeStateChanged;
        }

        _activeController = _controllerHost.GetActiveController();
        _commandRuntime = _activeController?.CommandRuntime;
        if (_commandRuntime is null)
        {
            Prompt = "Command";
            ParameterHelp = "Open a drawing to start entering commands.";
            StatusMessage = string.Empty;
            _completions.Clear();
            SelectedCompletionIndex = -1;
            this.RaisePropertyChanged(nameof(IsCompletionOpen));
            this.RaisePropertyChanged(nameof(Completions));
            return;
        }

        _commandRuntime.StateChanged += OnRuntimeStateChanged;
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

        _activeDocumentSubscription.Dispose();
        _inputSubscription.Dispose();
    }
}

public sealed class CadCommandCompletionItemViewModel
{
    public CadCommandCompletionItemViewModel(CadCommandCompletionItem item, ICommand? applyCommand = null)
    {
        Value = item.Value;
        DisplayText = item.DisplayText;
        Kind = item.Kind;
        Description = item.Description;
        ApplyCommand = applyCommand;
    }

    public string Value { get; }
    public string DisplayText { get; }
    public string Kind { get; }
    public string Description { get; }
    public ICommand? ApplyCommand { get; }
}
