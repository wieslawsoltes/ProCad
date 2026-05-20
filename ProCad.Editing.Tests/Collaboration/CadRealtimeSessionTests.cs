using ProCad.Collaboration.History;
using ProCad.Collaboration.Services;
using ProCad.Collaboration.Sessions;
using ProCad.Collaboration.Snapshots;
using ProCad.Collaboration.Transports;
using ProCad.Collaboration.Contracts;
using ProCad.Editing.Constraints;
using ProCad.Editing.EntityIndex;
using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Selection;
using ProCad.Editing.Sessions;
using ProCad.Editing.Undo;
using ACadSharp;
using System.Text.Json;
using Xunit;

namespace ProCad.Editing.Tests.Collaboration;

public sealed class CadRealtimeSessionTests
{
    [Fact]
    public async Task SubmitLocalAsync_DoesNotDoubleApplyOnLoopbackEcho()
    {
        var editorSession = new FakeCadEditorSession();
        var coordinator = new CadCollabSessionCoordinator(editorSession, new CadCollabOpHistory());
        var transport = new ProEditCadRealtimeTransportFactory().CreateLoopback();
        await using var realtime = new CadRealtimeSession(Guid.NewGuid(), coordinator, transport);

        await realtime.ConnectAsync();

        var operations = new[]
        {
            new CadOperation(
                CadOperationKind.UpdateProperty,
                EntityId: null,
                Payload: new Dictionary<string, string> { ["Layer"] = "A-WALL" })
        };

        await realtime.SubmitLocalAsync(operations);
        await realtime.DisconnectAsync();

        Assert.Equal(1, editorSession.ApplyCallCount);
        Assert.Equal(1, realtime.Version);
    }

