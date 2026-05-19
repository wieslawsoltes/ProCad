using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using ProCad.Collaboration.Contracts;

namespace ProCad.Collaboration.Snapshots;

public interface IBrowserCadCollabKeyValueStore
{
    ValueTask<string?> GetItemAsync(string key, CancellationToken cancellationToken = default);
    ValueTask<bool> SetItemAsync(string key, string value, CancellationToken cancellationToken = default);
    ValueTask RemoveItemAsync(string key, CancellationToken cancellationToken = default);
}

[SupportedOSPlatform("browser")]
public sealed class BrowserHybridCadCollabKeyValueStore : IBrowserCadCollabKeyValueStore
{
    public async ValueTask<string?> GetItemAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (BrowserIndexedDbInterop.IsAvailable())
        {
            try
            {
                var value = await BrowserIndexedDbInterop.GetItemAsync(key).ConfigureAwait(false);
                if (value is not null)
                {
                    return value;
                }
            }
            catch
            {
                // Fall through to local storage fallback.
            }
        }

        return BrowserLocalStorageInterop.GetItem(key);
    }

    public async ValueTask<bool> SetItemAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        if (BrowserIndexedDbInterop.IsAvailable())
        {
            try
            {
                if (await BrowserIndexedDbInterop.SetItemAsync(key, value).ConfigureAwait(false))
                {
                    BrowserLocalStorageInterop.SetItem(key, value);
                    return true;
                }
            }
            catch
            {
                // Fall through to local storage fallback.
            }
        }

        try
        {
            BrowserLocalStorageInterop.SetItem(key, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask RemoveItemAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (BrowserIndexedDbInterop.IsAvailable())
        {
            try
            {
                await BrowserIndexedDbInterop.RemoveItemAsync(key).ConfigureAwait(false);
            }
            catch
            {
                // Fall through to local storage removal.
            }
        }

        BrowserLocalStorageInterop.RemoveItem(key);
    }
}

