using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using ProCad.Collaboration.UI;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class CadCollaborationToolViewModel : CadToolViewModelBase, IDisposable
{
    private static readonly CadCollabTransportMode[] TransportModeValues = Enum.GetValues<CadCollabTransportMode>();
    private static readonly CadCollabAuthMode[] AuthModeValues = Enum.GetValues<CadCollabAuthMode>();

    private readonly ICadCollabUiService _uiService;
    private readonly ICadCollabControlService _controlService;
    private readonly ObservableCollection<CadCollabParticipantRowViewModel> _participants = new();
    private readonly ObservableCollection<CadCollabConflictRowViewModel> _conflicts = new();
    private CadCollabTransportMode _selectedTransportMode;
    private CadCollabAuthMode _selectedAuthMode;
    private string _webSocketUrl = string.Empty;
    private string _sharedFilePath = string.Empty;
    private string _bearerToken = string.Empty;
    private bool _disposed;

    public IReadOnlyList<CadCollabParticipantRowViewModel> Participants => _participants;
    public IReadOnlyList<CadCollabConflictRowViewModel> Conflicts => _conflicts;
    public IReadOnlyList<CadCollabTransportMode> TransportModes => TransportModeValues;
    public IReadOnlyList<CadCollabAuthMode> AuthModes => AuthModeValues;

    [Reactive]
    public partial bool IsConnected { get; set; }

    [Reactive]
    public partial string ConnectionStatus { get; set; } = "Disconnected";

    [Reactive]
    public partial string AuthMode { get; set; } = "Anonymous";

    [Reactive]
    public partial string TransportMode { get; set; } = "None";

    [Reactive]
    public partial double SyncLagMs { get; set; }

    [Reactive]
    public partial int QueueDepth { get; set; }

    [Reactive]
    public partial string SnapshotAge { get; set; } = "0s";

    [Reactive]
    public partial bool ResyncRequired { get; set; }

    [Reactive]
    public partial bool CanReconnect { get; set; } = true;

    [Reactive]
    public partial bool CanJoin { get; set; } = true;

    [Reactive]
    public partial bool CanLeave { get; set; }

    [Reactive]
    public partial string ShareStatus { get; set; } = string.Empty;

    public CadCollabTransportMode SelectedTransportMode
    {
        get => _selectedTransportMode;
        set
        {
            if (_selectedTransportMode != value)
            {
                this.RaiseAndSetIfChanged(ref _selectedTransportMode, value);
                this.RaisePropertyChanged(nameof(ShowWebSocketUrlEditor));
                this.RaisePropertyChanged(nameof(ShowSharedFilePathEditor));
            }
        }
    }

    public CadCollabAuthMode SelectedAuthMode
    {
        get => _selectedAuthMode;
        set
        {
            if (_selectedAuthMode != value)
            {
                this.RaiseAndSetIfChanged(ref _selectedAuthMode, value);
                this.RaisePropertyChanged(nameof(ShowBearerTokenEditor));
            }
        }
    }

    public string WebSocketUrl
    {
        get => _webSocketUrl;
        set => this.RaiseAndSetIfChanged(ref _webSocketUrl, value);
    }

    public string SharedFilePath
    {
        get => _sharedFilePath;
        set => this.RaiseAndSetIfChanged(ref _sharedFilePath, value);
    }

    public string BearerToken
    {
        get => _bearerToken;
        set => this.RaiseAndSetIfChanged(ref _bearerToken, value);
    }

    public bool ShowWebSocketUrlEditor => SelectedTransportMode == CadCollabTransportMode.WebSocket;
    public bool ShowSharedFilePathEditor => SelectedTransportMode == CadCollabTransportMode.SharedFile;
    public bool ShowBearerTokenEditor => SelectedAuthMode == CadCollabAuthMode.BearerToken;

    public ReactiveCommand<Unit, Unit> JoinCommand { get; }
    public ReactiveCommand<Unit, Unit> LeaveCommand { get; }
    public ReactiveCommand<Unit, Unit> ShareCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplySettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ReconnectCommand { get; }
    public ReactiveCommand<Unit, Unit> ResyncCommand { get; }
    public ReactiveCommand<string, Unit> ReapplyConflictCommand { get; }

    public CadCollaborationToolViewModel(
        ICadCollabUiService uiService,
        ICadCollabControlService? controlService = null)
    {
        _uiService = uiService;
        _controlService = controlService ?? new NullCadCollabControlService();
        JoinCommand = ReactiveCommand.CreateFromTask(JoinAsync);
        LeaveCommand = ReactiveCommand.CreateFromTask(LeaveAsync);
        ShareCommand = ReactiveCommand.CreateFromTask(ShareAsync);
        ApplySettingsCommand = ReactiveCommand.CreateFromTask(ApplySettingsAsync);
        ReconnectCommand = ReactiveCommand.CreateFromTask(ReconnectAsync);
        ResyncCommand = ReactiveCommand.CreateFromTask(ResyncAsync);
        ReapplyConflictCommand = ReactiveCommand.CreateFromTask<string>(ReapplyConflictAsync);
        LoadOptions(_controlService.CurrentOptions);
        _uiService.StateChanged += OnStateChanged;
        ApplyState(_uiService.Current);
    }

    private void OnStateChanged(object? sender, CadCollabUiState state)
    {
        ApplyState(state);
    }

    private void ApplyState(CadCollabUiState state)
    {
        IsConnected = state.IsConnected;
        ConnectionStatus = state.ConnectionStatus;
        AuthMode = state.AuthMode;
        TransportMode = state.TransportMode;
        CanReconnect = state.CanReconnect;
        CanJoin = !state.IsConnected;
        CanLeave = state.IsConnected;
        SyncLagMs = state.Diagnostics.SyncLagMs;
        QueueDepth = state.Diagnostics.QueueDepth;
        SnapshotAge = $"{Math.Max(0, (int)state.Diagnostics.SnapshotAge.TotalSeconds)}s";
        ResyncRequired = state.Diagnostics.ResyncRequired;

        _participants.Clear();
        foreach (var participant in state.Participants.OrderByDescending(static item => item.LastActiveUtc))
        {
            _participants.Add(new CadCollabParticipantRowViewModel(
                participant.DisplayName,
                participant.Color,
                participant.IsLocal,
                participant.ActiveTool,
                participant.ActiveCommand,
                participant.PromptStage,
                participant.LastActiveUtc));
        }

        _conflicts.Clear();
        foreach (var conflict in state.Conflicts.OrderByDescending(static item => item.TimestampUtc))
        {
            _conflicts.Add(new CadCollabConflictRowViewModel(
                conflict.ConflictId,
                conflict.EntityKey,
                conflict.Summary,
                conflict.ResolutionPolicy,
                conflict.TimestampUtc,
                conflict.CanReapply));
        }

        this.RaisePropertyChanged(nameof(Participants));
        this.RaisePropertyChanged(nameof(Conflicts));
    }

    private void LoadOptions(CadCollabConnectionOptions options)
    {
        SelectedTransportMode = options.TransportMode;
        SelectedAuthMode = options.AuthMode;
        WebSocketUrl = options.WebSocketUrl ?? string.Empty;
        SharedFilePath = options.SharedFilePath ?? string.Empty;
        BearerToken = options.BearerToken ?? string.Empty;
    }

    private CadCollabConnectionOptions BuildOptions()
    {
        return new CadCollabConnectionOptions(
            TransportMode: SelectedTransportMode,
            AuthMode: SelectedAuthMode,
            WebSocketUrl: string.IsNullOrWhiteSpace(WebSocketUrl) ? null : WebSocketUrl.Trim(),
            SharedFilePath: string.IsNullOrWhiteSpace(SharedFilePath) ? null : SharedFilePath.Trim(),
            BearerToken: string.IsNullOrWhiteSpace(BearerToken) ? null : BearerToken.Trim());
    }

    private async System.Threading.Tasks.Task JoinAsync()
    {
        await _controlService.JoinAsync().ConfigureAwait(false);
    }

    private async System.Threading.Tasks.Task LeaveAsync()
    {
        await _controlService.LeaveAsync().ConfigureAwait(false);
    }

    private async System.Threading.Tasks.Task ShareAsync()
    {
        ShareStatus = await _controlService.ShareAsync().ConfigureAwait(false);
    }

    private async System.Threading.Tasks.Task ApplySettingsAsync()
    {
        await _controlService.ApplyConnectionOptionsAsync(BuildOptions()).ConfigureAwait(false);
    }

    private async System.Threading.Tasks.Task ReconnectAsync()
    {
        await _controlService.ReconnectAsync().ConfigureAwait(false);
    }

    private async System.Threading.Tasks.Task ResyncAsync()
    {
        await _controlService.ResyncAsync().ConfigureAwait(false);
    }

    private async System.Threading.Tasks.Task ReapplyConflictAsync(string conflictId)
    {
        if (string.IsNullOrWhiteSpace(conflictId))
        {
            return;
        }

        await _controlService.ReapplyConflictAsync(conflictId).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _uiService.StateChanged -= OnStateChanged;
    }
}

