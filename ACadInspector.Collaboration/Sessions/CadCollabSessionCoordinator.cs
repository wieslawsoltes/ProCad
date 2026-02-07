using ACadInspector.Collaboration.Contracts;
using ACadInspector.Collaboration.History;
using ACadInspector.Collaboration.Services;
using ACadInspector.Collaboration.Transforms;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Sessions;

namespace ACadInspector.Collaboration.Sessions;

public sealed class CadCollabSessionCoordinator
{
    private readonly ICadEditorSession _session;
    private readonly CadCollabOpHistory _history;
    private readonly Dictionary<Guid, PendingConflict> _conflicts = new();
    private readonly object _sync = new();

    public CadCollabSessionCoordinator(ICadEditorSession session, CadCollabOpHistory history)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    public long Version => _history.Version;
    public event EventHandler<IReadOnlyList<CadRealtimeConflict>>? ConflictsChanged;

    public CadCollabBatch SubmitLocalBatch(
        Guid actorId,
        IReadOnlyList<CadOperation> operations,
        long lamport,
        bool applyToSession = true)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var batch = new CadCollabBatch(
            Guid.NewGuid(),
            actorId,
            _history.Version,
            _history.Version + 1,
            lamport,
            DateTimeOffset.UtcNow,
            operations);

        _history.AppendLocal(batch);

        if (applyToSession)
        {
            _session.Apply(new CadOperationBatch(
                batch.BatchId,
                batch.ActorId,
                batch.BaseVersion,
                batch.Sequence,
                batch.TimestampUtc,
                batch.Operations));
        }

        return batch;
    }

    public CadCollabBatch SubmitLocalAppliedBatch(Guid actorId, IReadOnlyList<CadOperation> operations, long lamport)
    {
        return SubmitLocalBatch(actorId, operations, lamport, applyToSession: false);
    }

    public CadCollabTransformResult ApplyRemoteBatch(CadCollabBatch batch)
    {
        var transformed = _history.TransformRemote(batch);
        if (transformed.RequiresResync)
        {
            RegisterConflict(batch, transformed);
            return transformed;
        }

        if (transformed.Operations.Count > 0)
        {
            _session.Apply(new CadOperationBatch(
                batch.BatchId,
                batch.ActorId,
                batch.BaseVersion,
                batch.Sequence,
                batch.TimestampUtc,
                transformed.Operations));
        }

        return transformed;
    }

    public IReadOnlyList<CadRealtimeConflict> GetConflicts()
    {
        lock (_sync)
        {
            return _conflicts.Values
                .Select(static entry => entry.Conflict)
                .OrderByDescending(static conflict => conflict.TimestampUtc)
                .ToArray();
        }
    }

    public bool TryReapplyConflict(Guid conflictId, out CadCollabBatch reappliedBatch)
    {
        reappliedBatch = default!;
        PendingConflict? pending = null;
        lock (_sync)
        {
            if (!_conflicts.TryGetValue(conflictId, out pending))
            {
                return false;
            }
        }

        if (pending is null)
        {
            return false;
        }

        reappliedBatch = pending.Batch with
        {
            BatchId = Guid.NewGuid(),
            BaseVersion = _history.Version,
            Sequence = _history.Version + 1,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        _history.AppendLocal(reappliedBatch);
        _session.Apply(new CadOperationBatch(
            reappliedBatch.BatchId,
            reappliedBatch.ActorId,
            reappliedBatch.BaseVersion,
            reappliedBatch.Sequence,
            reappliedBatch.TimestampUtc,
            reappliedBatch.Operations));

        RemoveConflict(conflictId);
        return true;
    }

    public bool TryReapplyConflict(Guid conflictId)
    {
        return TryReapplyConflict(conflictId, out _);
    }

    public void Resync()
    {
        _history.Reset(_session.Revision);
        ClearConflicts();
    }

    private void RegisterConflict(CadCollabBatch batch, CadCollabTransformResult transformed)
    {
        var entityKey = ResolveConflictEntityKey(batch);
        var conflictId = batch.BatchId == Guid.Empty ? Guid.NewGuid() : batch.BatchId;
        var summary = transformed.RequiresResync
            ? "Remote operation window is stale for retained history; resync or force reapply is required."
            : "Concurrent edit could not be deterministically transformed.";
        var conflict = new CadRealtimeConflict(
            ConflictId: conflictId.ToString("D"),
            EntityKey: entityKey,
            Summary: summary,
            ResolutionPolicy: "Transform + LWW fallback",
            TimestampUtc: DateTimeOffset.UtcNow);

        lock (_sync)
        {
            _conflicts[conflictId] = new PendingConflict(conflict, batch);
        }

        RaiseConflictsChanged();
    }

    private void RemoveConflict(Guid conflictId)
    {
        var removed = false;
        lock (_sync)
        {
            removed = _conflicts.Remove(conflictId);
        }

        if (removed)
        {
            RaiseConflictsChanged();
        }
    }

    private void ClearConflicts()
    {
        var hadEntries = false;
        lock (_sync)
        {
            hadEntries = _conflicts.Count > 0;
            _conflicts.Clear();
        }

        if (hadEntries)
        {
            RaiseConflictsChanged();
        }
    }

    private void RaiseConflictsChanged()
    {
        ConflictsChanged?.Invoke(this, GetConflicts());
    }

    private static string ResolveConflictEntityKey(CadCollabBatch batch)
    {
        foreach (var operation in batch.Operations)
        {
            if (operation.EntityId is { } entityId && !entityId.IsEmpty)
            {
                return entityId.ToString();
            }
        }

        return $"batch:{batch.BatchId:D}";
    }

    private sealed record PendingConflict(CadRealtimeConflict Conflict, CadCollabBatch Batch);
}
