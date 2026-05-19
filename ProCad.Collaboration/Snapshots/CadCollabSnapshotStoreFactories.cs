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
    private const string DefaultKeyPrefix = "procad.collab";

    private readonly string _keyPrefix;
    private readonly string? _legacyKeyPrefix;
    private readonly IBrowserCadCollabKeyValueStore _storage;
    private readonly ConcurrentDictionary<string, ICadCollabSnapshotStore> _stores =
        new(StringComparer.Ordinal);

    public BrowserCadCollabSnapshotStoreFactory(
        string keyPrefix = DefaultKeyPrefix,
        IBrowserCadCollabKeyValueStore? storage = null,
        string? legacyKeyPrefix = null)
    {
        if (string.IsNullOrWhiteSpace(keyPrefix))
        {
            throw new ArgumentException("Key prefix must not be empty.", nameof(keyPrefix));
        }

        _keyPrefix = keyPrefix.Trim();
        _legacyKeyPrefix = NormalizeLegacyPrefix(_keyPrefix, legacyKeyPrefix);
        _storage = storage ?? new BrowserHybridCadCollabKeyValueStore();
    }

    public ICadCollabSnapshotStore CreateStore(string scopeKey)
    {
        var scope = NormalizeScope(scopeKey);
        return _stores.GetOrAdd(scope, CreateStoreCore);
    }

    private ICadCollabSnapshotStore CreateStoreCore(string scope)
    {
        var currentPrefix = $"{_keyPrefix}.{scope}";
        var legacyPrefix = _legacyKeyPrefix is null ? null : $"{_legacyKeyPrefix}.{scope}";
        return new BrowserCadCollabSnapshotStore(_storage, currentPrefix, legacyPrefix);
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
