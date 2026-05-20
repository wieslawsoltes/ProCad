namespace ProCad.Editing.Operations;

public sealed record CadOperationBatch(
    Guid BatchId,
    Guid ActorId,
    long BaseVersion,
    long Sequence,
    DateTimeOffset TimestampUtc,
    IReadOnlyList<CadOperation> Operations)
{
    public static CadOperationBatch Create(Guid actorId, long baseVersion, long sequence, IReadOnlyList<CadOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        return new CadOperationBatch(Guid.NewGuid(), actorId, baseVersion, sequence, DateTimeOffset.UtcNow, operations);
    }
}
