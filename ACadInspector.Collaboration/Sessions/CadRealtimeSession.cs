using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using ACadInspector.Collaboration.Contracts;
using ACadInspector.Collaboration.Services;
using ACadInspector.Collaboration.Snapshots;
using ACadInspector.Collaboration.Transports;
using ACadInspector.Editing.Operations;

namespace ACadInspector.Collaboration.Sessions;

public sealed class CadRealtimeSession : ICadRealtimeSession
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int SnapshotIntervalBatches = 512;
    private const int SnapshotMaxBatches = 100_000;

    private readonly CadCollabSessionCoordinator _coordinator;
    private readonly ICadRealtimeTransport _transport;
    private readonly ICadCollabSnapshotStore? _snapshotStore;
    private readonly SemaphoreSlim _recoveryGate = new(1, 1);
    private readonly object _persistSync = new();
    private readonly HashSet<Guid> _persistedBatchIds = new();
    private long _lamport;
    private int _batchesSinceSnapshot;
    private bool _recoveryLoaded;
    private bool _disposed;

    public CadRealtimeSession(
        Guid actorId,
        CadCollabSessionCoordinator coordinator,
        ICadRealtimeTransport transport,
        ICadCollabSnapshotStore? snapshotStore = null)
    {
        ActorId = actorId == Guid.Empty ? Guid.NewGuid() : actorId;
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _snapshotStore = snapshotStore;
        _transport.MessageReceived += OnMessageReceived;
        _transport.StateChanged += OnTransportStateChanged;
        _coordinator.ConflictsChanged += OnConflictsChanged;
    }

    public Guid ActorId { get; }
    public long Version => _coordinator.Version;
    public event EventHandler<CadRealtimeStateChangedEventArgs>? TransportStateChanged;
    public event EventHandler<CadCollabPresence>? PresenceReceived;
    public event EventHandler<IReadOnlyList<CadRealtimeConflict>>? ConflictsChanged;
    public event EventHandler<CadRealtimeOperationsAppliedEventArgs>? OperationsApplied;

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await RecoverFromStoreAsync(cancellationToken).ConfigureAwait(false);
        await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        return _transport.DisconnectAsync(cancellationToken);
    }

    public async ValueTask ReconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask ResyncAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _coordinator.Resync();
        return ValueTask.CompletedTask;
    }

    public async ValueTask SubmitLocalAsync(
        IReadOnlyList<CadOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(operations);

        var batch = _coordinator.SubmitLocalBatch(ActorId, operations, NextLamport());
        RaiseOperationsApplied(isRemote: false, batch.ActorId, Version, batch.Operations);
        var envelope = new CadRealtimeEnvelope(
            Kind: CadCollabOpKind.OperationBatch,
            Batch: batch,
            Snapshot: null,
            Presence: null,
            TimeToLiveSeconds: null);
        await SendEnvelopeAsync(envelope, cancellationToken).ConfigureAwait(false);
        await PersistBatchAsync(batch, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SubmitLocalAppliedAsync(
        IReadOnlyList<CadOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(operations);

        if (operations.Count == 0)
        {
            return;
        }

        var batch = _coordinator.SubmitLocalAppliedBatch(ActorId, operations, NextLamport());
        var envelope = new CadRealtimeEnvelope(
            Kind: CadCollabOpKind.OperationBatch,
            Batch: batch,
            Snapshot: null,
            Presence: null,
            TimeToLiveSeconds: null);
        await SendEnvelopeAsync(envelope, cancellationToken).ConfigureAwait(false);
        await PersistBatchAsync(batch, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PublishPresenceAsync(
        CadCollabPresence presence,
        TimeSpan? timeToLive = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(presence);

        var ttl = timeToLive ?? TimeSpan.FromSeconds(10);
        if (ttl <= TimeSpan.Zero)
        {
            ttl = TimeSpan.FromSeconds(10);
        }

        var envelope = new CadRealtimeEnvelope(
            Kind: CadCollabOpKind.PresenceUpdate,
            Batch: null,
            Snapshot: null,
            Presence: presence with
            {
                UpdatedAtUtc = presence.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : presence.UpdatedAtUtc
            },
            TimeToLiveSeconds: ttl.TotalSeconds);
        await SendEnvelopeAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> ReapplyConflictAsync(string conflictId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!Guid.TryParse(conflictId, out var conflictGuid))
        {
            return false;
        }

        if (!_coordinator.TryReapplyConflict(conflictGuid, out var reappliedBatch))
        {
            return false;
        }

        RaiseOperationsApplied(isRemote: false, reappliedBatch.ActorId, Version, reappliedBatch.Operations);
        var envelope = new CadRealtimeEnvelope(
            Kind: CadCollabOpKind.OperationBatch,
            Batch: reappliedBatch,
            Snapshot: null,
            Presence: null,
            TimeToLiveSeconds: null);
        await SendEnvelopeAsync(envelope, cancellationToken).ConfigureAwait(false);
        await PersistBatchAsync(reappliedBatch, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public IReadOnlyList<CadRealtimeConflict> GetConflicts()
    {
        ThrowIfDisposed();
        return _coordinator.GetConflicts();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transport.MessageReceived -= OnMessageReceived;
        _transport.StateChanged -= OnTransportStateChanged;
        _coordinator.ConflictsChanged -= OnConflictsChanged;
        await _transport.DisposeAsync();
    }

    private void OnMessageReceived(object? sender, CadRealtimeMessageEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<CadRealtimeEnvelope>(args.Payload.Span, JsonOptions);
            if (TryHandleEnvelope(envelope))
            {
                return;
            }

            var batch = JsonSerializer.Deserialize<CadCollabBatch>(args.Payload.Span, JsonOptions);
            if (batch is null)
            {
                return;
            }

            var transformed = _coordinator.ApplyRemoteBatch(batch);
            if (!transformed.RequiresResync && transformed.Operations.Count > 0)
            {
                RaiseOperationsApplied(isRemote: true, batch.ActorId, Version, transformed.Operations);
            }

            _ = PersistBatchAsync(batch);
        }
        catch (JsonException)
        {
            // Ignore non-CAD payloads from shared transports.
        }
    }

    private void OnTransportStateChanged(object? sender, CadRealtimeStateChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        TransportStateChanged?.Invoke(this, args);
    }

    private void OnConflictsChanged(object? sender, IReadOnlyList<CadRealtimeConflict> conflicts)
    {
        if (_disposed)
        {
            return;
        }

        ConflictsChanged?.Invoke(this, conflicts);
    }

    private bool TryHandleEnvelope(CadRealtimeEnvelope? envelope)
    {
        if (envelope is null)
        {
            return false;
        }

        switch (envelope.Kind)
        {
            case CadCollabOpKind.OperationBatch:
                if (envelope.Batch is null)
                {
                    return false;
                }

                var transformed = _coordinator.ApplyRemoteBatch(envelope.Batch);
                if (!transformed.RequiresResync && transformed.Operations.Count > 0)
                {
                    RaiseOperationsApplied(
                        isRemote: true,
                        envelope.Batch.ActorId,
                        Version,
                        transformed.Operations);
                }

                _ = PersistBatchAsync(envelope.Batch);
                return true;
            case CadCollabOpKind.PresenceUpdate:
                if (envelope.Presence is null)
                {
                    return false;
                }

                PresenceReceived?.Invoke(this, envelope.Presence);
                return true;
            case CadCollabOpKind.SnapshotReplace:
                if (envelope.Snapshot is null)
                {
                    return false;
                }

                _ = ApplyAndPersistSnapshotAsync(envelope.Snapshot);
                return true;
            default:
                return false;
        }
    }

    private async ValueTask SendEnvelopeAsync(CadRealtimeEnvelope envelope, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        await _transport.SendAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private long NextLamport()
    {
        return Interlocked.Increment(ref _lamport);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void RaiseOperationsApplied(bool isRemote, Guid actorId, long version, IReadOnlyList<CadOperation> operations)
    {
        if (_disposed || operations.Count == 0)
        {
            return;
        }

        OperationsApplied?.Invoke(this, new CadRealtimeOperationsAppliedEventArgs(
            IsRemote: isRemote,
            ActorId: actorId,
            Version: version,
            Operations: operations));
    }

    private sealed record CadRealtimeEnvelope(
        CadCollabOpKind Kind,
        CadCollabBatch? Batch,
        CadCollabSnapshot? Snapshot,
        CadCollabPresence? Presence,
        double? TimeToLiveSeconds);

    private async ValueTask RecoverFromStoreAsync(CancellationToken cancellationToken)
    {
        if (_snapshotStore is null || _recoveryLoaded)
        {
            return;
        }

        await _recoveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_recoveryLoaded)
            {
                return;
            }

            var snapshot = await _snapshotStore.LoadLatestSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot is not null)
            {
                ApplySnapshot(snapshot);
            }

            var batches = await _snapshotStore.LoadBatchesAsync(cancellationToken).ConfigureAwait(false);
            if (batches.Count > 0)
            {
                foreach (var batch in batches
                             .OrderBy(static item => item.BaseVersion)
                             .ThenBy(static item => item.Sequence)
                             .ThenBy(static item => item.Lamport)
                             .ThenBy(static item => item.TimestampUtc))
                {
                    var transformed = _coordinator.ApplyRemoteBatch(batch);
                    if (!transformed.RequiresResync && transformed.Operations.Count > 0)
                    {
                        RaiseOperationsApplied(isRemote: true, batch.ActorId, Version, transformed.Operations);
                    }

                    TryTrackPersistedBatch(batch.BatchId);
                }
            }

            _recoveryLoaded = true;
        }
        catch (Exception)
        {
            // Recovery is best-effort and should not block session establishment.
        }
        finally
        {
            _recoveryGate.Release();
        }
    }

    private async ValueTask PersistBatchAsync(CadCollabBatch batch, CancellationToken cancellationToken = default)
    {
        if (_snapshotStore is null)
        {
            return;
        }

        if (!TryTrackPersistedBatch(batch.BatchId))
        {
            return;
        }

        try
        {
            await _snapshotStore.AppendBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            var snapshot = await MaybeCreateSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return;
            }

            var envelope = new CadRealtimeEnvelope(
                Kind: CadCollabOpKind.SnapshotReplace,
                Batch: null,
                Snapshot: snapshot,
                Presence: null,
                TimeToLiveSeconds: null);
            await SendEnvelopeAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Persistence is best-effort and should not block collaborative editing.
        }
    }

    private async ValueTask<CadCollabSnapshot?> MaybeCreateSnapshotAsync(CancellationToken cancellationToken)
    {
        _batchesSinceSnapshot++;
        if (_batchesSinceSnapshot < SnapshotIntervalBatches)
        {
            return null;
        }

        _batchesSinceSnapshot = 0;
        if (_snapshotStore is null)
        {
            return null;
        }

        var batches = await BuildSnapshotBatchesAsync(cancellationToken).ConfigureAwait(false);
        if (batches.Count == 0)
        {
            return null;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(batches, JsonOptions);
        var snapshot = new CadCollabSnapshot(
            SnapshotId: Guid.NewGuid(),
            Version: Version,
            Payload: payload,
            TimestampUtc: DateTimeOffset.UtcNow);
        await _snapshotStore.WriteSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await _snapshotStore.CompactAsync(cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    private async ValueTask ApplyAndPersistSnapshotAsync(CadCollabSnapshot snapshot)
    {
        try
        {
            ApplySnapshot(snapshot);
            if (_snapshotStore is not null)
            {
                await _snapshotStore.WriteSnapshotAsync(snapshot).ConfigureAwait(false);
            }
        }
        catch
        {
            // Ignore malformed snapshot envelopes.
        }
    }

    private void ApplySnapshot(CadCollabSnapshot snapshot)
    {
        if (snapshot.Version <= Version)
        {
            return;
        }

        if (!TryDeserializeSnapshotPayload(snapshot.Payload, out var snapshotBatches) || snapshotBatches.Count == 0)
        {
            return;
        }

        var batches = NormalizeBatches(snapshotBatches);
        _coordinator.Resync();
        foreach (var batch in batches)
        {
            var transformed = _coordinator.ApplyRemoteBatch(batch);
            if (!transformed.RequiresResync && transformed.Operations.Count > 0)
            {
                RaiseOperationsApplied(isRemote: true, batch.ActorId, Version, transformed.Operations);
            }

            TryTrackPersistedBatch(batch.BatchId);
        }
    }

    private static bool TryDeserializeSnapshotPayload(byte[] payload, out IReadOnlyList<CadCollabBatch> batches)
    {
        batches = Array.Empty<CadCollabBatch>();
        if (payload.Length == 0)
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<CadCollabBatch[]>(payload, JsonOptions);
            if (parsed is null || parsed.Length == 0)
            {
                return false;
            }

            batches = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool TryTrackPersistedBatch(Guid batchId)
    {
        if (batchId == Guid.Empty)
        {
            return true;
        }

        lock (_persistSync)
        {
            return _persistedBatchIds.Add(batchId);
        }
    }

    private async ValueTask<IReadOnlyList<CadCollabBatch>> BuildSnapshotBatchesAsync(CancellationToken cancellationToken)
    {
        if (_snapshotStore is null)
        {
            return Array.Empty<CadCollabBatch>();
        }

        var latestSnapshot = await _snapshotStore.LoadLatestSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var oplogBatches = await _snapshotStore.LoadBatchesAsync(cancellationToken).ConfigureAwait(false);

        if (latestSnapshot is null)
        {
            return NormalizeBatches(oplogBatches);
        }

        if (!TryDeserializeSnapshotPayload(latestSnapshot.Payload, out var snapshotBatches))
        {
            return NormalizeBatches(oplogBatches);
        }

        if (snapshotBatches.Count == 0)
        {
            return NormalizeBatches(oplogBatches);
        }

        if (oplogBatches.Count == 0)
        {
            return NormalizeBatches(snapshotBatches);
        }

        var merged = new List<CadCollabBatch>(snapshotBatches.Count + oplogBatches.Count);
        merged.AddRange(snapshotBatches);
        merged.AddRange(oplogBatches);
        return NormalizeBatches(merged);
    }

    private static IReadOnlyList<CadCollabBatch> NormalizeBatches(IReadOnlyList<CadCollabBatch> batches)
    {
        if (batches.Count == 0)
        {
            return Array.Empty<CadCollabBatch>();
        }

        var ordered = batches
            .OrderBy(static item => item.BaseVersion)
            .ThenBy(static item => item.Sequence)
            .ThenBy(static item => item.Lamport)
            .ThenBy(static item => item.TimestampUtc)
            .ToArray();

        var seen = new HashSet<Guid>();
        var deduped = new List<CadCollabBatch>(ordered.Length);
        foreach (var batch in ordered)
        {
            if (batch.BatchId != Guid.Empty && !seen.Add(batch.BatchId))
            {
                continue;
            }

            deduped.Add(batch);
        }

        if (deduped.Count <= SnapshotMaxBatches)
        {
            return deduped;
        }

        var start = deduped.Count - SnapshotMaxBatches;
        var tail = new CadCollabBatch[SnapshotMaxBatches];
        deduped.CopyTo(start, tail, 0, SnapshotMaxBatches);
        return tail;
    }
}
