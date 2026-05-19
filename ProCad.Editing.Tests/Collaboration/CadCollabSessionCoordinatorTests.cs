using ProCad.Collaboration.Contracts;
using ProCad.Collaboration.History;
using ProCad.Collaboration.Sessions;
using ProCad.Editing.Constraints;
using ProCad.Editing.EntityIndex;
using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Selection;
using ProCad.Editing.Sessions;
using ProCad.Editing.Undo;
using ACadSharp;
using Xunit;

namespace ProCad.Editing.Tests.Collaboration;

public sealed class CadCollabSessionCoordinatorTests
{
    [Fact]
    public void ApplyRemoteBatch_WithStaleWindow_RegistersConflict_AndReapplyClearsIt()
    {
        var session = new FakeCadEditorSession();
        var coordinator = new CadCollabSessionCoordinator(session, new CadCollabOpHistory());

        var batch = new CadCollabBatch(
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

        var transformed = coordinator.ApplyRemoteBatch(batch);

        Assert.True(transformed.RequiresResync);
        var conflicts = coordinator.GetConflicts();
        var conflict = Assert.Single(conflicts);
        Assert.Equal(0, session.ApplyCallCount);

        var reapplied = coordinator.TryReapplyConflict(Guid.Parse(conflict.ConflictId), out var reappliedBatch);
        Assert.True(reapplied);
        Assert.NotEqual(Guid.Empty, reappliedBatch.BatchId);
        Assert.Equal(1, session.ApplyCallCount);
        Assert.Empty(coordinator.GetConflicts());
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
