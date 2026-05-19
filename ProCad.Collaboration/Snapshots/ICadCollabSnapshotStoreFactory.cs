namespace ProCad.Collaboration.Snapshots;

public interface ICadCollabSnapshotStoreFactory
{
    ICadCollabSnapshotStore CreateStore(string scopeKey);
}

