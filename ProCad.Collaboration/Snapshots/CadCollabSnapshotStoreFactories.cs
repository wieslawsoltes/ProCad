using System.IO;
using System.Collections.Concurrent;
using System.Text;

namespace ProCad.Collaboration.Snapshots;

public sealed class FileCadCollabSnapshotStoreFactory : ICadCollabSnapshotStoreFactory
{
    private readonly string _basePath;
    private readonly ConcurrentDictionary<string, ICadCollabSnapshotStore> _stores =
        new(StringComparer.Ordinal);

    public FileCadCollabSnapshotStoreFactory(string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new ArgumentException("Base path must not be empty.", nameof(basePath));
        }

        _basePath = basePath;
    }

    public ICadCollabSnapshotStore CreateStore(string scopeKey)
    {
        var scope = NormalizeScope(scopeKey);
        return _stores.GetOrAdd(scope, CreateStoreCore);
    }

    private ICadCollabSnapshotStore CreateStoreCore(string scope)
    {
        return new FileCadCollabSnapshotStore(Path.Combine(_basePath, scope));
    }

    private static string NormalizeScope(string? scopeKey)
    {
        if (string.IsNullOrWhiteSpace(scopeKey))
        {
            return "default";
        }

        var buffer = new StringBuilder(scopeKey.Length);
        foreach (var character in scopeKey)
        {
            buffer.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
        }

        return buffer.Length == 0 ? "default" : buffer.ToString();
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("browser")]
public sealed class BrowserCadCollabSnapshotStoreFactory : ICadCollabSnapshotStoreFactory
{
    private readonly string _keyPrefix;
    private readonly IBrowserCadCollabKeyValueStore _storage;
    private readonly ConcurrentDictionary<string, ICadCollabSnapshotStore> _stores =
        new(StringComparer.Ordinal);

    public BrowserCadCollabSnapshotStoreFactory(
        string keyPrefix = "procad.collab",
        IBrowserCadCollabKeyValueStore? storage = null)
    {
        if (string.IsNullOrWhiteSpace(keyPrefix))
        {
            throw new ArgumentException("Key prefix must not be empty.", nameof(keyPrefix));
        }

        _keyPrefix = keyPrefix.Trim();
        _storage = storage ?? new BrowserHybridCadCollabKeyValueStore();
    }

    public ICadCollabSnapshotStore CreateStore(string scopeKey)
    {
        var scope = NormalizeScope(scopeKey);
        return _stores.GetOrAdd(scope, CreateStoreCore);
    }

    private ICadCollabSnapshotStore CreateStoreCore(string scope)
    {
        return new BrowserCadCollabSnapshotStore(_storage, $"{_keyPrefix}.{scope}");
    }

    private static string NormalizeScope(string? scopeKey)
    {
        if (string.IsNullOrWhiteSpace(scopeKey))
        {
            return "default";
        }

        var buffer = new StringBuilder(scopeKey.Length);
        foreach (var character in scopeKey)
        {
            buffer.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
        }

        return buffer.Length == 0 ? "default" : buffer.ToString();
    }
}

public sealed class InMemoryCadCollabSnapshotStoreFactory : ICadCollabSnapshotStoreFactory
{
    private readonly ConcurrentDictionary<string, ICadCollabSnapshotStore> _stores =
        new(StringComparer.Ordinal);

    public ICadCollabSnapshotStore CreateStore(string scopeKey)
    {
        var scope = string.IsNullOrWhiteSpace(scopeKey) ? "default" : scopeKey.Trim();
        return _stores.GetOrAdd(scope, static _ => new InMemoryCadCollabSnapshotStore());
    }
}
