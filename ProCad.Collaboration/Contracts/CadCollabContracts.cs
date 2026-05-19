using ProCad.Editing.Operations;

namespace ProCad.Collaboration.Contracts;

public enum CadCollabOpKind
{
    OperationBatch,
    SnapshotReplace,
    PresenceUpdate
}

public interface ICadCollabOp
{
    CadCollabOpKind Kind { get; }
}

public sealed record CadCollabBatch(
    Guid BatchId,
    Guid ActorId,
    long BaseVersion,
    long Sequence,
    long Lamport,
    DateTimeOffset TimestampUtc,
    IReadOnlyList<CadOperation> Operations);

public sealed record CadCollabOp(CadCollabBatch Batch) : ICadCollabOp
{
    public CadCollabOpKind Kind => CadCollabOpKind.OperationBatch;
}

public sealed record CadCollabSnapshot(
    Guid SnapshotId,
    long Version,
    byte[] Payload,
    DateTimeOffset TimestampUtc);

public sealed record CadCollabSnapshotOp(CadCollabSnapshot Snapshot) : ICadCollabOp
{
    public CadCollabOpKind Kind => CadCollabOpKind.SnapshotReplace;
}

public sealed record CadCollabPresence(
    Guid UserId,
    string DisplayName,
    string Color,
    string? Status,
    string? ActiveTool,
    string? PromptStage,
    CadCollabPoint? CursorPoint,
    CadCollabViewportSummary? Viewport,
    IReadOnlyList<Guid>? SelectedEntityIds,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<CadCollabToolPreviewPrimitive>? ToolPreview = null,
    Guid? SessionId = null);

public sealed record CadCollabPoint(double X, double Y);

public sealed record CadCollabViewportSummary(
    CadCollabPoint Center,
    double Zoom,
    double Width,
    double Height);

public sealed record CadCollabToolPreviewPrimitive(
    string Kind,
    CadCollabPoint Start,
    CadCollabPoint? End = null,
    string? Text = null,
    CadCollabPoint? Mid = null,
    double? Scalar = null);

public sealed record CadCollabPresenceOp(CadCollabPresence Presence, TimeSpan TimeToLive) : ICadCollabOp
{
    public CadCollabOpKind Kind => CadCollabOpKind.PresenceUpdate;
}
