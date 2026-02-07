using ACadInspector.Editing.Constraints;
using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Sessions;
using ACadSharp;
using Xunit;

namespace ACadInspector.Editing.Tests.Constraints;

public sealed class CadConstraintFoundationTests
{
    [Fact]
    public void AddConstraint_StoresConstraintAndGraphAdjacency()
    {
        var service = CreateService();
        var first = CadEntityId.New();
        var second = CadEntityId.New();

        var created = service.AddConstraint(
            CadConstraintKind.Parallel,
            [
                new CadConstraintReference(first, Role: "First"),
                new CadConstraintReference(second, Role: "Second")
            ],
            parameters: new Dictionary<string, string> { ["Weight"] = "1.0" });

        Assert.Equal(CadConstraintKind.Parallel, created.Kind);
        Assert.Single(service.GetConstraints());
        Assert.Contains(created.Id, service.Graph.GetConstraints(first));
        Assert.Contains(created.Id, service.Graph.GetConstraints(second));
    }

    [Fact]
    public void RemoveConstraint_RemovesGraphEdges()
    {
        var service = CreateService();
        var entity = CadEntityId.New();
        var created = service.AddConstraint(
            CadConstraintKind.Fixed,
            [new CadConstraintReference(entity)]);

        var removed = service.TryRemoveConstraint(created.Id, out var removedConstraint);

        Assert.True(removed);
        Assert.Equal(created.Id, removedConstraint.Id);
        Assert.Empty(service.GetConstraints());
        Assert.Empty(service.Graph.GetConstraints(entity));
    }

    [Fact]
    public void StoreSnapshot_Load_RestoresConstraintGraphIntegrity()
    {
        var sourceStore = new CadConstraintStore();
        var a = CadEntityId.New();
        var b = CadEntityId.New();
        var c = CadEntityId.New();

        Assert.True(sourceStore.TryAdd(new CadConstraint(
            CadConstraintId.New(),
            CadConstraintKind.Horizontal,
            [new CadConstraintReference(a), new CadConstraintReference(b)],
            new Dictionary<string, string>(),
            IsDriving: true,
            CreatedAtUtc: DateTimeOffset.UtcNow)));
        Assert.True(sourceStore.TryAdd(new CadConstraint(
            CadConstraintId.New(),
            CadConstraintKind.Distance,
            [new CadConstraintReference(b), new CadConstraintReference(c)],
            new Dictionary<string, string> { ["Distance"] = "25.5" },
            IsDriving: true,
            CreatedAtUtc: DateTimeOffset.UtcNow)));

        var snapshot = sourceStore.CreateSnapshot();
        var targetStore = new CadConstraintStore();
        targetStore.LoadSnapshot(snapshot);

        Assert.Equal(2, targetStore.GetAll().Count);
        Assert.Single(targetStore.Graph.GetConstraints(a));
        Assert.Equal(2, targetStore.Graph.GetConstraints(b).Count);
        Assert.Single(targetStore.Graph.GetConstraints(c));
    }

    [Fact]
    public void JsonCodec_RoundTripsSnapshot()
    {
        var codec = new CadConstraintJsonSnapshotCodec();
        var source = new CadConstraintSnapshot(
            SchemaVersion: 1,
            SavedAtUtc: DateTimeOffset.UtcNow,
            Constraints:
            [
                new CadConstraint(
                    CadConstraintId.New(),
                    CadConstraintKind.Angle,
                    [new CadConstraintReference(CadEntityId.New(), Role: "Base")],
                    new Dictionary<string, string> { ["Angle"] = "45" },
                    IsDriving: false,
                    CreatedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var payload = codec.Serialize(source);
        var ok = codec.TryDeserialize(payload, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Single(restored.Constraints);
        Assert.Equal(CadConstraintKind.Angle, restored.Constraints[0].Kind);
        Assert.Equal("45", restored.Constraints[0].Parameters["Angle"]);
    }

    [Fact]
    public void ServicePayload_ExportImport_RestoresConstraints()
    {
        var source = CreateService();
        source.AddConstraint(
            CadConstraintKind.Coincident,
            [new CadConstraintReference(CadEntityId.New()), new CadConstraintReference(CadEntityId.New())]);
        var payload = source.ExportPayload();

        var target = CreateService();
        var imported = target.ImportPayload(payload);

        Assert.True(imported);
        Assert.Single(target.GetConstraints());
    }

    [Fact]
    public void SessionConstraintPayload_CanRoundTripBetweenSessions()
    {
        var factory = new CadEditorSessionFactory();
        var source = Assert.IsType<CadDocumentSession>(factory.Create(new CadDocument()));
        var target = Assert.IsType<CadDocumentSession>(factory.Create(new CadDocument()));
        source.Constraints.AddConstraint(
            CadConstraintKind.Vertical,
            [new CadConstraintReference(CadEntityId.New())]);

        var payload = source.ExportConstraintPayload();
        var restored = target.ImportConstraintPayload(payload);

        Assert.True(restored);
        Assert.Single(target.Constraints.GetConstraints());
    }

    private static ICadConstraintService CreateService()
    {
        return new CadConstraintService(
            new CadConstraintStore(),
            new CadConstraintJsonSnapshotCodec());
    }
}
