using ACadInspector.Collaboration.Contracts;

namespace ACadInspector.Collaboration.Presence;

public sealed class CadCollabPresenceRegistry
{
    public static readonly TimeSpan MinimumTimeToLive = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaximumTimeToLive = TimeSpan.FromSeconds(30);
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly object _sync = new();

    public TimeSpan DefaultTimeToLive { get; }

    public CadCollabPresenceRegistry(TimeSpan? defaultTimeToLive = null)
    {
        DefaultTimeToLive = defaultTimeToLive ?? TimeSpan.FromSeconds(10);
    }

    public void Update(CadCollabPresence presence, TimeSpan? timeToLive = null, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(presence);

        var nowValue = now ?? DateTimeOffset.UtcNow;
        var ttl = timeToLive ?? DefaultTimeToLive;
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        ttl = ttl < MinimumTimeToLive
            ? MinimumTimeToLive
            : ttl > MaximumTimeToLive
                ? MaximumTimeToLive
                : ttl;

        var updated = presence.UpdatedAtUtc == default ? presence with { UpdatedAtUtc = nowValue } : presence;
        var expiresAt = updated.UpdatedAtUtc + ttl;

        lock (_sync)
        {
            _entries[updated.UserId] = new Entry(updated, expiresAt);
        }
    }

    public IReadOnlyList<CadCollabPresence> GetActive(DateTimeOffset? now = null)
    {
        var nowValue = now ?? DateTimeOffset.UtcNow;
        lock (_sync)
        {
            PruneInternal(nowValue, out _);
            return _entries.Values.Select(static e => e.Presence).ToArray();
        }
    }

    public IReadOnlyList<Guid> Prune(DateTimeOffset? now = null)
    {
        var nowValue = now ?? DateTimeOffset.UtcNow;
        lock (_sync)
        {
            PruneInternal(nowValue, out var removed);
            return removed;
        }
    }

    public int RemoveBySession(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            return 0;
        }

        lock (_sync)
        {
            var removed = 0;
            foreach (var (userId, entry) in _entries.ToArray())
            {
                if (entry.Presence.SessionId != sessionId)
                {
                    continue;
                }

                _entries.Remove(userId);
                removed++;
            }

            return removed;
        }
    }

    public bool RemoveByUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            return false;
        }

        lock (_sync)
        {
            return _entries.Remove(userId);
        }
    }

    private void PruneInternal(DateTimeOffset now, out List<Guid> removed)
    {
        removed = new List<Guid>();
        foreach (var (userId, entry) in _entries.ToArray())
        {
            if (entry.ExpiresAtUtc <= now)
            {
                _entries.Remove(userId);
                removed.Add(userId);
            }
        }
    }

    private readonly record struct Entry(CadCollabPresence Presence, DateTimeOffset ExpiresAtUtc);
}
