using ProCad.Editing.Identifiers;

namespace ProCad.Editing.Constraints;

public sealed class CadConstraintStore : ICadConstraintStore
{
    private const int SnapshotSchemaVersion = 1;
    private readonly Dictionary<CadConstraintId, CadConstraint> _constraints = new();

    public CadConstraintStore()
        : this(new CadConstraintGraph())
    {
    }

    public CadConstraintStore(ICadConstraintGraph graph)
    {
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    public ICadConstraintGraph Graph { get; }

    public IReadOnlyList<CadConstraint> GetAll()
    {
        return _constraints.Values.ToArray();
    }

    public bool TryGet(CadConstraintId id, out CadConstraint constraint)
    {
        return _constraints.TryGetValue(id, out constraint!);
    }

    public bool TryAdd(CadConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        if (constraint.Id == default || constraint.Id.Value == Guid.Empty)
        {
            return false;
        }

        if (constraint.References.Count == 0)
        {
            return false;
        }

        if (_constraints.ContainsKey(constraint.Id))
        {
            return false;
        }

        var normalized = Normalize(constraint);
        _constraints[normalized.Id] = normalized;
        Graph.Add(normalized);
        return true;
    }

    public bool TryUpdate(CadConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        if (constraint.Id.IsEmpty ||
            !_constraints.TryGetValue(constraint.Id, out var existing))
        {
            return false;
        }

        var normalized = Normalize(constraint);
        _constraints[normalized.Id] = normalized;

        if (!existing.References.SequenceEqual(normalized.References))
        {
            Graph.Remove(existing);
            Graph.Add(normalized);
        }

        return true;
    }

    public bool TryRemove(CadConstraintId id, out CadConstraint removed)
    {
        if (!_constraints.Remove(id, out removed!))
        {
            return false;
        }

        Graph.Remove(removed);
        return true;
    }

    public CadConstraintSnapshot CreateSnapshot()
    {
        return new CadConstraintSnapshot(
            SchemaVersion: SnapshotSchemaVersion,
            SavedAtUtc: DateTimeOffset.UtcNow,
            Constraints: _constraints.Values.ToArray());
    }

    public void LoadSnapshot(CadConstraintSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion <= 0)
        {
            throw new InvalidOperationException("Constraint snapshot schema version is invalid.");
        }

        Clear();
        foreach (var constraint in snapshot.Constraints)
        {
            if (!TryAdd(constraint))
            {
                throw new InvalidOperationException(
                    $"Constraint snapshot contains duplicate or invalid entry '{constraint.Id.Value:D}'.");
            }
        }
    }

    public void Clear()
    {
        _constraints.Clear();
        Graph.Clear();
    }

    private static CadConstraint Normalize(CadConstraint constraint)
    {
        var references = constraint.References
            .Where(static reference => reference is not null)
            .GroupBy(static reference => (reference.EntityId, reference.VertexIndex, reference.Role), static reference => reference)
            .Select(static group => group.First())
            .ToArray();

        var parameters = constraint.Parameters.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(constraint.Parameters, StringComparer.Ordinal);

        return constraint with
        {
            References = references,
            Parameters = parameters,
            CreatedAtUtc = constraint.CreatedAtUtc == default ? DateTimeOffset.UtcNow : constraint.CreatedAtUtc
        };
    }
}
