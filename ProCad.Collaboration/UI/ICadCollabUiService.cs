namespace ProCad.Collaboration.UI;

public sealed record CadCollabParticipantUi(
    Guid UserId,
    string DisplayName,
    string Color,
    bool IsLocal,
    string? ActiveTool,
    string? ActiveCommand,
    DateTimeOffset LastActiveUtc,
    string? PromptStage = null);

public sealed record CadCollabDiagnosticsUi(
    double SyncLagMs,
    int QueueDepth,
    TimeSpan SnapshotAge,
    bool ResyncRequired);

public sealed record CadCollabConflictUi(
    string ConflictId,
    string EntityKey,
    string Summary,
    string ResolutionPolicy,
    DateTimeOffset TimestampUtc,
    bool CanReapply);

public sealed record CadCollabUiState(
    bool IsConnected,
    string ConnectionStatus,
    string AuthMode,
    string TransportMode,
    bool CanReconnect,
    IReadOnlyList<CadCollabParticipantUi> Participants,
    CadCollabDiagnosticsUi Diagnostics,
    IReadOnlyList<CadCollabConflictUi> Conflicts)
{
    public static readonly CadCollabUiState Empty = new(
        IsConnected: false,
        ConnectionStatus: "Disconnected",
        AuthMode: "Anonymous",
        TransportMode: "None",
        CanReconnect: true,
        Participants: Array.Empty<CadCollabParticipantUi>(),
        Diagnostics: new CadCollabDiagnosticsUi(
            SyncLagMs: 0,
            QueueDepth: 0,
            SnapshotAge: TimeSpan.Zero,
            ResyncRequired: false),
        Conflicts: Array.Empty<CadCollabConflictUi>());
}

public interface ICadCollabUiService
{
    CadCollabUiState Current { get; }
    event EventHandler<CadCollabUiState>? StateChanged;

    void UpdateConnection(bool isConnected, string status, string authMode, string transportMode, bool canReconnect = true);
    void UpdateParticipants(IReadOnlyList<CadCollabParticipantUi> participants);
    void UpdateDiagnostics(CadCollabDiagnosticsUi diagnostics);
    void UpdateConflicts(IReadOnlyList<CadCollabConflictUi> conflicts);
}

public sealed class CadCollabUiService : ICadCollabUiService
{
    private readonly object _sync = new();
    private CadCollabUiState _state = CadCollabUiState.Empty;

    public CadCollabUiState Current
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public event EventHandler<CadCollabUiState>? StateChanged;

    public void UpdateConnection(bool isConnected, string status, string authMode, string transportMode, bool canReconnect = true)
    {
        lock (_sync)
        {
            _state = _state with
            {
                IsConnected = isConnected,
                ConnectionStatus = status,
                AuthMode = authMode,
                TransportMode = transportMode,
                CanReconnect = canReconnect
            };
        }

        StateChanged?.Invoke(this, Current);
    }

    public void UpdateParticipants(IReadOnlyList<CadCollabParticipantUi> participants)
    {
        lock (_sync)
        {
            _state = _state with
            {
                Participants = participants ?? Array.Empty<CadCollabParticipantUi>()
            };
        }

        StateChanged?.Invoke(this, Current);
    }

    public void UpdateDiagnostics(CadCollabDiagnosticsUi diagnostics)
    {
        lock (_sync)
        {
            _state = _state with
            {
                Diagnostics = diagnostics
            };
        }

        StateChanged?.Invoke(this, Current);
    }

    public void UpdateConflicts(IReadOnlyList<CadCollabConflictUi> conflicts)
    {
        lock (_sync)
        {
            _state = _state with
            {
                Conflicts = conflicts ?? Array.Empty<CadCollabConflictUi>()
            };
        }

        StateChanged?.Invoke(this, Current);
    }
}
