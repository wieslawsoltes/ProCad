using CSMath;

namespace ProCad.Editing.Clipboard;

public interface ICadClipboardService
{
    void SetPayload(CadClipboardPayload payload);
    bool TryGetPayload(out CadClipboardPayload payload);
    void Clear();
}

public interface ICadSystemClipboardSync
{
    ValueTask PublishAsync(CadClipboardPayload payload, CancellationToken cancellationToken = default);
    ValueTask<bool> TryHydrateAsync(CancellationToken cancellationToken = default);
}

public interface ICadSystemClipboardBridge
{
    ValueTask WriteAsync(CadClipboardPayload payload, CancellationToken cancellationToken = default);
    ValueTask<CadClipboardPayload?> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed record CadClipboardEntity(
    string EntityType,
    IReadOnlyDictionary<string, string> Payload,
    XYZ ReferencePoint);

public sealed record CadClipboardBlockDependency(
    string Name,
    IReadOnlyList<CadClipboardEntity> Entities);

public sealed record CadClipboardDependencies(
    IReadOnlyList<string> LayerNames,
    IReadOnlyList<string> LineTypeNames,
    IReadOnlyList<string> TextStyleNames,
    IReadOnlyList<string> DimensionStyleNames,
    IReadOnlyList<CadClipboardBlockDependency> BlockDependencies)
{
    public static CadClipboardDependencies Empty { get; } = new(
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<CadClipboardBlockDependency>());
}

public sealed record CadClipboardPayload(
    IReadOnlyList<CadClipboardEntity> Entities,
    XYZ BasePoint,
    string SchemaVersion = "1.0",
    CadClipboardDependencies? Dependencies = null);
