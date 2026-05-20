using ProCad.Editing.Identifiers;

namespace ProCad.Editing.Constraints;

public sealed class CadConstraintGraph : ICadConstraintGraph
{
    private readonly Dictionary<CadEntityId, HashSet<CadConstraintId>> _adjacency = new();

    public IReadOnlyList<CadConstraintId> GetConstraints(CadEntityId entityId)
    {
        return _adjacency.TryGetValue(entityId, out var ids)
            ? ids.ToArray()
            : Array.Empty<CadConstraintId>();
    }

    public IReadOnlyDictionary<CadEntityId, IReadOnlyList<CadConstraintId>> Snapshot()
    {
        var snapshot = new Dictionary<CadEntityId, IReadOnlyList<CadConstraintId>>(_adjacency.Count);
        foreach (var (entityId, ids) in _adjacency)
        {
            snapshot[entityId] = ids.ToArray();
        }

        return snapshot;
    }

    public void Add(CadConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        foreach (var reference in constraint.References)
        {
            if (!_adjacency.TryGetValue(reference.EntityId, out var ids))
            {
                ids = new HashSet<CadConstraintId>();
                _adjacency[reference.EntityId] = ids;
            }

            ids.Add(constraint.Id);
        }
    }

    public void Remove(CadConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        foreach (var reference in constraint.References)
        {
            if (!_adjacency.TryGetValue(reference.EntityId, out var ids))
            {
                continue;
            }

            ids.Remove(constraint.Id);
            if (ids.Count == 0)
            {
                _adjacency.Remove(reference.EntityId);
            }
        }
    }

    public void Clear()
    {
        _adjacency.Clear();
    }
}
