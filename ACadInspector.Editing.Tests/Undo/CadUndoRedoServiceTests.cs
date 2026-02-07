using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Undo;
using Xunit;

namespace ACadInspector.Editing.Tests.Undo;

public sealed class CadUndoRedoServiceTests
{
    [Fact]
    public void Record_ThenUndoThenRedo_ReturnsExpectedBatches()
    {
        var service = new CadUndoRedoService();
        var actor = Guid.NewGuid();
        var forward = CreateBatch(actor, 0, 1, CadOperationKind.CreateEntity);
        var inverse = CreateBatch(actor, 1, 2, CadOperationKind.DeleteEntity);

        service.Record(forward, inverse);

        Assert.True(service.TryPopUndo(out var undo));
        Assert.Equal(inverse.BatchId, undo.BatchId);

        Assert.True(service.TryPopRedo(out var redo));
        Assert.Equal(forward.BatchId, redo.BatchId);
    }

    [Fact]
    public void Record_WithMergeKeyWithinWindow_MergesIntoSingleUndoUnit()
    {
        var service = new CadUndoRedoService();
        var actor = Guid.NewGuid();
        var stamp = DateTimeOffset.UtcNow;
        var forward1 = CreateBatch(actor, 0, 1, CadOperationKind.TransformEntity);
        var inverse1 = CreateBatch(actor, 1, 2, CadOperationKind.TransformEntity);
        var forward2 = CreateBatch(actor, 2, 3, CadOperationKind.TransformEntity);
        var inverse2 = CreateBatch(actor, 3, 4, CadOperationKind.TransformEntity);

        var options1 = new CadUndoRecordOptions(
            CommandId: "MOVE",
            Label: "Move",
            ActorId: actor,
            Source: CadUndoSource.Tool,
            TimestampUtc: stamp,
            MergeKey: "drag-1",
            MergeWindow: TimeSpan.FromMilliseconds(250));
        var options2 = options1 with { TimestampUtc = stamp.AddMilliseconds(120) };

        service.Record(forward1, inverse1, options1);
        service.Record(forward2, inverse2, options2);

        Assert.Equal(1, service.UndoDepth);
        Assert.True(service.TryPopUndoUnit(out var unit));
        Assert.Equal("drag-1", unit.Metadata.MergeKey);
        Assert.Equal(CadUndoSource.Tool, unit.Metadata.Source);
        Assert.Equal(2, unit.Forward.Operations.Count);
        Assert.Equal(2, unit.Inverse.Operations.Count);
    }

    [Fact]
    public void Record_WithMergeKeyOutsideWindow_DoesNotMerge()
    {
        var service = new CadUndoRedoService();
        var actor = Guid.NewGuid();
        var stamp = DateTimeOffset.UtcNow;
        var forward1 = CreateBatch(actor, 0, 1, CadOperationKind.TransformEntity);
        var inverse1 = CreateBatch(actor, 1, 2, CadOperationKind.TransformEntity);
        var forward2 = CreateBatch(actor, 2, 3, CadOperationKind.TransformEntity);
        var inverse2 = CreateBatch(actor, 3, 4, CadOperationKind.TransformEntity);

        var options1 = new CadUndoRecordOptions(
            CommandId: "MOVE",
            Label: "Move",
            ActorId: actor,
            Source: CadUndoSource.Tool,
            TimestampUtc: stamp,
            MergeKey: "drag-2",
            MergeWindow: TimeSpan.FromMilliseconds(250));
        var options2 = options1 with { TimestampUtc = stamp.AddMilliseconds(300) };

        service.Record(forward1, inverse1, options1);
        service.Record(forward2, inverse2, options2);

        Assert.Equal(2, service.UndoDepth);
    }

    [Fact]
    public void TryPopUndoUnit_WithActorPredicate_PopsMatchingUnitAndKeepsOthers()
    {
        var service = new CadUndoRedoService();
        var actorA = Guid.NewGuid();
        var actorB = Guid.NewGuid();

        service.Record(
            CreateBatch(actorA, 0, 1, CadOperationKind.TransformEntity),
            CreateBatch(actorA, 1, 2, CadOperationKind.TransformEntity),
            new CadUndoRecordOptions(ActorId: actorA, Source: CadUndoSource.CommandLine));
        service.Record(
            CreateBatch(actorB, 2, 3, CadOperationKind.TransformEntity),
            CreateBatch(actorB, 3, 4, CadOperationKind.TransformEntity),
            new CadUndoRecordOptions(ActorId: actorB, Source: CadUndoSource.CommandLine));

        Assert.True(service.TryPopUndoUnit(unit => unit.Metadata.ActorId == actorA, out var popped));
        Assert.Equal(actorA, popped.Metadata.ActorId);
        Assert.Equal(1, service.UndoDepth);
        Assert.True(service.TryPopUndoUnit(out var remaining));
        Assert.Equal(actorB, remaining.Metadata.ActorId);
    }

    [Fact]
    public void Record_UsesAmbientUndoExecutionContext_WhenExplicitOptionsNotProvided()
    {
        var service = new CadUndoRedoService();
        var actor = Guid.NewGuid();

        using (CadUndoExecutionContext.Push(new CadUndoRecordOptions(
                   CommandId: "LINE",
                   Label: "Line",
                   ActorId: actor,
                   Source: CadUndoSource.Tool,
                   MergeKey: "ambient-line")))
        {
            service.Record(
                CreateBatch(actor, 0, 1, CadOperationKind.CreateEntity),
                CreateBatch(actor, 1, 2, CadOperationKind.DeleteEntity));
        }

        Assert.True(service.TryPopUndoUnit(out var unit));
        Assert.Equal("LINE", unit.Metadata.CommandId);
        Assert.Equal("Line", unit.Metadata.Label);
        Assert.Equal(actor, unit.Metadata.ActorId);
        Assert.Equal(CadUndoSource.Tool, unit.Metadata.Source);
        Assert.Equal("ambient-line", unit.Metadata.MergeKey);
    }

    private static CadOperationBatch CreateBatch(Guid actor, long baseVersion, long sequence, CadOperationKind kind)
    {
        var operation = new CadOperation(kind, EntityId: null);
        return CadOperationBatch.Create(actor, baseVersion, sequence, new[] { operation });
    }
}
