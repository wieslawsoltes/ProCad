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

public sealed class CadGeometricConstraintSolverTests
{
    [Fact]
    public void Coincident_MovesTargetAnchorToSource()
    {
        var solver = new CadGeometricConstraintSolver();
        var index = new CadEntityIndex();
        var service = CreateConstraintService();
        var source = new Point { Location = new XYZ(5, 7, 0) };
        var target = new Point { Location = new XYZ(-3, 2, 0) };
        var sourceId = index.Register(source);
        var targetId = index.Register(target);
        service.AddConstraint(
            CadConstraintKind.Coincident,
            [
                new CadConstraintReference(sourceId),
                new CadConstraintReference(targetId)
            ]);

        var result = solver.Solve(service, index, [targetId]);

        Assert.Equal(1, result.AdjustedEntityCount);
        Assert.Equal(source.Location.X, target.Location.X, 4);
        Assert.Equal(source.Location.Y, target.Location.Y, 4);
    }

    [Fact]
    public void Concentric_AlignsCircleCenters()
    {
        var solver = new CadGeometricConstraintSolver();
        var index = new CadEntityIndex();
        var service = CreateConstraintService();
        var source = new Circle { Center = new XYZ(3, 4, 0), Radius = 2 };
        var target = new Circle { Center = new XYZ(10, -2, 0), Radius = 6 };
        var sourceId = index.Register(source);
        var targetId = index.Register(target);
        service.AddConstraint(
            CadConstraintKind.Concentric,
            [
                new CadConstraintReference(sourceId),
                new CadConstraintReference(targetId)
            ]);

        solver.Solve(service, index, [targetId]);

        Assert.Equal(source.Center.X, target.Center.X, 4);
        Assert.Equal(source.Center.Y, target.Center.Y, 4);
    }

    [Fact]
    public void Collinear_ProjectsTargetLineToSourceAxis()
    {
        var solver = new CadGeometricConstraintSolver();
        var index = new CadEntityIndex();
        var service = CreateConstraintService();
        var source = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        var target = new Line(new XYZ(2, 2, 0), new XYZ(6, 6, 0));
        var sourceId = index.Register(source);
        var targetId = index.Register(target);
        service.AddConstraint(
            CadConstraintKind.Collinear,
            [
                new CadConstraintReference(sourceId),
                new CadConstraintReference(targetId)
            ]);

        solver.Solve(service, index, [targetId]);

        Assert.Equal(0d, target.StartPoint.Y, 4);
        Assert.Equal(0d, target.EndPoint.Y, 4);
    }

    [Fact]
    public void Parallel_AlignsTargetDirection()
    {
        var solver = new CadGeometricConstraintSolver();
        var index = new CadEntityIndex();
        var service = CreateConstraintService();
        var source = new Line(new XYZ(0, 0, 0), new XYZ(0, 8, 0));
        var target = new Line(new XYZ(5, 5, 0), new XYZ(8, 7, 0));
        var sourceId = index.Register(source);
        var targetId = index.Register(target);
        service.AddConstraint(
            CadConstraintKind.Parallel,
            [
                new CadConstraintReference(sourceId),
                new CadConstraintReference(targetId)
            ]);

        solver.Solve(service, index, [targetId]);

        Assert.Equal(target.StartPoint.X, target.EndPoint.X, 4);
    }

    [Fact]
    public void Perpendicular_AlignsTargetDirection()
    {
        var solver = new CadGeometricConstraintSolver();
        var index = new CadEntityIndex();
        var service = CreateConstraintService();
        var source = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        var target = new Line(new XYZ(4, 1, 0), new XYZ(8, 3, 0));
        var sourceId = index.Register(source);
        var targetId = index.Register(target);
        service.AddConstraint(
            CadConstraintKind.Perpendicular,
            [
                new CadConstraintReference(sourceId),
                new CadConstraintReference(targetId)
            ]);

        solver.Solve(service, index, [targetId]);

        Assert.Equal(target.StartPoint.X, target.EndPoint.X, 4);
    }

    [Fact]
    public void Horizontal_And_Vertical_ClampLineAxis()
    {
        var solver = new CadGeometricConstraintSolver();
        var index = new CadEntityIndex();
        var service = CreateConstraintService();
        var horizontal = new Line(new XYZ(0, 0, 0), new XYZ(7, 3, 0));
        var vertical = new Line(new XYZ(1, 1, 0), new XYZ(4, 9, 0));
        var horizontalId = index.Register(horizontal);
        var verticalId = index.Register(vertical);
        service.AddConstraint(CadConstraintKind.Horizontal, [new CadConstraintReference(horizontalId)]);
        service.AddConstraint(CadConstraintKind.Vertical, [new CadConstraintReference(verticalId)]);

        solver.Solve(service, index, [horizontalId, verticalId]);

        Assert.Equal(horizontal.StartPoint.Y, horizontal.EndPoint.Y, 4);
        Assert.Equal(vertical.StartPoint.X, vertical.EndPoint.X, 4);
    }

