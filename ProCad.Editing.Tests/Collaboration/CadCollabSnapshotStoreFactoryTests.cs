using System.IO;
using ProCad.Collaboration.Contracts;
using ProCad.Collaboration.Snapshots;
using ProCad.Editing.Operations;
using Xunit;

namespace ProCad.Editing.Tests.Collaboration;

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
        var basePath = Path.Combine(Path.GetTempPath(), $"procad-collab-{Guid.NewGuid():N}");
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

    [Fact]
    public async Task FileFactory_MigratesLegacyScopeOplogToCurrentBasePath()
    {
        var basePath = Path.Combine(Path.GetTempPath(), $"procad-collab-{Guid.NewGuid():N}");
        var legacyBasePath = Path.Combine(Path.GetTempPath(), $"legacy-collab-{Guid.NewGuid():N}");
        try
        {
            const string scope = "layers";
            var legacyStore = new FileCadCollabSnapshotStore(Path.Combine(legacyBasePath, scope));
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
            await legacyStore.AppendBatchAsync(batch);

            var factory = new FileCadCollabSnapshotStoreFactory(basePath, legacyBasePath);
            var store = factory.CreateStore(scope);

            var batches = await store.LoadBatchesAsync();

            Assert.Single(batches);
            Assert.True(File.Exists(Path.Combine(basePath, scope, "cadcollab.oplog.jsonl")));
            Assert.False(File.Exists(Path.Combine(legacyBasePath, scope, "cadcollab.oplog.jsonl")));
        }
        finally
        {
            DeleteDirectoryQuietly(basePath);
            DeleteDirectoryQuietly(legacyBasePath);
        }
    }

    [Fact]
    public async Task FileFactory_MigratesLegacyScopeSnapshotToCurrentBasePath()
    {
        var basePath = Path.Combine(Path.GetTempPath(), $"procad-collab-{Guid.NewGuid():N}");
        var legacyBasePath = Path.Combine(Path.GetTempPath(), $"legacy-collab-{Guid.NewGuid():N}");
        try
        {
            const string scope = "snapshot";
            var legacyStore = new FileCadCollabSnapshotStore(Path.Combine(legacyBasePath, scope));
            var snapshot = new CadCollabSnapshot(
                SnapshotId: Guid.NewGuid(),
                Version: 7,
                Payload: [1, 3, 5],
                TimestampUtc: DateTimeOffset.UtcNow);
            await legacyStore.WriteSnapshotAsync(snapshot);

            var factory = new FileCadCollabSnapshotStoreFactory(basePath, legacyBasePath);
            var store = factory.CreateStore(scope);

            var loaded = await store.LoadLatestSnapshotAsync();

            Assert.NotNull(loaded);
            Assert.Equal(snapshot.Version, loaded!.Version);
            Assert.True(File.Exists(Path.Combine(basePath, scope, "cadcollab.snapshot.json")));
            Assert.False(File.Exists(Path.Combine(legacyBasePath, scope, "cadcollab.snapshot.json")));
        }
        finally
        {
            DeleteDirectoryQuietly(basePath);
            DeleteDirectoryQuietly(legacyBasePath);
        }
    }

    private static void DeleteDirectoryQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for temp test data.
        }
    }
}