public sealed class CadCollabParticipantRowViewModel
{
    public CadCollabParticipantRowViewModel(
        string displayName,
        string color,
        bool isLocal,
        string? activeTool,
        string? activeCommand,
        string? promptStage,
        DateTimeOffset lastActiveUtc)
    {
        DisplayName = displayName;
        Color = color;
        IsLocal = isLocal;
        ActiveTool = activeTool ?? string.Empty;
        ActiveCommand = activeCommand ?? string.Empty;
        PromptStage = promptStage ?? string.Empty;
        LastActive = lastActiveUtc.ToLocalTime().ToString("HH:mm:ss");
    }

    public string DisplayName { get; }
    public string Color { get; }
    public bool IsLocal { get; }
    public string ActiveTool { get; }
    public string ActiveCommand { get; }
    public string PromptStage { get; }
    public string LastActive { get; }
}

public sealed class CadCollabConflictRowViewModel
{
    public CadCollabConflictRowViewModel(
        string conflictId,
        string entityKey,
        string summary,
        string policy,
        DateTimeOffset timestampUtc,
        bool canReapply)
    {
        ConflictId = conflictId;
        EntityKey = entityKey;
        Summary = summary;
        Policy = policy;
        CanReapply = canReapply;
        Time = timestampUtc.ToLocalTime().ToString("HH:mm:ss");
    }

    public string ConflictId { get; }
    public string EntityKey { get; }
    public string Summary { get; }
    public string Policy { get; }
    public bool CanReapply { get; }
    public string Time { get; }
}