    [Fact]
    public void Tangent_AlignsLineDistanceToCircleRadius()
    {
        var solver = new CadGeometricConstraintSolver();
        var index = new CadEntityIndex();
        var service = CreateConstraintService();
        var circle = new Circle { Center = new XYZ(0, 0, 0), Radius = 5 };
        var line = new Line(new XYZ(-4, 1, 0), new XYZ(6, 1, 0));
        var circleId = index.Register(circle);
        var lineId = index.Register(line);
        service.AddConstraint(
            CadConstraintKind.Tangent,
            [
                new CadConstraintReference(circleId),
                new CadConstraintReference(lineId)
            ]);

        solver.Solve(service, index, [lineId]);

        var distance = DistancePointToLine(new XY(0, 0), line.StartPoint, line.EndPoint);
        Assert.InRange(distance, 4.95, 5.05);
    }

    [Fact]
    public void Equal_MatchesLineLength_AndCircleRadius()
    {
        var solver = new CadGeometricConstraintSolver();
        var index = new CadEntityIndex();
        var service = CreateConstraintService();
        var sourceLine = new Line(new XYZ(0, 0, 0), new XYZ(12, 0, 0));
        var targetLine = new Line(new XYZ(1, 1, 0), new XYZ(5, 1, 0));
        var sourceCircle = new Circle { Center = new XYZ(0, 0, 0), Radius = 3 };
        var targetCircle = new Circle { Center = new XYZ(10, 0, 0), Radius = 8 };
        var sourceLineId = index.Register(sourceLine);
        var targetLineId = index.Register(targetLine);
        var sourceCircleId = index.Register(sourceCircle);
        var targetCircleId = index.Register(targetCircle);
        service.AddConstraint(
            CadConstraintKind.Equal,
            [
                new CadConstraintReference(sourceLineId),
                new CadConstraintReference(targetLineId)
            ]);
        service.AddConstraint(
            CadConstraintKind.Equal,
            [
                new CadConstraintReference(sourceCircleId),
                new CadConstraintReference(targetCircleId)
            ]);

        solver.Solve(service, index, [targetLineId, targetCircleId]);

        Assert.Equal(12d, LineLength(targetLine), 3);
        Assert.Equal(sourceCircle.Radius, targetCircle.Radius, 6);
    }

    [Fact]
    public void Symmetric_MirrorsPointAcrossAxisLine()
    {
        var solver = new CadGeometricConstraintSolver();
        var index = new CadEntityIndex();
        var service = CreateConstraintService();
        var axis = new Line(new XYZ(0, -10, 0), new XYZ(0, 10, 0));
        var point = new Point { Location = new XYZ(4, 2, 0) };
        var axisId = index.Register(axis);
        var pointId = index.Register(point);
        service.AddConstraint(
            CadConstraintKind.Symmetric,
            [
                new CadConstraintReference(axisId),
                new CadConstraintReference(pointId)
            ]);

        solver.Solve(service, index, [pointId]);

        Assert.Equal(-4d, point.Location.X, 4);
        Assert.Equal(2d, point.Location.Y, 4);
    }

    [Fact]
    public void Fixed_RestoresOriginalGeometryAfterTransform()
    {
        var solver = new CadGeometricConstraintSolver();
        var index = new CadEntityIndex();
        var service = CreateConstraintService();
        var line = new Line(new XYZ(0, 0, 0), new XYZ(5, 0, 0));
        var lineId = index.Register(line);
        service.AddConstraint(CadConstraintKind.Fixed, [new CadConstraintReference(lineId)]);

        solver.Solve(service, index, [lineId]); // capture state
        line.StartPoint = new XYZ(10, 10, 0);
        line.EndPoint = new XYZ(20, 20, 0);
        solver.Solve(service, index, [lineId]); // restore state

        Assert.Equal(0d, line.StartPoint.X, 4);
        Assert.Equal(0d, line.StartPoint.Y, 4);
        Assert.Equal(5d, line.EndPoint.X, 4);
        Assert.Equal(0d, line.EndPoint.Y, 4);
    }

