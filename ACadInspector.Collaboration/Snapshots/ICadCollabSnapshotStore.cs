using ACadInspector.Collaboration.Contracts;

namespace ACadInspector.Collaboration.Snapshots;

public interface ICadCollabSnapshotStore
{
    ValueTask<CadCollabSnapshot?> LoadLatestSnapshotAsync(CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<CadCollabBatch>> LoadBatchesAsync(CancellationToken cancellationToken = default);
    ValueTask AppendBatchAsync(CadCollabBatch batch, CancellationToken cancellationToken = default);
    ValueTask WriteSnapshotAsync(CadCollabSnapshot snapshot, CancellationToken cancellationToken = default);
    ValueTask CompactAsync(CancellationToken cancellationToken = default);
}
