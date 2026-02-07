using CSMath;

namespace ACadInspector.Editing.Clipboard;

public interface ICadClipboardService
{
    void SetPayload(CadClipboardPayload payload);
    bool TryGetPayload(out CadClipboardPayload payload);
    void Clear();
}

public sealed record CadClipboardEntity(
    string EntityType,
    IReadOnlyDictionary<string, string> Payload,
    XYZ ReferencePoint);

public sealed record CadClipboardPayload(
    IReadOnlyList<CadClipboardEntity> Entities,
    XYZ BasePoint,
    string SchemaVersion = "1.0");
