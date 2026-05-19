using ProCad.Collaboration.Contracts;

namespace ProCad.Collaboration.Snapshots;

public sealed class InMemoryCadCollabSnapshotStore : ICadCollabSnapshotStore
{
    private const int MaxBatches = 8_192;
    private readonly List<CadCollabBatch> _batches = new();
    private CadCollabSnapshot? _snapshot;

    public ValueTask<CadCollabSnapshot?> LoadLatestSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _snapshot;
        if (snapshot is null)
        {
            return ValueTask.FromResult<CadCollabSnapshot?>(null);
        }

        return ValueTask.FromResult<CadCollabSnapshot?>(CloneSnapshot(snapshot));
    }

    public ValueTask<IReadOnlyList<CadCollabBatch>> LoadBatchesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_batches.Count == 0)
        {
            return ValueTask.FromResult<IReadOnlyList<CadCollabBatch>>(Array.Empty<CadCollabBatch>());
        }

        return ValueTask.FromResult<IReadOnlyList<CadCollabBatch>>(_batches.Select(CloneBatch).ToArray());
    }

    public ValueTask AppendBatchAsync(CadCollabBatch batch, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.BatchId != Guid.Empty && _batches.Any(existing => existing.BatchId == batch.BatchId))
        {
            return ValueTask.CompletedTask;
        }

        _batches.Add(CloneBatch(batch));
        if (_batches.Count > MaxBatches)
        {
            _batches.RemoveRange(0, _batches.Count - MaxBatches);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask WriteSnapshotAsync(CadCollabSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = CloneSnapshot(snapshot);
        _batches.Clear();
        return ValueTask.CompletedTask;
    }

    public ValueTask CompactAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_batches.Count > 1_024)
        {
            _batches.RemoveRange(0, _batches.Count - 1_024);
        }

        return ValueTask.CompletedTask;
    }

    private static CadCollabBatch CloneBatch(CadCollabBatch batch)
    {
        return batch with
        {
            Operations = batch.Operations.Count == 0
                ? Array.Empty<Editing.Operations.CadOperation>()
                : batch.Operations.ToArray()
        };
    }

    private static CadCollabSnapshot CloneSnapshot(CadCollabSnapshot snapshot)
    {
        return snapshot with
        {
            Payload = snapshot.Payload.Length == 0 ? Array.Empty<byte>() : snapshot.Payload.ToArray()
        };
    }
}
