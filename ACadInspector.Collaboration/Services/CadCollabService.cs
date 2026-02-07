using ACadInspector.Collaboration.History;
using ACadInspector.Collaboration.Sessions;
using ACadInspector.Collaboration.Snapshots;
using ACadInspector.Collaboration.Transports;
using ACadInspector.Editing.Sessions;

namespace ACadInspector.Collaboration.Services;

public sealed class CadCollabService : ICadCollabService
{
    private readonly ICadCollabSnapshotStoreFactory _snapshotStoreFactory;

    public CadCollabService(ICadCollabSnapshotStoreFactory snapshotStoreFactory)
    {
        _snapshotStoreFactory = snapshotStoreFactory ?? throw new ArgumentNullException(nameof(snapshotStoreFactory));
    }

    public ICadRealtimeSession CreateSession(ICadEditorSession session, ICadRealtimeTransport transport, Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(transport);

        var coordinator = new CadCollabSessionCoordinator(session, new CadCollabOpHistory());
        var scopeKey = session.SessionId.Value.ToString("N");
        var snapshotStore = _snapshotStoreFactory.CreateStore(scopeKey);
        return new CadRealtimeSession(actorId, coordinator, transport, snapshotStore);
    }
}
