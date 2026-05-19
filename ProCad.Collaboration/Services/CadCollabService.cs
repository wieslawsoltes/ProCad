using ProCad.Collaboration.History;
using ProCad.Collaboration.Sessions;
using ProCad.Collaboration.Snapshots;
using ProCad.Collaboration.Transports;
using ProCad.Editing.Sessions;

namespace ProCad.Collaboration.Services;

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