    [Fact]
    public void SessionApply_InvokesSolver_ForTransformBatch()
    {
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));
        var sourceId = CadEntityId.New();
        var targetId = CadEntityId.New();
        var sourceCreate = CadOperationPayloadCodec.CreateLine(sourceId, new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        var targetCreate = CadOperationPayloadCodec.CreateLine(targetId, new XYZ(0, 2, 0), new XYZ(4, 5, 0));
        session.Apply(session.NextBatch(session.SessionId.Value, [sourceCreate, targetCreate]));
        session.Constraints.AddConstraint(
            CadConstraintKind.Parallel,
            [
                new CadConstraintReference(sourceId),
                new CadConstraintReference(targetId)
            ]);

        var transform = CadOperationPayloadCodec.TransformLine(
            targetId,
            fromStart: new XYZ(0, 2, 0),
            fromEnd: new XYZ(4, 5, 0),
            toStart: new XYZ(0, 2, 0),
            toEnd: new XYZ(2, 7, 0));
        session.Apply(session.NextBatch(session.SessionId.Value, [transform]));

        Assert.True(session.EntityIndex.TryGetEntity(targetId, out var entity));
        var line = Assert.IsType<Line>(entity);
        Assert.Equal(line.StartPoint.Y, line.EndPoint.Y, 3);
    }

    [Fact]
    public void SessionUndoRedo_PreservesConstraintSolution()
    {
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));
        var sourceId = CadEntityId.New();
        var targetId = CadEntityId.New();
        var sourceCreate = CadOperationPayloadCodec.CreateLine(sourceId, new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        var targetCreate = CadOperationPayloadCodec.CreateLine(targetId, new XYZ(0, 2, 0), new XYZ(4, 5, 0));
        session.Apply(session.NextBatch(session.SessionId.Value, [sourceCreate, targetCreate]));
        session.Constraints.AddConstraint(
            CadConstraintKind.Parallel,
            [
                new CadConstraintReference(sourceId),
                new CadConstraintReference(targetId)
            ]);

        var forwardOperation = CadOperationPayloadCodec.TransformLine(
            targetId,
            fromStart: new XYZ(0, 2, 0),
            fromEnd: new XYZ(4, 5, 0),
            toStart: new XYZ(3, 6, 0),
            toEnd: new XYZ(7, 10, 0));
        var inverseOperation = CadOperationPayloadCodec.TransformLine(
            targetId,
            fromStart: new XYZ(3, 6, 0),
            fromEnd: new XYZ(7, 10, 0),
            toStart: new XYZ(0, 2, 0),
            toEnd: new XYZ(4, 5, 0));
        var forward = session.NextBatch(session.SessionId.Value, [forwardOperation]);
        var inverse = session.NextBatch(session.SessionId.Value, [inverseOperation]);
        session.Apply(forward);
        session.UndoRedo.Record(forward, inverse);

        Assert.True(session.EntityIndex.TryGetEntity(targetId, out var targetEntity));
        var targetLine = Assert.IsType<Line>(targetEntity);
        Assert.Equal(targetLine.StartPoint.Y, targetLine.EndPoint.Y, 3);

        Assert.True(session.TryUndo(session.SessionId.Value, out _));
        Assert.Equal(targetLine.StartPoint.Y, targetLine.EndPoint.Y, 3);

        Assert.True(session.TryRedo(session.SessionId.Value, out _));
        Assert.Equal(targetLine.StartPoint.Y, targetLine.EndPoint.Y, 3);
    }

    private static ICadConstraintService CreateConstraintService()
    {
        return new CadConstraintService(
            new CadConstraintStore(),
            new CadConstraintJsonSnapshotCodec());
    }

    private static double LineLength(Line line)
    {
        var dx = line.EndPoint.X - line.StartPoint.X;
        var dy = line.EndPoint.Y - line.StartPoint.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double DistancePointToLine(XY point, XYZ lineStart, XYZ lineEnd)
    {
        var x0 = point.X;
        var y0 = point.Y;
        var x1 = lineStart.X;
        var y1 = lineStart.Y;
        var x2 = lineEnd.X;
        var y2 = lineEnd.Y;
        var denominator = Math.Sqrt(Math.Pow(y2 - y1, 2) + Math.Pow(x2 - x1, 2));
        if (denominator < 1e-9)
        {
            return Math.Sqrt(Math.Pow(x0 - x1, 2) + Math.Pow(y0 - y1, 2));
        }

        var numerator = Math.Abs((y2 - y1) * x0 - (x2 - x1) * y0 + x2 * y1 - y2 * x1);
        return numerator / denominator;
    }
}
