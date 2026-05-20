using System.Text.Json;
using ProCad.Editing.Identifiers;

namespace ProCad.Editing.Constraints;

public sealed class CadConstraintService : ICadConstraintService
{
    private readonly ICadConstraintStore _store;
    private readonly ICadConstraintSnapshotCodec _codec;

    public CadConstraintService(ICadConstraintStore store, ICadConstraintSnapshotCodec codec)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
    }

    public ICadConstraintGraph Graph => _store.Graph;

    public IReadOnlyList<CadConstraint> GetConstraints()
    {
        return _store.GetAll();
    }

    public bool TryGetConstraint(CadConstraintId id, out CadConstraint constraint)
    {
        return _store.TryGet(id, out constraint!);
    }

    public CadConstraint AddConstraint(
        CadConstraintKind kind,
        IReadOnlyList<CadConstraintReference> references,
        IReadOnlyDictionary<string, string>? parameters = null,
        bool isDriving = true,
        CadConstraintId? id = null)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (references.Count == 0)
        {
            throw new ArgumentException("At least one constrained reference is required.", nameof(references));
        }

        var constraint = new CadConstraint(
            Id: id is { } value && value.Value != Guid.Empty ? value : CadConstraintId.New(),
            Kind: kind,
            References: references,
            Parameters: parameters ?? new Dictionary<string, string>(StringComparer.Ordinal),
            IsDriving: isDriving,
            CreatedAtUtc: DateTimeOffset.UtcNow);

        if (!_store.TryAdd(constraint))
        {
            throw new InvalidOperationException($"Constraint '{constraint.Id.Value:D}' could not be added.");
        }

        return constraint;
    }

    public bool TryRemoveConstraint(CadConstraintId id, out CadConstraint removed)
    {
        return _store.TryRemove(id, out removed!);
    }

    public bool TrySetConstraintParameters(
        CadConstraintId id,
        IReadOnlyDictionary<string, string> parameters,
        bool? isDriving,
        out CadConstraint updated)
    {
        updated = null!;
        ArgumentNullException.ThrowIfNull(parameters);
        if (!_store.TryGet(id, out var current))
        {
            return false;
        }

        var next = current with
        {
            Parameters = new Dictionary<string, string>(parameters, StringComparer.Ordinal),
            IsDriving = isDriving ?? current.IsDriving
        };

        if (!_store.TryUpdate(next))
        {
            return false;
        }

        updated = next;
        return true;
    }

    public CadConstraintSnapshot ExportSnapshot()
    {
        return _store.CreateSnapshot();
    }

    public void ImportSnapshot(CadConstraintSnapshot snapshot)
    {
        _store.LoadSnapshot(snapshot);
    }

    public byte[] ExportPayload()
    {
        return _codec.Serialize(_store.CreateSnapshot());
    }

    public bool ImportPayload(ReadOnlySpan<byte> payload)
    {
        if (!_codec.TryDeserialize(payload, out var snapshot))
        {
            return false;
        }

        _store.LoadSnapshot(snapshot);
        return true;
    }
}

public sealed class CadConstraintJsonSnapshotCodec : ICadConstraintSnapshotCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public byte[] Serialize(CadConstraintSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var dto = new SnapshotDto(
            snapshot.SchemaVersion,
            snapshot.SavedAtUtc.ToUnixTimeMilliseconds(),
            snapshot.Constraints.Select(static constraint => ConstraintDto.From(constraint)).ToArray());
        return JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
    }

    public bool TryDeserialize(ReadOnlySpan<byte> payload, out CadConstraintSnapshot snapshot)
    {
        snapshot = null!;
        if (payload.Length == 0)
        {
            return false;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<SnapshotDto>(payload, JsonOptions);
            if (dto is null || dto.SchemaVersion <= 0)
            {
                return false;
            }

            var constraints = new List<CadConstraint>(dto.Constraints.Count);
            foreach (var item in dto.Constraints)
            {
                if (!item.TryToModel(out var constraint))
                {
                    return false;
                }

                constraints.Add(constraint);
            }

            snapshot = new CadConstraintSnapshot(
                dto.SchemaVersion,
                DateTimeOffset.FromUnixTimeMilliseconds(dto.SavedAtUnixMs),
                constraints);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record SnapshotDto(
        int SchemaVersion,
        long SavedAtUnixMs,
        IReadOnlyList<ConstraintDto> Constraints);

    private sealed record ConstraintDto(
        string Id,
        string Kind,
        bool IsDriving,
        long CreatedAtUnixMs,
        IReadOnlyList<ReferenceDto> References,
        IReadOnlyDictionary<string, string> Parameters)
    {
        public static ConstraintDto From(CadConstraint constraint)
        {
            return new ConstraintDto(
                Id: constraint.Id.Value.ToString("D"),
                Kind: constraint.Kind.ToString(),
                IsDriving: constraint.IsDriving,
                CreatedAtUnixMs: constraint.CreatedAtUtc.ToUnixTimeMilliseconds(),
                References: constraint.References.Select(static reference => ReferenceDto.From(reference)).ToArray(),
                Parameters: constraint.Parameters.Count == 0
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(constraint.Parameters, StringComparer.Ordinal));
        }

        public bool TryToModel(out CadConstraint constraint)
        {
            constraint = null!;
            if (!Guid.TryParse(Id, out var idGuid) ||
                !Enum.TryParse<CadConstraintKind>(Kind, ignoreCase: true, out var kind))
            {
                return false;
            }

            var references = new List<CadConstraintReference>(References.Count);
            foreach (var item in References)
            {
                if (!item.TryToModel(out var reference))
                {
                    return false;
                }

                references.Add(reference);
            }

            constraint = new CadConstraint(
                Id: new CadConstraintId(idGuid),
                Kind: kind,
                References: references,
                Parameters: Parameters.Count == 0
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(Parameters, StringComparer.Ordinal),
                IsDriving: IsDriving,
                CreatedAtUtc: DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtUnixMs));
            return true;
        }
    }

    private sealed record ReferenceDto(
        string EntityId,
        int? VertexIndex,
        string? Role)
    {
        public static ReferenceDto From(CadConstraintReference reference)
        {
            return new ReferenceDto(
                EntityId: reference.EntityId.Value.ToString("D"),
                VertexIndex: reference.VertexIndex,
                Role: reference.Role);
        }

        public bool TryToModel(out CadConstraintReference reference)
        {
            reference = null!;
            if (!Guid.TryParse(EntityId, out var entityGuid))
            {
                return false;
            }

            reference = new CadConstraintReference(new CadEntityId(entityGuid), VertexIndex, Role);
            return true;
        }
    }
}
