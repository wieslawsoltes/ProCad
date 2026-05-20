using ProCad.Collaboration.Contracts;
using ProCad.Collaboration.Transforms;
using ProCad.Editing.Operations;

namespace ProCad.Collaboration.History;

public sealed class CadCollabOpHistory
{
    private readonly List<AppliedOp> _history = new();
    private readonly Queue<Guid> _recentBatchQueue = new();
    private readonly HashSet<Guid> _recentBatchIds = new();
    private readonly int _maxEntries;
    private readonly int _maxRecentBatches;

    public long Version { get; private set; }
    public long MinRetainedVersion => _history.Count == 0 ? Version : _history[0].Version;

    public CadCollabOpHistory(int maxEntries = 100_000)
    {
        _maxEntries = Math.Max(1, maxEntries);
        _maxRecentBatches = Math.Max(1024, Math.Min(200_000, _maxEntries * 2));
    }

    public void Reset(long version = 0)
    {
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        _history.Clear();
        _recentBatchQueue.Clear();
        _recentBatchIds.Clear();
        Version = version;
    }

    public void AppendLocal(CadCollabBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        TrackBatch(batch.BatchId);
        Append(batch, batch.Operations);
    }

    public CadCollabTransformResult TransformRemote(CadCollabBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (IsDuplicateBatch(batch.BatchId))
        {
            return new CadCollabTransformResult(Array.Empty<CadOperation>(), RequiresResync: false);
        }

        var transformed = Transform(batch);
        if (transformed.RequiresResync || transformed.Operations.Count == 0)
        {
            return transformed;
        }

        Append(batch, transformed.Operations);
        TrackBatch(batch.BatchId);
        return transformed;
    }

    public CadCollabTransformResult Transform(CadCollabBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var minSupported = Math.Max(0, MinRetainedVersion - 1);
        if (batch.BaseVersion < minSupported)
        {
            return new CadCollabTransformResult(Array.Empty<CadOperation>(), RequiresResync: true);
        }

        if (batch.Operations.Count == 0)
        {
            return new CadCollabTransformResult(Array.Empty<CadOperation>(), RequiresResync: false);
        }

        var startIndex = ResolveHistoryStartIndex(batch.BaseVersion);
        if (startIndex >= _history.Count)
        {
            return new CadCollabTransformResult(batch.Operations, RequiresResync: false);
        }

        var transformed = new List<CadOperation>(batch.Operations.Count);
        foreach (var operation in batch.Operations)
        {
            var current = TransformOperation(operation, batch, startIndex);
            if (current is null)
            {
                continue;
            }

            transformed.Add(current);
        }

        return new CadCollabTransformResult(transformed, RequiresResync: false);
    }

    private void Append(CadCollabBatch batch, IReadOnlyList<CadOperation> operations)
    {
        foreach (var op in operations)
        {
            Version++;
            _history.Add(new AppliedOp(Version, batch.ActorId, batch.Sequence, batch.Lamport, op));
        }

        var overflow = _history.Count - _maxEntries;
        if (overflow > 0)
        {
            _history.RemoveRange(0, overflow);
        }
    }

    private bool IsDuplicateBatch(Guid batchId)
    {
        return batchId != Guid.Empty && _recentBatchIds.Contains(batchId);
    }

    private void TrackBatch(Guid batchId)
    {
        if (batchId == Guid.Empty)
        {
            return;
        }

        if (_recentBatchIds.Add(batchId))
        {
            _recentBatchQueue.Enqueue(batchId);
        }

        while (_recentBatchQueue.Count > _maxRecentBatches)
        {
            var expired = _recentBatchQueue.Dequeue();
            _recentBatchIds.Remove(expired);
        }
    }

    private readonly record struct AppliedOp(
        long Version,
        Guid ActorId,
        long Sequence,
        long Lamport,
        CadOperation Operation);

    private int ResolveHistoryStartIndex(long baseVersion)
    {
        if (_history.Count == 0)
        {
            return 0;
        }

        for (var index = 0; index < _history.Count; index++)
        {
            if (_history[index].Version > baseVersion)
            {
                return index;
            }
        }

        return _history.Count;
    }

