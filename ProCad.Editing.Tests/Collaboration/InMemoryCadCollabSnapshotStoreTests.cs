using ProCad.Collaboration.Contracts;
using ProCad.Collaboration.Snapshots;
using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using CSMath;
using Xunit;

namespace ProCad.Editing.Tests.Collaboration;

public sealed class InMemoryCadCollabSnapshotStoreTests
{
    [Fact]
    public async Task AppendBatchAsync_DeduplicatesByBatchId()
    {
        var store = new InMemoryCadCollabSnapshotStore();
        var batchId = Guid.NewGuid();
        var batch = new CadCollabBatch(
            BatchId: batchId,
            ActorId: Guid.NewGuid(),
            BaseVersion: 0,
            Sequence: 1,
            Lamport: 1,
            TimestampUtc: DateTimeOffset.UtcNow,
            Operations: [CadOperationPayloadCodec.CreatePoint(CadEntityId.New(), new XYZ(1, 2, 0))]);

        await store.AppendBatchAsync(batch);
        await store.AppendBatchAsync(batch);

        var loaded = await store.LoadBatchesAsync();
        Assert.Single(loaded);
    }

    [Fact]
    public async Task LoadLatestSnapshotAsync_ReturnsDefensiveCopy()
    {
        var store = new InMemoryCadCollabSnapshotStore();
        var snapshot = new CadCollabSnapshot(
            SnapshotId: Guid.NewGuid(),
            Version: 10,
            Payload: [1, 2, 3],
            TimestampUtc: DateTimeOffset.UtcNow);

        await store.WriteSnapshotAsync(snapshot);
        var first = await store.LoadLatestSnapshotAsync();
        Assert.NotNull(first);
        first!.Payload[0] = 99;

        var second = await store.LoadLatestSnapshotAsync();
        Assert.NotNull(second);
        Assert.Equal(1, second!.Payload[0]);
    }

    [Fact]
    public async Task CompactAsync_TrimsLargeBatchHistory()
    {
        var store = new InMemoryCadCollabSnapshotStore();
        for (var index = 0; index < 1_500; index++)
        {
            await store.AppendBatchAsync(new CadCollabBatch(
                BatchId: Guid.NewGuid(),
                ActorId: Guid.NewGuid(),
                BaseVersion: index,
                Sequence: index + 1,
                Lamport: index + 1,
                TimestampUtc: DateTimeOffset.UtcNow,
                Operations: [CadOperationPayloadCodec.CreatePoint(CadEntityId.New(), new XYZ(index, 0, 0))]));
        }

        await store.CompactAsync();
        var loaded = await store.LoadBatchesAsync();
        Assert.True(loaded.Count <= 1_024);
    }
}