    [Fact]
    public async Task PublishPresenceAsync_Loopback_RaisesPresenceReceived()
    {
        var editorSession = new FakeCadEditorSession();
        var coordinator = new CadCollabSessionCoordinator(editorSession, new CadCollabOpHistory());
        var transport = new ProEditCadRealtimeTransportFactory().CreateLoopback();
        await using var realtime = new CadRealtimeSession(Guid.NewGuid(), coordinator, transport);
        var tcs = new TaskCompletionSource<CadCollabPresence>(TaskCreationOptions.RunContinuationsAsynchronously);
        realtime.PresenceReceived += (_, presence) => tcs.TrySetResult(presence);

        await realtime.ConnectAsync();

        await realtime.PublishPresenceAsync(new CadCollabPresence(
            UserId: Guid.NewGuid(),
            DisplayName: "Remote",
            Color: "#22AAFF",
            Status: "Editing",
            ActiveTool: "LINE",
            PromptStage: "Specify first point",
            CursorPoint: new CadCollabPoint(10, 20),
            Viewport: null,
            SelectedEntityIds: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.True(ReferenceEquals(completed, tcs.Task), "Presence event was not raised.");
        var received = await tcs.Task;
        Assert.Equal("Remote", received.DisplayName);
    }

    [Fact]
    public async Task ConnectAsync_ReplaysPersistedBatchesFromSnapshotStore()
    {
        var editorSession = new FakeCadEditorSession();
        var coordinator = new CadCollabSessionCoordinator(editorSession, new CadCollabOpHistory());
        var transport = new ProEditCadRealtimeTransportFactory().CreateLoopback();
        var snapshotStore = new InMemoryCadCollabSnapshotStore();
        await snapshotStore.AppendBatchAsync(new CadCollabBatch(
            BatchId: Guid.NewGuid(),
            ActorId: Guid.NewGuid(),
            BaseVersion: 0,
            Sequence: 1,
            Lamport: 1,
            TimestampUtc: DateTimeOffset.UtcNow,
            Operations:
            [
                new CadOperation(
                    CadOperationKind.UpdateProperty,
                    EntityId: null,
                    Payload: new Dictionary<string, string> { ["Layer"] = "B-ANNO" })
            ]));

        await using var realtime = new CadRealtimeSession(Guid.NewGuid(), coordinator, transport, snapshotStore);
        await realtime.ConnectAsync();

        Assert.Equal(1, editorSession.ApplyCallCount);
        Assert.Equal(1, realtime.Version);
    }

    [Fact]
    public async Task ConnectAsync_ReplaysPersistedBatches_RaisesRemoteOperationsApplied()
    {
        var editorSession = new FakeCadEditorSession();
        var coordinator = new CadCollabSessionCoordinator(editorSession, new CadCollabOpHistory());
        var transport = new ProEditCadRealtimeTransportFactory().CreateLoopback();
        var snapshotStore = new InMemoryCadCollabSnapshotStore();
        await snapshotStore.AppendBatchAsync(new CadCollabBatch(
            BatchId: Guid.NewGuid(),
            ActorId: Guid.NewGuid(),
            BaseVersion: 0,
            Sequence: 1,
            Lamport: 1,
            TimestampUtc: DateTimeOffset.UtcNow,
            Operations:
            [
                new CadOperation(
                    CadOperationKind.UpdateProperty,
                    EntityId: null,
                    Payload: new Dictionary<string, string> { ["Layer"] = "B-ANNO" })
            ]));

        await using var realtime = new CadRealtimeSession(Guid.NewGuid(), coordinator, transport, snapshotStore);
        var operationEvents = 0;
        realtime.OperationsApplied += (_, args) =>
        {
            if (args.IsRemote)
            {
                operationEvents++;
            }
        };

        await realtime.ConnectAsync();

        Assert.Equal(1, operationEvents);
    }

    [Fact]
    public async Task SubmitLocalAppliedAsync_PersistsBatchToSnapshotStore()
    {
        var editorSession = new FakeCadEditorSession();
        var coordinator = new CadCollabSessionCoordinator(editorSession, new CadCollabOpHistory());
        var transport = new ProEditCadRealtimeTransportFactory().CreateLoopback();
        var snapshotStore = new InMemoryCadCollabSnapshotStore();
        await using var realtime = new CadRealtimeSession(Guid.NewGuid(), coordinator, transport, snapshotStore);
        await realtime.ConnectAsync();

        await realtime.SubmitLocalAppliedAsync(
        [
            new CadOperation(
                CadOperationKind.UpdateProperty,
                EntityId: null,
                Payload: new Dictionary<string, string> { ["Layer"] = "A-WALL" })
        ]);

        var batches = await snapshotStore.LoadBatchesAsync();
        Assert.Single(batches);
    }

    [Fact]
    public async Task ReapplyConflictAsync_PublishesAndPersistsReappliedBatch()
    {
        var editorSession = new FakeCadEditorSession();
        var coordinator = new CadCollabSessionCoordinator(editorSession, new CadCollabOpHistory());
        var transport = new ProEditCadRealtimeTransportFactory().CreateLoopback();
        var snapshotStore = new InMemoryCadCollabSnapshotStore();
        await using var realtime = new CadRealtimeSession(Guid.NewGuid(), coordinator, transport, snapshotStore);
        await realtime.ConnectAsync();

        var staleBatch = new CadCollabBatch(
            BatchId: Guid.NewGuid(),
            ActorId: Guid.NewGuid(),
            BaseVersion: -1,
            Sequence: 1,
            Lamport: 1,
            TimestampUtc: DateTimeOffset.UtcNow,
            Operations:
            [
                new CadOperation(
                    CadOperationKind.UpdateProperty,
                    EntityId: null,
                    Payload: new Dictionary<string, string> { ["Layer"] = "A-WALL" })
            ]);

        var payload = JsonSerializer.SerializeToUtf8Bytes(staleBatch);
        await transport.SendAsync(payload);

        var conflict = Assert.Single(realtime.GetConflicts());
        var reapplied = await realtime.ReapplyConflictAsync(conflict.ConflictId);

        Assert.True(reapplied);
        Assert.Empty(realtime.GetConflicts());
        Assert.True(editorSession.ApplyCallCount >= 1);

        var batches = await snapshotStore.LoadBatchesAsync();
        Assert.True(batches.Count >= 1);
    }

    [Fact]
    public async Task LoopbackRemoteBatch_RaisesRemoteOperationsApplied()
    {
        var editorSession = new FakeCadEditorSession();
        var coordinator = new CadCollabSessionCoordinator(editorSession, new CadCollabOpHistory());
        var transport = new ProEditCadRealtimeTransportFactory().CreateLoopback();
        await using var realtime = new CadRealtimeSession(Guid.NewGuid(), coordinator, transport);
        await realtime.ConnectAsync();

        var tcs = new TaskCompletionSource<CadRealtimeOperationsAppliedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        realtime.OperationsApplied += (_, args) =>
        {
            if (args.IsRemote)
            {
                tcs.TrySetResult(args);
            }
        };

        var remoteBatch = new CadCollabBatch(
            BatchId: Guid.NewGuid(),
            ActorId: Guid.NewGuid(),
            BaseVersion: realtime.Version,
            Sequence: realtime.Version + 1,
            Lamport: 1,
            TimestampUtc: DateTimeOffset.UtcNow,
            Operations:
            [
                new CadOperation(
                    CadOperationKind.UpdateProperty,
                    EntityId: null,
                    Payload: new Dictionary<string, string> { ["Layer"] = "REMOTE" })
            ]);
        var payload = JsonSerializer.SerializeToUtf8Bytes(remoteBatch);
        await transport.SendAsync(payload);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.True(ReferenceEquals(completed, tcs.Task), "Remote operations event was not raised.");

        var raised = await tcs.Task;
        Assert.True(raised.IsRemote);
        Assert.Single(raised.Operations);
    }

    [Fact]
    public async Task ConnectAsync_ReplaysFullHistoryAfterMultipleSnapshotRotations()
    {
        var snapshotStore = new InMemoryCadCollabSnapshotStore();
        var initialSession = new FakeCadEditorSession();
        var initialCoordinator = new CadCollabSessionCoordinator(initialSession, new CadCollabOpHistory());
        var initialTransport = new ProEditCadRealtimeTransportFactory().CreateLoopback();
        await using (var initialRealtime = new CadRealtimeSession(Guid.NewGuid(), initialCoordinator, initialTransport, snapshotStore))
        {
            await initialRealtime.ConnectAsync();

            var operation = new CadOperation(
                CadOperationKind.UpdateProperty,
                EntityId: null,
                Payload: new Dictionary<string, string> { ["Layer"] = "A-WALL" });

            for (var index = 0; index < 1_025; index++)
            {
                await initialRealtime.SubmitLocalAppliedAsync([operation]);
            }
        }

        var recoveredSession = new FakeCadEditorSession();
        var recoveredCoordinator = new CadCollabSessionCoordinator(recoveredSession, new CadCollabOpHistory());
        var recoveredTransport = new ProEditCadRealtimeTransportFactory().CreateLoopback();
        await using var recoveredRealtime = new CadRealtimeSession(Guid.NewGuid(), recoveredCoordinator, recoveredTransport, snapshotStore);
        await recoveredRealtime.ConnectAsync();

        Assert.Equal(1_025, recoveredSession.ApplyCallCount);
        Assert.Equal(1_025, recoveredRealtime.Version);
    }

    [Fact]
    public async Task ReconnectAsync_MultiClientReplayFromSharedStore_AppliesOnlyMissedBatches()
    {
        var snapshotStore = new InMemoryCadCollabSnapshotStore();
        var hub = new InMemoryTransportHub();
        var sessionA = new FakeCadEditorSession();
        var sessionB = new FakeCadEditorSession();
        var coordinatorA = new CadCollabSessionCoordinator(sessionA, new CadCollabOpHistory());
        var coordinatorB = new CadCollabSessionCoordinator(sessionB, new CadCollabOpHistory());
        await using var realtimeA = new CadRealtimeSession(Guid.NewGuid(), coordinatorA, hub.CreateTransport(), snapshotStore);
        await using var realtimeB = new CadRealtimeSession(Guid.NewGuid(), coordinatorB, hub.CreateTransport(), snapshotStore);

        await realtimeA.ConnectAsync();
        await realtimeB.ConnectAsync();

        await realtimeA.SubmitLocalAppliedAsync(
        [
            new CadOperation(
                CadOperationKind.UpdateProperty,
                EntityId: null,
                Payload: new Dictionary<string, string> { ["Layer"] = "A-WALL" })
        ]);

        Assert.Equal(1, sessionB.ApplyCallCount);
        Assert.Equal(1, realtimeB.Version);

        await realtimeB.DisconnectAsync();

        await realtimeA.SubmitLocalAppliedAsync(
        [
            new CadOperation(
                CadOperationKind.UpdateProperty,
                EntityId: null,
                Payload: new Dictionary<string, string> { ["Layer"] = "A-ANNO" })
        ]);

        Assert.Equal(1, sessionB.ApplyCallCount);

        await realtimeB.ReconnectAsync();

        Assert.Equal(2, sessionB.ApplyCallCount);
        Assert.Equal(2, realtimeB.Version);

        await realtimeB.ReconnectAsync();

        Assert.Equal(2, sessionB.ApplyCallCount);
        Assert.Equal(2, realtimeB.Version);
    }

    private sealed class InMemoryTransportHub
    {
        private readonly object _sync = new();
        private readonly List<InMemoryHubTransport> _transports = new();

        public InMemoryHubTransport CreateTransport()
        {
            var transport = new InMemoryHubTransport(this);
            lock (_sync)
            {
                _transports.Add(transport);
            }

            return transport;
        }

        public void Remove(InMemoryHubTransport transport)
        {
            lock (_sync)
            {
                _transports.Remove(transport);
            }
        }

        public void Broadcast(InMemoryHubTransport sender, ReadOnlyMemory<byte> payload)
        {
            InMemoryHubTransport[] transports;
            lock (_sync)
            {
                transports = _transports.ToArray();
            }

            foreach (var transport in transports)
            {
                if (ReferenceEquals(transport, sender) || !transport.IsConnected)
                {
                    continue;
                }

                transport.RaiseMessage(payload);
            }
        }
    }

    private sealed class InMemoryHubTransport : ICadRealtimeTransport
    {
        private readonly InMemoryTransportHub _hub;
        private bool _disposed;

        public InMemoryHubTransport(InMemoryTransportHub hub)
        {
            _hub = hub;
        }

        public bool IsConnected { get; private set; }

        public event EventHandler<CadRealtimeMessageEventArgs>? MessageReceived;
        public event EventHandler<CadRealtimeStateChangedEventArgs>? StateChanged;

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            IsConnected = true;
            StateChanged?.Invoke(this, new CadRealtimeStateChangedEventArgs(CadRealtimeTransportState.Connected, "Connected"));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            IsConnected = false;
            StateChanged?.Invoke(this, new CadRealtimeStateChangedEventArgs(CadRealtimeTransportState.Disconnected, "Disconnected"));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || !IsConnected)
            {
                return ValueTask.CompletedTask;
            }

            _hub.Broadcast(this, payload);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            IsConnected = false;
            _hub.Remove(this);
            return ValueTask.CompletedTask;
        }

        public void RaiseMessage(ReadOnlyMemory<byte> payload)
        {
            if (_disposed || !IsConnected)
            {
                return;
            }

            MessageReceived?.Invoke(this, new CadRealtimeMessageEventArgs(payload));
        }
    }

