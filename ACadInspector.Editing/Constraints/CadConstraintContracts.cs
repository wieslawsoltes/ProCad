using ACadInspector.Editing.Identifiers;

namespace ACadInspector.Editing.Constraints;

public enum CadConstraintKind
{
    Coincident,
    Concentric,
    Collinear,
    Parallel,
    Perpendicular,
    Horizontal,
    Vertical,
    Tangent,
    Equal,
    Symmetric,
    Fixed,
    Distance,
    AlignedDistance,
    Angle,
    Radius,
    Diameter
}

public sealed record CadConstraintReference(
    CadEntityId EntityId,
    int? VertexIndex = null,
    string? Role = null);

public sealed record CadConstraint(
    CadConstraintId Id,
    CadConstraintKind Kind,
    IReadOnlyList<CadConstraintReference> References,
    IReadOnlyDictionary<string, string> Parameters,
    bool IsDriving,
    DateTimeOffset CreatedAtUtc);

public sealed record CadConstraintSnapshot(
    int SchemaVersion,
    DateTimeOffset SavedAtUtc,
    IReadOnlyList<CadConstraint> Constraints);

public interface ICadConstraintGraph
{
    IReadOnlyList<CadConstraintId> GetConstraints(CadEntityId entityId);
    IReadOnlyDictionary<CadEntityId, IReadOnlyList<CadConstraintId>> Snapshot();
    void Add(CadConstraint constraint);
    void Remove(CadConstraint constraint);
    void Clear();
}

public interface ICadConstraintStore
{
    IReadOnlyList<CadConstraint> GetAll();
    bool TryGet(CadConstraintId id, out CadConstraint constraint);
    bool TryAdd(CadConstraint constraint);
    bool TryUpdate(CadConstraint constraint);
    bool TryRemove(CadConstraintId id, out CadConstraint removed);
    CadConstraintSnapshot CreateSnapshot();
    void LoadSnapshot(CadConstraintSnapshot snapshot);
    void Clear();
    ICadConstraintGraph Graph { get; }
}

public interface ICadConstraintSnapshotCodec
{
    byte[] Serialize(CadConstraintSnapshot snapshot);
    bool TryDeserialize(ReadOnlySpan<byte> payload, out CadConstraintSnapshot snapshot);
}

public interface ICadConstraintService
{
    IReadOnlyList<CadConstraint> GetConstraints();
    bool TryGetConstraint(CadConstraintId id, out CadConstraint constraint);
    CadConstraint AddConstraint(
        CadConstraintKind kind,
        IReadOnlyList<CadConstraintReference> references,
        IReadOnlyDictionary<string, string>? parameters = null,
        bool isDriving = true,
        CadConstraintId? id = null);
    bool TryRemoveConstraint(CadConstraintId id, out CadConstraint removed);
    bool TrySetConstraintParameters(
        CadConstraintId id,
        IReadOnlyDictionary<string, string> parameters,
        bool? isDriving,
        out CadConstraint updated);
    CadConstraintSnapshot ExportSnapshot();
    void ImportSnapshot(CadConstraintSnapshot snapshot);
    byte[] ExportPayload();
    bool ImportPayload(ReadOnlySpan<byte> payload);
    ICadConstraintGraph Graph { get; }
}
