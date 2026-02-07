using System.IO;
using ACadInspector.Collaboration.Contracts;
using ACadInspector.Collaboration.Snapshots;
using ACadInspector.Editing.Operations;
using Xunit;

namespace ACadInspector.Editing.Tests.Collaboration;

public sealed class CadCollabSnapshotStoreFactoryTests
{
    [Fact]
    public async Task InMemoryFactory_IsolatesScopes()
    {
        var factory = new InMemoryCadCollabSnapshotStoreFactory();
        var storeA = factory.CreateStore("doc-a");
        var storeB = factory.CreateStore("doc-b");

        var batch = new CadCollabBatch(
            BatchId: Guid.NewGuid(),
            ActorId: Guid.NewGuid(),
            BaseVersion: 0,
            Sequence: 1,
            Lamport: 1,
            TimestampUtc: DateTimeOffset.UtcNow,
            Operations:
            [
                new CadOperation(CadOperationKind.UpdateProperty, null)
            ]);

        await storeA.AppendBatchAsync(batch);

        var batchesA = await storeA.LoadBatchesAsync();
        var batchesB = await storeB.LoadBatchesAsync();

        Assert.Single(batchesA);
        Assert.Empty(batchesB);
    }

    [Fact]
    public async Task FileFactory_IsolatesScopes()
    {
        var basePath = Path.Combine(Path.GetTempPath(), $"acadinspector-collab-{Guid.NewGuid():N}");
        Directory.CreateDirectory(basePath);
        try
        {
            var factory = new FileCadCollabSnapshotStoreFactory(basePath);
            var storeA = factory.CreateStore("layers1");
            var storeB = factory.CreateStore("layers2");

            var batch = new CadCollabBatch(
                BatchId: Guid.NewGuid(),
                ActorId: Guid.NewGuid(),
                BaseVersion: 0,
                Sequence: 1,
                Lamport: 1,
                TimestampUtc: DateTimeOffset.UtcNow,
                Operations:
                [
                    new CadOperation(CadOperationKind.UpdateProperty, null)
                ]);

            await storeA.AppendBatchAsync(batch);

            var batchesA = await storeA.LoadBatchesAsync();
            var batchesB = await storeB.LoadBatchesAsync();

            Assert.Single(batchesA);
            Assert.Empty(batchesB);
        }
        finally
        {
            try
            {
                Directory.Delete(basePath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp test data.
            }
        }
    }
}
