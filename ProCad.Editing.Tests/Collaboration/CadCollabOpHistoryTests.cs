using ProCad.Collaboration.Contracts;
using ProCad.Collaboration.History;
using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using Xunit;

namespace ProCad.Editing.Tests.Collaboration;

public sealed class CadCollabOpHistoryTests
{
    [Fact]
    public void TransformRemote_IgnoresDuplicateBatchIds()
    {
        var history = new CadCollabOpHistory();
        var actor = Guid.NewGuid();
        var batchId = Guid.NewGuid();

        var batch = new CadCollabBatch(
            batchId,
            actor,
            0,
            1,
            1,
            DateTimeOffset.UtcNow,
            Array.Empty<CadOperation>());

        history.AppendLocal(batch);

        var result = history.TransformRemote(batch);
        Assert.False(result.RequiresResync);
        Assert.Empty(result.Operations);
    }

    [Fact]
    public void TransformRemote_DropsOlderTransform_WhenNewerLocalTransformExists()
    {
        var history = new CadCollabOpHistory();
        var entityId = CadEntityId.New();
        var localActor = Guid.NewGuid();
        var remoteActor = Guid.NewGuid();

        history.AppendLocal(new CadCollabBatch(
            Guid.NewGuid(),
            localActor,
            0,
            1,
            Lamport: 10,
            DateTimeOffset.UtcNow,
            [CadOperationPayloadCodec.TransformPoint(entityId, new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(1, 1, 0))]));

        var remote = new CadCollabBatch(
            Guid.NewGuid(),
            remoteActor,
            BaseVersion: 0,
            Sequence: 2,
            Lamport: 5,
            DateTimeOffset.UtcNow,
            [CadOperationPayloadCodec.TransformPoint(entityId, new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(2, 2, 0))]);

        var result = history.TransformRemote(remote);

        Assert.False(result.RequiresResync);
        Assert.Empty(result.Operations);
    }

    [Fact]
    public void TransformRemote_KeepsNewerPropertyUpdate_ForSameEntityProperty()
    {
        var history = new CadCollabOpHistory();
        var entityId = CadEntityId.New();
        var localActor = Guid.NewGuid();
        var remoteActor = Guid.NewGuid();

        history.AppendLocal(new CadCollabBatch(
            Guid.NewGuid(),
            localActor,
            0,
            1,
            Lamport: 3,
            DateTimeOffset.UtcNow,
            [CadOperationPayloadCodec.UpdateEntityProperty(entityId, "Layer", "0", "Walls")]));

        var remote = new CadCollabBatch(
            Guid.NewGuid(),
            remoteActor,
            BaseVersion: 0,
            Sequence: 2,
            Lamport: 8,
            DateTimeOffset.UtcNow,
            [CadOperationPayloadCodec.UpdateEntityProperty(entityId, "Layer", "0", "Doors")]);

        var result = history.TransformRemote(remote);

        Assert.False(result.RequiresResync);
        Assert.Single(result.Operations);
        Assert.True(CadOperationPayloadCodec.TryGetUpdateProperty(result.Operations[0], out var propertyName, out var toValue));
        Assert.Equal("Layer", propertyName);
        Assert.Equal("Doors", toValue);
    }

    [Fact]
    public void TransformRemote_DropsTransform_WhenEntityDeletedLocally()
    {
        var history = new CadCollabOpHistory();
        var entityId = CadEntityId.New();
        var localActor = Guid.NewGuid();
        var remoteActor = Guid.NewGuid();

        history.AppendLocal(new CadCollabBatch(
            Guid.NewGuid(),
            localActor,
            0,
            1,
            Lamport: 4,
            DateTimeOffset.UtcNow,
            [CadOperationPayloadCodec.DeletePoint(entityId, new CSMath.XYZ(0, 0, 0))]));

        var remote = new CadCollabBatch(
            Guid.NewGuid(),
            remoteActor,
            BaseVersion: 0,
            Sequence: 2,
            Lamport: 10,
            DateTimeOffset.UtcNow,
            [CadOperationPayloadCodec.TransformPoint(entityId, new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(2, 2, 0))]);

        var result = history.TransformRemote(remote);

        Assert.False(result.RequiresResync);
        Assert.Empty(result.Operations);
    }

    [Fact]
    public void TransformRemote_PreservesPropertyUpdate_WhenTransformAlreadyExists()
    {
        var history = new CadCollabOpHistory();
        var entityId = CadEntityId.New();
        var localActor = Guid.NewGuid();
        var remoteActor = Guid.NewGuid();

        history.AppendLocal(new CadCollabBatch(
            Guid.NewGuid(),
            localActor,
            0,
            1,
            Lamport: 10,
            DateTimeOffset.UtcNow,
            [CadOperationPayloadCodec.TransformPoint(entityId, new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(1, 0, 0))]));

        var remote = new CadCollabBatch(
            Guid.NewGuid(),
            remoteActor,
            BaseVersion: 0,
            Sequence: 2,
            Lamport: 5,
            DateTimeOffset.UtcNow,
            [CadOperationPayloadCodec.UpdateEntityProperty(entityId, "Layer", "0", "A-ANNO")]);

        var result = history.TransformRemote(remote);

        Assert.False(result.RequiresResync);
        Assert.Single(result.Operations);
        Assert.True(CadOperationPayloadCodec.TryGetUpdateProperty(result.Operations[0], out var propertyName, out var toValue));
        Assert.Equal("Layer", propertyName);
        Assert.Equal("A-ANNO", toValue);
    }

    [Fact]
    public void TransformRemote_ConstraintOpsOnDifferentIds_AreNotSuppressed()
    {
        var history = new CadCollabOpHistory();
        var entityId = CadEntityId.New();
        var localActor = Guid.NewGuid();
        var remoteActor = Guid.NewGuid();

        history.AppendLocal(new CadCollabBatch(
            Guid.NewGuid(),
            localActor,
            0,
            1,
            Lamport: 8,
            DateTimeOffset.UtcNow,
            [
                new CadOperation(
                    CadOperationKind.AddConstraint,
                    entityId,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["ConstraintId"] = "C1"
                    })
            ]));

        var remote = new CadCollabBatch(
            Guid.NewGuid(),
            remoteActor,
            BaseVersion: 0,
            Sequence: 2,
            Lamport: 7,
            DateTimeOffset.UtcNow,
            [
                new CadOperation(
                    CadOperationKind.RemoveConstraint,
                    entityId,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["ConstraintId"] = "C2"
                    })
            ]);

        var result = history.TransformRemote(remote);

        Assert.False(result.RequiresResync);
        Assert.Single(result.Operations);
        Assert.Equal(CadOperationKind.RemoveConstraint, result.Operations[0].Kind);
    }
}
