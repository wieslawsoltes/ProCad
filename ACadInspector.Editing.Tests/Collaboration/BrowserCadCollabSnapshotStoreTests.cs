using ACadInspector.Collaboration.Contracts;
using ACadInspector.Collaboration.Snapshots;
using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using CSMath;
using Xunit;

namespace ACadInspector.Editing.Tests.Collaboration;

public sealed class BrowserCadCollabSnapshotStoreTests
{
    [Fact]
    public async Task AppendBatchAsync_DeduplicatesAndRecoversFromTransientSetFailure()
    {
        const string prefix = "browser-collab";
        var storage = new FakeBrowserStorage();
        storage.FailSet($"{prefix}.oplog", count: 1);
        var store = new BrowserCadCollabSnapshotStore(storage, prefix);

        var batch = new CadCollabBatch(
            BatchId: Guid.NewGuid(),
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
    public async Task LoadBatchesAsync_WithCorruptPayload_RemovesCorruptData()
    {
        const string prefix = "browser-collab-corrupt";
        var storage = new FakeBrowserStorage();
        storage.SetRaw($"{prefix}.oplog", "{bad-json");
        var store = new BrowserCadCollabSnapshotStore(storage, prefix);

        var loaded = await store.LoadBatchesAsync();

        Assert.Empty(loaded);
        Assert.False(storage.ContainsKey($"{prefix}.oplog"));
    }

    [Fact]
    public async Task WriteSnapshotAsync_ReplacesOplogAndReturnsDefensiveCopy()
    {
        const string prefix = "browser-collab-snapshot";
        var storage = new FakeBrowserStorage();
        var store = new BrowserCadCollabSnapshotStore(storage, prefix);

        await store.AppendBatchAsync(new CadCollabBatch(
            BatchId: Guid.NewGuid(),
            ActorId: Guid.NewGuid(),
            BaseVersion: 1,
            Sequence: 1,
            Lamport: 1,
            TimestampUtc: DateTimeOffset.UtcNow,
            Operations: [CadOperationPayloadCodec.CreatePoint(CadEntityId.New(), new XYZ(3, 4, 0))]));

        var snapshot = new CadCollabSnapshot(
            SnapshotId: Guid.NewGuid(),
            Version: 10,
            Payload: [1, 2, 3, 4],
            TimestampUtc: DateTimeOffset.UtcNow);

        await store.WriteSnapshotAsync(snapshot);
        var loadedSnapshot = await store.LoadLatestSnapshotAsync();
        var loadedBatches = await store.LoadBatchesAsync();

        Assert.NotNull(loadedSnapshot);
        Assert.Empty(loadedBatches);
        Assert.Equal(snapshot.Version, loadedSnapshot!.Version);
        loadedSnapshot.Payload[0] = 99;

        var reloaded = await store.LoadLatestSnapshotAsync();
        Assert.NotNull(reloaded);
        Assert.Equal(1, reloaded!.Payload[0]);
    }

    private sealed class FakeBrowserStorage : IBrowserCadCollabKeyValueStore
    {
        private readonly Dictionary<string, string> _storage = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _remainingSetFailures = new(StringComparer.Ordinal);

        public ValueTask<string?> GetItemAsync(string key, CancellationToken cancellationToken = default)
        {
            _storage.TryGetValue(key, out var value);
            return ValueTask.FromResult<string?>(value);
        }

        public ValueTask<bool> SetItemAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            if (_remainingSetFailures.TryGetValue(key, out var remaining) && remaining > 0)
            {
                _remainingSetFailures[key] = remaining - 1;
                return ValueTask.FromResult(false);
            }

            _storage[key] = value;
            return ValueTask.FromResult(true);
        }

        public ValueTask RemoveItemAsync(string key, CancellationToken cancellationToken = default)
        {
            _storage.Remove(key);
            return ValueTask.CompletedTask;
        }

        public void SetRaw(string key, string value)
        {
            _storage[key] = value;
        }

        public bool ContainsKey(string key)
        {
            return _storage.ContainsKey(key);
        }

        public void FailSet(string key, int count)
        {
            _remainingSetFailures[key] = Math.Max(0, count);
        }
    }
}