    private CadOperation? TransformOperation(CadOperation incoming, CadCollabBatch batch, int startIndex)
    {
        var current = incoming;
        for (var index = startIndex; index < _history.Count; index++)
        {
            var existing = _history[index];
            if (!TargetsSameEntity(current, existing.Operation))
            {
                continue;
            }

            current = ResolveConflict(current, batch, existing);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static CadOperation? ResolveConflict(CadOperation incoming, CadCollabBatch incomingBatch, AppliedOp existing)
    {
        var incomingWins = IsIncomingNewerThanExisting(incomingBatch, existing);
        if (incoming.Kind == CadOperationKind.Composite || existing.Operation.Kind == CadOperationKind.Composite)
        {
            return incomingWins ? incoming : null;
        }

        // Delete acts as a tombstone for entity operations unless a newer create arrives.
        if (incoming.Kind == CadOperationKind.DeleteEntity)
        {
            return incomingWins ? incoming : null;
        }

        if (existing.Operation.Kind == CadOperationKind.DeleteEntity)
        {
            return incoming.Kind == CadOperationKind.CreateEntity && incomingWins
                ? incoming
                : null;
        }

        // Incoming edits that pre-date an existing create are stale.
        if (existing.Operation.Kind == CadOperationKind.CreateEntity &&
            incoming.Kind is CadOperationKind.TransformEntity or CadOperationKind.UpdateProperty or CadOperationKind.AddConstraint or CadOperationKind.RemoveConstraint &&
            !incomingWins)
        {
            return null;
        }

        return (incoming.Kind, existing.Operation.Kind) switch
        {
            (CadOperationKind.CreateEntity, CadOperationKind.CreateEntity) => incomingWins ? incoming : null,
            (CadOperationKind.CreateEntity, CadOperationKind.TransformEntity) => incomingWins ? incoming : null,
            (CadOperationKind.CreateEntity, CadOperationKind.UpdateProperty) => incomingWins ? incoming : null,
            (CadOperationKind.CreateEntity, CadOperationKind.AddConstraint) => incomingWins ? incoming : null,
            (CadOperationKind.CreateEntity, CadOperationKind.RemoveConstraint) => incomingWins ? incoming : null,

            (CadOperationKind.TransformEntity, CadOperationKind.TransformEntity) => incomingWins ? incoming : null,
            (CadOperationKind.TransformEntity, CadOperationKind.UpdateProperty) => incoming,
            (CadOperationKind.UpdateProperty, CadOperationKind.TransformEntity) => incoming,
            (CadOperationKind.UpdateProperty, CadOperationKind.UpdateProperty) => ResolvePropertyConflict(incoming, existing.Operation, incomingWins),
            (CadOperationKind.AddConstraint, CadOperationKind.AddConstraint) => ResolveConstraintConflict(incoming, existing.Operation, incomingWins),
            (CadOperationKind.AddConstraint, CadOperationKind.RemoveConstraint) => ResolveConstraintConflict(incoming, existing.Operation, incomingWins),
            (CadOperationKind.RemoveConstraint, CadOperationKind.AddConstraint) => ResolveConstraintConflict(incoming, existing.Operation, incomingWins),
            (CadOperationKind.RemoveConstraint, CadOperationKind.RemoveConstraint) => ResolveConstraintConflict(incoming, existing.Operation, incomingWins),
            (CadOperationKind.AddConstraint, CadOperationKind.TransformEntity) => incoming,
            (CadOperationKind.RemoveConstraint, CadOperationKind.TransformEntity) => incoming,
            (CadOperationKind.TransformEntity, CadOperationKind.AddConstraint) => incoming,
            (CadOperationKind.TransformEntity, CadOperationKind.RemoveConstraint) => incoming,
            (CadOperationKind.AddConstraint, CadOperationKind.UpdateProperty) => incoming,
            (CadOperationKind.RemoveConstraint, CadOperationKind.UpdateProperty) => incoming,
            (CadOperationKind.UpdateProperty, CadOperationKind.AddConstraint) => incoming,
            (CadOperationKind.UpdateProperty, CadOperationKind.RemoveConstraint) => incoming,

            // Fallback policy for mixed concurrent edits:
            // deterministic last-writer-wins using (Lamport, ActorId, Sequence).
            _ => incomingWins ? incoming : null
        };
    }

    private static CadOperation? ResolvePropertyConflict(
        CadOperation incoming,
        CadOperation existing,
        bool incomingWins)
    {
        if (!CadOperationPayloadCodec.TryGetUpdateProperty(incoming, out var incomingProperty, out _) ||
            !CadOperationPayloadCodec.TryGetUpdateProperty(existing, out var existingProperty, out _))
        {
            return incomingWins ? incoming : null;
        }

        if (!string.Equals(incomingProperty, existingProperty, StringComparison.Ordinal))
        {
            return incoming;
        }

        return incomingWins ? incoming : null;
    }

    private static CadOperation? ResolveConstraintConflict(
        CadOperation incoming,
        CadOperation existing,
        bool incomingWins)
    {
        var incomingKey = ResolveConstraintKey(incoming);
        var existingKey = ResolveConstraintKey(existing);
        if (string.IsNullOrWhiteSpace(incomingKey) ||
            string.IsNullOrWhiteSpace(existingKey) ||
            !string.Equals(incomingKey, existingKey, StringComparison.Ordinal))
        {
            return incoming;
        }

        return incomingWins ? incoming : null;
    }

    private static string? ResolveConstraintKey(CadOperation operation)
    {
        if (operation.Payload is null || operation.Payload.Count == 0)
        {
            return null;
        }

        foreach (var key in new[] { "ConstraintId", "constraintId", "Id", "id" })
        {
            if (operation.Payload.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool TargetsSameEntity(CadOperation left, CadOperation right)
    {
        if (left.EntityId is not { } leftId || right.EntityId is not { } rightId)
        {
            return false;
        }

        return !leftId.IsEmpty && leftId.Equals(rightId);
    }

    private static bool IsIncomingNewerThanExisting(CadCollabBatch incomingBatch, AppliedOp existing)
    {
        var lamportComparison = incomingBatch.Lamport.CompareTo(existing.Lamport);
        if (lamportComparison != 0)
        {
            return lamportComparison > 0;
        }

        var actorComparison = incomingBatch.ActorId.CompareTo(existing.ActorId);
        if (actorComparison != 0)
        {
            return actorComparison > 0;
        }

        return incomingBatch.Sequence > existing.Sequence;
    }
}