public sealed class BrowserCadCollabSnapshotStore : ICadCollabSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string DefaultKeyPrefix = "procad.collab";
    private const int MaxBatches = 8_192;
    private const int CompactTail = 1_024;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _snapshotKey;
    private readonly string _oplogKey;
    private readonly string? _legacySnapshotKey;
    private readonly string? _legacyOplogKey;
    private readonly IBrowserCadCollabKeyValueStore _storage;

    [SupportedOSPlatform("browser")]
    public BrowserCadCollabSnapshotStore(string keyPrefix = DefaultKeyPrefix)
        : this(new BrowserHybridCadCollabKeyValueStore(), keyPrefix)
    {
    }

    public BrowserCadCollabSnapshotStore(
        IBrowserCadCollabKeyValueStore storage,
        string keyPrefix = DefaultKeyPrefix,
        string? legacyKeyPrefix = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        if (string.IsNullOrWhiteSpace(keyPrefix))
        {
            throw new ArgumentException("Key prefix must not be empty.", nameof(keyPrefix));
        }

        var prefix = keyPrefix.Trim();
        _snapshotKey = $"{prefix}.snapshot";
        _oplogKey = $"{prefix}.oplog";

        var legacyPrefix = NormalizeLegacyPrefix(prefix, legacyKeyPrefix);
        if (legacyPrefix is not null)
        {
            _legacySnapshotKey = $"{legacyPrefix}.snapshot";
            _legacyOplogKey = $"{legacyPrefix}.oplog";
        }
    }

    public async ValueTask<CadCollabSnapshot?> LoadLatestSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var raw = await GetItemWithLegacyMigrationAsync(_snapshotKey, _legacySnapshotKey, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            try
            {
                var snapshot = JsonSerializer.Deserialize<CadCollabSnapshot>(raw, JsonOptions);
                return snapshot is null ? null : CloneSnapshot(snapshot);
            }
            catch (JsonException)
            {
                // Corrupt snapshot data is dropped to preserve recovery guarantees.
                await RemoveCurrentAndLegacyAsync(_snapshotKey, _legacySnapshotKey, cancellationToken).ConfigureAwait(false);
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<CadCollabBatch>> LoadBatchesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadBatchListCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask AppendBatchAsync(CadCollabBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var batches = (await LoadBatchListCoreAsync(cancellationToken).ConfigureAwait(false)).ToList();
            if (batch.BatchId != Guid.Empty && batches.Any(existing => existing.BatchId == batch.BatchId))
            {
                return;
            }

            batches.Add(CloneBatch(batch));
            if (batches.Count > MaxBatches)
            {
                var skip = batches.Count - MaxBatches;
                batches.RemoveRange(0, skip);
            }

            await PersistBatchesAsync(batches, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask WriteSnapshotAsync(CadCollabSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var persisted = await _storage
                .SetItemAsync(_snapshotKey, JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken)
                .ConfigureAwait(false);
            if (!persisted)
            {
                return;
            }

            await _storage.RemoveItemAsync(_oplogKey, cancellationToken).ConfigureAwait(false);
            await RemoveLegacyItemAsync(_legacySnapshotKey, cancellationToken).ConfigureAwait(false);
            await RemoveLegacyItemAsync(_legacyOplogKey, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask CompactAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var batches = await LoadBatchListCoreAsync(cancellationToken).ConfigureAwait(false);
            if (batches.Count <= CompactTail)
            {
                return;
            }

            var start = Math.Max(0, batches.Count - CompactTail);
            var tail = batches.Skip(start).Select(CloneBatch).ToArray();
            if (await _storage.SetItemAsync(_oplogKey, JsonSerializer.Serialize(tail, JsonOptions), cancellationToken).ConfigureAwait(false))
            {
                await RemoveLegacyItemAsync(_legacyOplogKey, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask PersistBatchesAsync(IReadOnlyList<CadCollabBatch> batches, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(batches, JsonOptions);
        if (await _storage.SetItemAsync(_oplogKey, payload, cancellationToken).ConfigureAwait(false))
        {
            await RemoveLegacyItemAsync(_legacyOplogKey, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Retry once with the same payload for transient failures.
        if (await _storage.SetItemAsync(_oplogKey, payload, cancellationToken).ConfigureAwait(false))
        {
            await RemoveLegacyItemAsync(_legacyOplogKey, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Quota or transient storage failures: keep a compact tail and retry once.
        if (batches.Count <= CompactTail)
        {
            return;
        }

        var start = Math.Max(0, batches.Count - CompactTail);
        var tail = batches.Skip(start).Select(CloneBatch).ToArray();
        if (await _storage.SetItemAsync(_oplogKey, JsonSerializer.Serialize(tail, JsonOptions), cancellationToken).ConfigureAwait(false))
        {
            await RemoveLegacyItemAsync(_legacyOplogKey, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<IReadOnlyList<CadCollabBatch>> LoadBatchListCoreAsync(CancellationToken cancellationToken)
    {
        var raw = await GetItemWithLegacyMigrationAsync(_oplogKey, _legacyOplogKey, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<CadCollabBatch>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<CadCollabBatch[]>(raw, JsonOptions);
            if (parsed is null || parsed.Length == 0)
            {
                return Array.Empty<CadCollabBatch>();
            }

            var result = new List<CadCollabBatch>(parsed.Length);
            var seen = new HashSet<Guid>();
            foreach (var batch in parsed)
            {
                if (batch is null)
                {
                    continue;
                }

                if (batch.BatchId != Guid.Empty && !seen.Add(batch.BatchId))
                {
                    continue;
                }

                result.Add(CloneBatch(batch));
            }

            return result;
        }
        catch (JsonException)
        {
            await RemoveCurrentAndLegacyAsync(_oplogKey, _legacyOplogKey, cancellationToken).ConfigureAwait(false);
            return Array.Empty<CadCollabBatch>();
        }
    }

    private async ValueTask<string?> GetItemWithLegacyMigrationAsync(
        string currentKey,
        string? legacyKey,
        CancellationToken cancellationToken)
    {
        var raw = await _storage.GetItemAsync(currentKey, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(raw) || legacyKey is null)
        {
            return raw;
        }

        var legacyRaw = await _storage.GetItemAsync(legacyKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(legacyRaw))
        {
            return legacyRaw;
        }

        if (await _storage.SetItemAsync(currentKey, legacyRaw, cancellationToken).ConfigureAwait(false))
        {
            await _storage.RemoveItemAsync(legacyKey, cancellationToken).ConfigureAwait(false);
        }

        return legacyRaw;
    }

    private async ValueTask RemoveCurrentAndLegacyAsync(
        string currentKey,
        string? legacyKey,
        CancellationToken cancellationToken)
    {
        await _storage.RemoveItemAsync(currentKey, cancellationToken).ConfigureAwait(false);
        await RemoveLegacyItemAsync(legacyKey, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RemoveLegacyItemAsync(string? legacyKey, CancellationToken cancellationToken)
    {
        if (legacyKey is not null)
        {
            await _storage.RemoveItemAsync(legacyKey, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string? NormalizeLegacyPrefix(string currentPrefix, string? legacyPrefix)
    {
        if (!string.IsNullOrWhiteSpace(legacyPrefix))
        {
            return legacyPrefix.Trim();
        }

        return string.Equals(currentPrefix, DefaultKeyPrefix, StringComparison.Ordinal)
            ? string.Concat("acad", "inspector.collab")
            : null;
    }

    private static CadCollabSnapshot CloneSnapshot(CadCollabSnapshot snapshot)
    {
        var payload = snapshot.Payload.Length == 0
            ? Array.Empty<byte>()
            : snapshot.Payload.ToArray();
        return snapshot with { Payload = payload };
    }

    private static CadCollabBatch CloneBatch(CadCollabBatch batch)
    {
        var operations = batch.Operations.Count == 0
            ? Array.Empty<Editing.Operations.CadOperation>()
            : batch.Operations.ToArray();
        return batch with { Operations = operations };
    }
}

[SupportedOSPlatform("browser")]
internal static partial class BrowserLocalStorageInterop
{
    [JSImport("globalThis.localStorage.getItem")]
    internal static partial string? GetItem(string key);

    [JSImport("globalThis.localStorage.setItem")]
    internal static partial void SetItem(string key, string value);

    [JSImport("globalThis.localStorage.removeItem")]
    internal static partial void RemoveItem(string key);
}

[SupportedOSPlatform("browser")]
internal static partial class BrowserIndexedDbInterop
{
    [JSImport("globalThis.proCadCollab.idbAvailable")]
    internal static partial bool IsAvailable();

    [JSImport("globalThis.proCadCollab.idbGet")]
    internal static partial Task<string?> GetItemAsync(string key);

    [JSImport("globalThis.proCadCollab.idbSet")]
    internal static partial Task<bool> SetItemAsync(string key, string value);

    [JSImport("globalThis.proCadCollab.idbRemove")]
    internal static partial Task<bool> RemoveItemAsync(string key);
}