    private sealed class FakeCadEditorSession : ICadEditorSession
    {
        private long _revision;

        public CadDocumentSessionId SessionId { get; } = CadDocumentSessionId.New();
        public CadDocument Document { get; } = new();
        public CadSelectionSet SelectionSet { get; } = new();
        public ICadEntityIndex EntityIndex { get; } = new CadEntityIndex();
        public ICadUndoRedoService UndoRedo { get; } = new CadUndoRedoService();
        public ICadConstraintService Constraints { get; } =
            new CadConstraintService(new CadConstraintStore(), new CadConstraintJsonSnapshotCodec());
        public long Revision => _revision;
        public bool IsDirty { get; private set; }

        public int ApplyCallCount { get; private set; }

        public CadOperationBatch Apply(CadOperationBatch batch)
        {
            ApplyCallCount++;
            _revision += Math.Max(1, batch.Operations.Count);
            IsDirty = true;
            return batch;
        }

        public bool TryUndo(Guid actorId, out CadOperationBatch undoBatch)
        {
            undoBatch = null!;
            return false;
        }

        public bool TryRedo(Guid actorId, out CadOperationBatch redoBatch)
        {
            redoBatch = null!;
            return false;
        }

        public bool SetSelection(IEnumerable<object?> selection, CadSelectionMode mode)
        {
            return SelectionSet.Apply(selection, mode);
        }
    }
}
