using ACadInspector.Collaboration.Contracts;
using ACadInspector.Collaboration.Transports;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Sessions;

namespace ACadInspector.Collaboration.Services;

public sealed record CadRealtimeConflict(
    string ConflictId,
    string EntityKey,
    string Summary,
    string ResolutionPolicy,
    DateTimeOffset TimestampUtc);

public sealed record CadRealtimeOperationsAppliedEventArgs(
    bool IsRemote,
    Guid ActorId,
    long Version,
    IReadOnlyList<CadOperation> Operations);

public interface ICadRealtimeSession : IAsyncDisposable
{
    Guid ActorId { get; }
    long Version { get; }
    event EventHandler<CadRealtimeStateChangedEventArgs>? TransportStateChanged;
    event EventHandler<CadCollabPresence>? PresenceReceived;
    event EventHandler<IReadOnlyList<CadRealtimeConflict>>? ConflictsChanged;
    event EventHandler<CadRealtimeOperationsAppliedEventArgs>? OperationsApplied;

    ValueTask ConnectAsync(CancellationToken cancellationToken = default);
    ValueTask DisconnectAsync(CancellationToken cancellationToken = default);
    ValueTask ReconnectAsync(CancellationToken cancellationToken = default);
    ValueTask ResyncAsync(CancellationToken cancellationToken = default);
    ValueTask SubmitLocalAsync(IReadOnlyList<CadOperation> operations, CancellationToken cancellationToken = default);
    ValueTask SubmitLocalAppliedAsync(IReadOnlyList<CadOperation> operations, CancellationToken cancellationToken = default);
    ValueTask PublishPresenceAsync(
        CadCollabPresence presence,
        TimeSpan? timeToLive = null,
        CancellationToken cancellationToken = default);
    ValueTask<bool> ReapplyConflictAsync(string conflictId, CancellationToken cancellationToken = default);
    IReadOnlyList<CadRealtimeConflict> GetConflicts();
}

public interface ICadCollabService
{
    ICadRealtimeSession CreateSession(ICadEditorSession session, ICadRealtimeTransport transport, Guid actorId);
}
