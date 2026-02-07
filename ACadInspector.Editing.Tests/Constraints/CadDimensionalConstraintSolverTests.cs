using ACadInspector.Editing.Constraints;
using ACadInspector.Editing.EntityIndex;
using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Sessions;
using ACadSharp;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ACadInspector.Editing.Tests.Constraints;

public sealed class CadDimensionalConstraintSolverTests
{
    [Fact]
    public void Distance_DrivingConstraint_AdjustsTargetPoint()
    {
        var solver = new CadGeometricConstraintSolver();
        var index = new CadEntityIndex();
        var service = CreateConstraintService();
        var first = new Point { Location = new XYZ(0, 0, 0) };
        var second = new Point { Location = new XYZ(1, 0, 0) };
        var firstId = index.Register(first);
        var secondId = index.Register(second);
        service.AddConstraint(
            CadConstraintKind.Distance,
            [
                new CadConstraintReference(firstId),
                new CadConstraintReference(secondId)
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Distance"] = "5"
            },
            isDriving: true);

        var result = solver.Solve(service, index, [secondId]);

        Assert.Equal(1, result.AdjustedEntityCount);
        Assert.Equal(5d, Distance(first.Location, second.Location), 3);
    }

    [Fact]
    public void Distance_DrivenConstraint_UpdatesMeasuredParameter()
    {
        var solver = new CadGeometricConstraintSolver();
        var index = new CadEntityIndex();
        var service = CreateConstraintService();
        var first = new Point { Location = new XYZ(0, 0, 0) };
        var second = new Point { Location = new XYZ(3, 4, 0) };
        var firstId = index.Register(first);
        var secondId = index.Register(second);
        var constraint = service.AddConstraint(
            CadConstraintKind.Distance,
            [
                new CadConstraintReference(firstId),
                new CadConstraintReference(secondId)
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Distance"] = "1"
            },
            isDriving: false);

        var result = solver.Solve(service, index, [secondId]);

        Assert.Equal(0, result.AdjustedEntityCount);
        Assert.True(service.TryGetConstraint(constraint.Id, out var updated));
        Assert.Equal("5", updated.Parameters["Distance"]);
    }

    [Fact]
    public void Angle_DrivingConstraint_AlignsTargetToSpecifiedAngle()
    {
        var solver = new CadGeometricConstraintSolver();
        var index = new CadEntityIndex();
        var service = CreateConstraintService();
        var source = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        var target = new Line(new XYZ(5, -2, 0), new XYZ(8, 4, 0));
        var sourceId = index.Register(source);
        var targetId = index.Register(target);
        service.AddConstraint(
            CadConstraintKind.Angle,
            [
                new CadConstraintReference(sourceId),
                new CadConstraintReference(targetId)
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Angle"] = "90"
            },
            isDriving: true);

        solver.Solve(service, index, [targetId]);

        Assert.Equal(target.StartPoint.X, target.EndPoint.X, 3);
    }

    [Fact]
    public void RadiusAndDiameter_DrivingConstraints_AdjustCircleRadius()
    {
        var solver = new CadGeometricConstraintSolver();
        var index = new CadEntityIndex();
        var service = CreateConstraintService();
        var circle = new Circle { Center = new XYZ(0, 0, 0), Radius = 1 };
        var id = index.Register(circle);
        service.AddConstraint(
            CadConstraintKind.Radius,
            [new CadConstraintReference(id)],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Radius"] = "7" },
            isDriving: true);
        solver.Solve(service, index, [id]);
        Assert.Equal(7d, circle.Radius, 4);

        service.AddConstraint(
            CadConstraintKind.Diameter,
            [new CadConstraintReference(id)],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Diameter"] = "9" },
            isDriving: true);
        solver.Solve(service, index, [id]);
        Assert.Equal(4.5d, circle.Radius, 4);
    }

    [Fact]
    public void InvalidDimensionalParameter_ProducesDeterministicDiagnostics()
    {
        var solver = new CadGeometricConstraintSolver();
        var index = new CadEntityIndex();
        var service = CreateConstraintService();
        var first = new Point { Location = new XYZ(0, 0, 0) };
        var second = new Point { Location = new XYZ(1, 0, 0) };
        var firstId = index.Register(first);
        var secondId = index.Register(second);
        service.AddConstraint(
            CadConstraintKind.Distance,
            [
                new CadConstraintReference(firstId),
                new CadConstraintReference(secondId)
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Distance"] = "abc"
            },
            isDriving: true);

        var firstRun = solver.Solve(service, index, [secondId]);
        var secondRun = solver.Solve(service, index, [secondId]);

        Assert.Equal(0, firstRun.AdjustedEntityCount);
        Assert.Equal(0, secondRun.AdjustedEntityCount);
        Assert.NotEmpty(firstRun.Diagnostics);
        Assert.Equal(firstRun.Diagnostics, secondRun.Diagnostics);
    }

    [Fact]
    public void SessionApply_InvokesDimensionalSolver_ForPointTransform()
    {
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));
        var firstId = CadEntityId.New();
        var secondId = CadEntityId.New();
        session.Apply(session.NextBatch(
            session.SessionId.Value,
            [
                CadOperationPayloadCodec.CreatePoint(firstId, new XYZ(0, 0, 0)),
                CadOperationPayloadCodec.CreatePoint(secondId, new XYZ(1, 0, 0))
            ]));
        session.Constraints.AddConstraint(
            CadConstraintKind.Distance,
            [
                new CadConstraintReference(firstId),
                new CadConstraintReference(secondId)
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Distance"] = "8"
            },
            isDriving: true);

        session.Apply(session.NextBatch(
            session.SessionId.Value,
            [
                CadOperationPayloadCodec.TransformPoint(
                    secondId,
                    fromLocation: new XYZ(1, 0, 0),
                    toLocation: new XYZ(2, 3, 0))
            ]));

        Assert.True(session.EntityIndex.TryGetEntity(firstId, out var firstEntity));
        Assert.True(session.EntityIndex.TryGetEntity(secondId, out var secondEntity));
        var first = Assert.IsType<Point>(firstEntity);
        var second = Assert.IsType<Point>(secondEntity);
        Assert.Equal(8d, Distance(first.Location, second.Location), 3);
    }

    private static ICadConstraintService CreateConstraintService()
    {
        return new CadConstraintService(
            new CadConstraintStore(),
            new CadConstraintJsonSnapshotCodec());
    }

    private static double Distance(XYZ first, XYZ second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
