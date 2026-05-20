using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Selection;
using ProCad.Editing.Sessions;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Commands;

public sealed class BreakCadCommand : ICadCommandHandler
{
    private const double LineBreakTolerance = 1e-6;
    private const double LineBreakDistanceToleranceSquared = 1e-8;

    public string Name => "BREAK";
    public IReadOnlyList<string> Aliases => ["BR"];

    public bool CanExecute(CadCommandContext context)
    {
        return context.Session is not null;
    }

    public ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
    {
        if (!CadCommandSessionHelper.TryGetSession(context, out var session, out var error))
        {
            return ValueTask.FromResult(error);
        }

        if (!CadCommandParsing.TryParseBreakArguments(
                context.Arguments,
                out var targetHandle,
                out var firstBreakPoint,
                out var secondBreakPoint,
                out var hasSecondBreakPoint,
                out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        if (!session.EntityIndex.TryGetByHandle(targetHandle, out var target, out _) ||
            target is not Entity targetEntity)
        {
            return ValueTask.FromResult(CadCommandResult.Fail($"BREAK target handle '{targetHandle:X}' was not found."));
        }

        return targetEntity switch
        {
            Line line => hasSecondBreakPoint
                ? ExecuteBreakLineSpan(session, line, firstBreakPoint, secondBreakPoint)
                : ExecuteBreakLine(session, line, firstBreakPoint),
            LwPolyline polyline => hasSecondBreakPoint
                ? ExecuteBreakPolylineSpan(session, polyline, firstBreakPoint, secondBreakPoint)
                : ExecuteBreakPolyline(session, polyline, firstBreakPoint),
            _ => ValueTask.FromResult(CadCommandResult.Fail(
                $"BREAK target handle '{targetHandle:X}' must resolve to a LINE or open LWPOLYLINE."))
        };
    }

    private static ValueTask<CadCommandResult> ExecuteBreakLine(CadDocumentSession session, Line line, XYZ breakPoint)
    {
        if (!TryProjectBreakPoint(line, breakPoint, requireInteriorPoint: true, out var projectedBreakPoint, out _, out var validationError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(validationError!));
        }

        if (!session.EntityIndex.TryGetId(line, out var targetId))
        {
            targetId = session.EntityIndex.Register(line);
        }

        var newId = CadEntityId.New();
        var forwardOperations = new CadOperation[]
        {
            CadOperationPayloadCodec.TransformLine(targetId, line.StartPoint, line.EndPoint, line.StartPoint, projectedBreakPoint),
            CadOperationPayloadCodec.CreateLine(newId, projectedBreakPoint, line.EndPoint).WithSourceProperties(line)
        };

        var inverseOperations = new CadOperation[]
        {
            CadOperationPayloadCodec.DeleteLine(newId, projectedBreakPoint, line.EndPoint),
            CadOperationPayloadCodec.TransformLine(targetId, line.StartPoint, projectedBreakPoint, line.StartPoint, line.EndPoint)
        };

        ApplyWithUndo(session, forwardOperations, inverseOperations);
        SetSelectionToBrokenTargets(session, line, newId);
        return ValueTask.FromResult(CadCommandResult.Ok("Break completed.", forwardOperations));
    }

    private static ValueTask<CadCommandResult> ExecuteBreakLineSpan(
        CadDocumentSession session,
        Line line,
        XYZ firstBreakPoint,
        XYZ secondBreakPoint)
    {
        if (!TryProjectBreakPoint(line, firstBreakPoint, requireInteriorPoint: false, out var projectedFirst, out var firstT, out var firstError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(firstError!));
        }

        if (!TryProjectBreakPoint(line, secondBreakPoint, requireInteriorPoint: false, out var projectedSecond, out var secondT, out var secondError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(secondError!));
        }

        if (Math.Abs(firstT - secondT) <= LineBreakTolerance)
        {
            return ExecuteBreakLine(session, line, projectedFirst);
        }

        var firstRangePoint = firstT <= secondT ? projectedFirst : projectedSecond;
        var secondRangePoint = firstT <= secondT ? projectedSecond : projectedFirst;
        var minT = Math.Min(firstT, secondT);
        var maxT = Math.Max(firstT, secondT);

        if (!session.EntityIndex.TryGetId(line, out var targetId))
        {
            targetId = session.EntityIndex.Register(line);
        }

        var forwardOperations = new List<CadOperation>(2);
        var inverseOperations = new List<CadOperation>(2);
        CadEntityId? createdId = null;

        if (minT <= LineBreakTolerance && maxT >= 1.0 - LineBreakTolerance)
        {
            forwardOperations.Add(CadOperationPayloadCodec.DeleteLine(targetId, line.StartPoint, line.EndPoint));
            inverseOperations.Add(CadOperationPayloadCodec.CreateLine(targetId, line.StartPoint, line.EndPoint).WithSourceProperties(line));
        }
        else if (minT <= LineBreakTolerance)
        {
            forwardOperations.Add(CadOperationPayloadCodec.TransformLine(targetId, line.StartPoint, line.EndPoint, secondRangePoint, line.EndPoint));
            inverseOperations.Add(CadOperationPayloadCodec.TransformLine(targetId, secondRangePoint, line.EndPoint, line.StartPoint, line.EndPoint));
        }
        else if (maxT >= 1.0 - LineBreakTolerance)
        {
            forwardOperations.Add(CadOperationPayloadCodec.TransformLine(targetId, line.StartPoint, line.EndPoint, line.StartPoint, firstRangePoint));
            inverseOperations.Add(CadOperationPayloadCodec.TransformLine(targetId, line.StartPoint, firstRangePoint, line.StartPoint, line.EndPoint));
        }
        else
        {
            createdId = CadEntityId.New();
            forwardOperations.Add(CadOperationPayloadCodec.TransformLine(targetId, line.StartPoint, line.EndPoint, line.StartPoint, firstRangePoint));
            forwardOperations.Add(CadOperationPayloadCodec.CreateLine(createdId.Value, secondRangePoint, line.EndPoint).WithSourceProperties(line));

            inverseOperations.Add(CadOperationPayloadCodec.DeleteLine(createdId.Value, secondRangePoint, line.EndPoint));
            inverseOperations.Add(CadOperationPayloadCodec.TransformLine(targetId, line.StartPoint, firstRangePoint, line.StartPoint, line.EndPoint));
        }

        ApplyWithUndo(session, forwardOperations, inverseOperations);

        if (createdId.HasValue)
        {
            SetSelectionToBrokenTargets(session, line, createdId.Value);
        }
        else if (forwardOperations.Count > 0 &&
                 forwardOperations[0].Kind == CadOperationKind.DeleteEntity)
        {
            session.SetSelection(Array.Empty<object?>(), CadSelectionMode.Replace);
        }
        else
        {
            session.SetSelection([line], CadSelectionMode.Replace);
        }

        return ValueTask.FromResult(CadCommandResult.Ok("Break completed.", forwardOperations));
    }

    private static ValueTask<CadCommandResult> ExecuteBreakPolyline(CadDocumentSession session, LwPolyline polyline, XYZ breakPoint)
    {
        if (polyline.IsClosed)
        {
            return ValueTask.FromResult(CadCommandResult.Fail("BREAK currently supports only open LWPOLYLINE targets."));
        }

        if (!CadPolylineEditValidation.TryValidateLinearPolyline(polyline, "BREAK", out var unsupportedError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(unsupportedError!));
        }

        var vertices = CadGeometryTransform.ToVertices(polyline);
        if (vertices.Count < 2)
        {
            return ValueTask.FromResult(CadCommandResult.Fail("BREAK target polyline must have at least two vertices."));
        }

        if (!TrySplitPolylineAtPoint(vertices, breakPoint, out var firstVertices, out var secondVertices, out var splitError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(splitError!));
        }

        if (!session.EntityIndex.TryGetId(polyline, out var targetId))
        {
            targetId = session.EntityIndex.Register(polyline);
        }

        var newId = CadEntityId.New();
        var forwardOperations = new CadOperation[]
        {
            CadOperationPayloadCodec.TransformLwPolyline(targetId, vertices, fromClosed: false, firstVertices, toClosed: false),
            CadOperationPayloadCodec.CreateLwPolyline(newId, secondVertices, isClosed: false).WithSourceProperties(polyline)
        };

        var inverseOperations = new CadOperation[]
        {
            CadOperationPayloadCodec.DeleteLwPolyline(newId, secondVertices, isClosed: false),
            CadOperationPayloadCodec.TransformLwPolyline(targetId, firstVertices, fromClosed: false, vertices, toClosed: false)
        };

        ApplyWithUndo(session, forwardOperations, inverseOperations);
        SetSelectionToBrokenTargets(session, polyline, newId);
        return ValueTask.FromResult(CadCommandResult.Ok("Break completed.", forwardOperations));
    }

    private static ValueTask<CadCommandResult> ExecuteBreakPolylineSpan(
        CadDocumentSession session,
        LwPolyline polyline,
        XYZ firstBreakPoint,
        XYZ secondBreakPoint)
    {
        if (polyline.IsClosed)
        {
            return ValueTask.FromResult(CadCommandResult.Fail("BREAK currently supports only open LWPOLYLINE targets."));
        }

        if (!CadPolylineEditValidation.TryValidateLinearPolyline(polyline, "BREAK", out var unsupportedError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(unsupportedError!));
        }

        var vertices = CadGeometryTransform.ToVertices(polyline);
        if (vertices.Count < 2)
        {
            return ValueTask.FromResult(CadCommandResult.Fail("BREAK target polyline must have at least two vertices."));
        }

        if (!TryLocatePolylineBreakPoint(vertices, firstBreakPoint, allowEndpoint: true, out var firstLocation, out var firstError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(firstError!));
        }

        if (!TryLocatePolylineBreakPoint(vertices, secondBreakPoint, allowEndpoint: true, out var secondLocation, out var secondError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(secondError!));
        }

        if (AreSamePolylineLocation(firstLocation, secondLocation))
        {
            return ExecuteBreakPolyline(session, polyline, firstLocation.ProjectedPoint);
        }

        if (ComparePolylineLocations(firstLocation, secondLocation) > 0)
        {
            (firstLocation, secondLocation) = (secondLocation, firstLocation);
        }

        var firstVertices = BuildPolylinePrefix(vertices, firstLocation);
        var secondVertices = BuildPolylineSuffix(vertices, secondLocation);

        if (!session.EntityIndex.TryGetId(polyline, out var targetId))
        {
            targetId = session.EntityIndex.Register(polyline);
        }

        var forwardOperations = new List<CadOperation>(2);
        var inverseOperations = new List<CadOperation>(2);
        CadEntityId? createdId = null;

        if (firstVertices.Count >= 2 && secondVertices.Count >= 2)
        {
            createdId = CadEntityId.New();
            forwardOperations.Add(CadOperationPayloadCodec.TransformLwPolyline(targetId, vertices, fromClosed: false, firstVertices, toClosed: false));
            forwardOperations.Add(CadOperationPayloadCodec.CreateLwPolyline(createdId.Value, secondVertices, isClosed: false).WithSourceProperties(polyline));

            inverseOperations.Add(CadOperationPayloadCodec.DeleteLwPolyline(createdId.Value, secondVertices, isClosed: false));
            inverseOperations.Add(CadOperationPayloadCodec.TransformLwPolyline(targetId, firstVertices, fromClosed: false, vertices, toClosed: false));
        }
        else if (firstVertices.Count >= 2)
        {
            forwardOperations.Add(CadOperationPayloadCodec.TransformLwPolyline(targetId, vertices, fromClosed: false, firstVertices, toClosed: false));
            inverseOperations.Add(CadOperationPayloadCodec.TransformLwPolyline(targetId, firstVertices, fromClosed: false, vertices, toClosed: false));
        }
        else if (secondVertices.Count >= 2)
        {
            forwardOperations.Add(CadOperationPayloadCodec.TransformLwPolyline(targetId, vertices, fromClosed: false, secondVertices, toClosed: false));
            inverseOperations.Add(CadOperationPayloadCodec.TransformLwPolyline(targetId, secondVertices, fromClosed: false, vertices, toClosed: false));
        }
        else
        {
            forwardOperations.Add(CadOperationPayloadCodec.DeleteLwPolyline(targetId, vertices, isClosed: false));
            inverseOperations.Add(CadOperationPayloadCodec.CreateLwPolyline(targetId, vertices, isClosed: false).WithSourceProperties(polyline));
        }

        ApplyWithUndo(session, forwardOperations, inverseOperations);

        if (createdId.HasValue)
        {
            SetSelectionToBrokenTargets(session, polyline, createdId.Value);
        }
        else if (forwardOperations.Count > 0 &&
                 forwardOperations[0].Kind == CadOperationKind.DeleteEntity)
        {
            session.SetSelection(Array.Empty<object?>(), CadSelectionMode.Replace);
        }
        else
        {
            session.SetSelection([polyline], CadSelectionMode.Replace);
        }

        return ValueTask.FromResult(CadCommandResult.Ok("Break completed.", forwardOperations));
    }

    private static void ApplyWithUndo(
        CadDocumentSession session,
        IReadOnlyList<CadOperation> forwardOperations,
        IReadOnlyList<CadOperation> inverseOperations)
    {
        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forwardOperations);
        var inverseBatch = session.NextBatch(actorId, inverseOperations);
        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);
    }

    private static void SetSelectionToBrokenTargets(CadDocumentSession session, Entity source, CadEntityId newId)
    {
        var created = session.EntityIndex.TryGetEntity(newId, out var createdEntity)
            ? createdEntity
            : null;
        session.SetSelection(
            created is null ? [source] : [source, created],
            CadSelectionMode.Replace);
    }

    private readonly record struct PolylineBreakLocation(
        int SegmentIndex,
        double SegmentT,
        XYZ ProjectedPoint);

    private static bool TryLocatePolylineBreakPoint(
        IReadOnlyList<XYZ> vertices,
        XYZ point,
        bool allowEndpoint,
        out PolylineBreakLocation location,
        out string? error)
    {
        location = default;
        error = null;

        var segmentIndex = -1;
        var segmentT = 0.0;
        var projected = XYZ.Zero;
        var bestDistanceSquared = double.MaxValue;

        for (var i = 0; i < vertices.Count - 1; i++)
        {
            if (!TryProjectPointToSegment(vertices[i], vertices[i + 1], point, out var candidate, out var t, out var distanceSquared))
            {
                continue;
            }

            if (distanceSquared > 1e-8 || distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            segmentIndex = i;
            segmentT = t;
            projected = candidate;
            bestDistanceSquared = distanceSquared;
        }

        if (segmentIndex < 0)
        {
            error = "Break point is not on the target polyline.";
            return false;
        }

        // Canonicalize vertex picks to the next segment where possible.
        if (segmentT >= 1.0 - LineBreakTolerance && segmentIndex < vertices.Count - 2)
        {
            segmentIndex++;
            segmentT = 0.0;
            projected = vertices[segmentIndex];
        }
        else if (segmentT <= LineBreakTolerance)
        {
            segmentT = 0.0;
            projected = vertices[segmentIndex];
        }
        else if (segmentT >= 1.0 - LineBreakTolerance)
        {
            segmentT = 1.0;
            projected = vertices[^1];
        }

        if (!allowEndpoint && IsPolylineEndpointLocation(vertices.Count, segmentIndex, segmentT))
        {
            error = "Break point must be between polyline endpoints.";
            return false;
        }

        location = new PolylineBreakLocation(segmentIndex, segmentT, projected);
        return true;
    }

    private static bool IsPolylineEndpointLocation(int vertexCount, int segmentIndex, double segmentT)
    {
        return (segmentIndex == 0 && segmentT <= LineBreakTolerance) ||
               (segmentIndex == vertexCount - 2 && segmentT >= 1.0 - LineBreakTolerance);
    }

    private static int ComparePolylineLocations(PolylineBreakLocation first, PolylineBreakLocation second)
    {
        var segmentCompare = first.SegmentIndex.CompareTo(second.SegmentIndex);
        if (segmentCompare != 0)
        {
            return segmentCompare;
        }

        return first.SegmentT.CompareTo(second.SegmentT);
    }

    private static bool AreSamePolylineLocation(PolylineBreakLocation first, PolylineBreakLocation second)
    {
        return ComparePolylineLocations(first, second) == 0 ||
               DistanceSquared(first.ProjectedPoint, second.ProjectedPoint) <= 1e-10;
    }

    private static IReadOnlyList<XYZ> BuildPolylinePrefix(
        IReadOnlyList<XYZ> vertices,
        PolylineBreakLocation location)
    {
        var result = new List<XYZ>(vertices.Count);
        for (var i = 0; i <= location.SegmentIndex; i++)
        {
            AddPointIfDistinct(result, vertices[i]);
        }

        AddPointIfDistinct(result, location.ProjectedPoint);
        return result;
    }

    private static IReadOnlyList<XYZ> BuildPolylineSuffix(
        IReadOnlyList<XYZ> vertices,
        PolylineBreakLocation location)
    {
        var result = new List<XYZ>(vertices.Count);
        AddPointIfDistinct(result, location.ProjectedPoint);
        for (var i = location.SegmentIndex + 1; i < vertices.Count; i++)
        {
            AddPointIfDistinct(result, vertices[i]);
        }

        return result;
    }

    private static void AddPointIfDistinct(List<XYZ> points, XYZ point)
    {
        if (points.Count == 0 ||
            DistanceSquared(points[^1], point) > 1e-10)
        {
            points.Add(point);
        }
    }

    private static bool TrySplitPolylineAtPoint(
        IReadOnlyList<XYZ> vertices,
        XYZ point,
        out IReadOnlyList<XYZ> firstVertices,
        out IReadOnlyList<XYZ> secondVertices,
        out string? error)
    {
        firstVertices = Array.Empty<XYZ>();
        secondVertices = Array.Empty<XYZ>();
        error = null;

        var segmentIndex = -1;
        var segmentT = 0.0;
        var projected = XYZ.Zero;
        var bestDistanceSquared = double.MaxValue;

        for (var i = 0; i < vertices.Count - 1; i++)
        {
            if (!TryProjectPointToSegment(vertices[i], vertices[i + 1], point, out var candidate, out var t, out var distanceSquared))
            {
                continue;
            }

            if (distanceSquared > 1e-8 || distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            segmentIndex = i;
            segmentT = t;
            projected = candidate;
            bestDistanceSquared = distanceSquared;
        }

        if (segmentIndex < 0)
        {
            error = "Break point is not on the target polyline.";
            return false;
        }

        if ((segmentIndex == 0 && segmentT <= 1e-6) ||
            (segmentIndex == vertices.Count - 2 && segmentT >= 1.0 - 1e-6))
        {
            error = "Break point must be between polyline endpoints.";
            return false;
        }

        var left = vertices[segmentIndex];
        var right = vertices[segmentIndex + 1];
        var useLeft = DistanceSquared(left, projected) <= 1e-10;
        var useRight = DistanceSquared(right, projected) <= 1e-10;

        var first = new List<XYZ>(vertices.Count);
        for (var i = 0; i <= segmentIndex; i++)
        {
            first.Add(vertices[i]);
        }

        if (!useLeft)
        {
            first.Add(projected);
        }

        var second = new List<XYZ>(vertices.Count);
        if (!useRight)
        {
            second.Add(projected);
        }

        for (var i = segmentIndex + 1; i < vertices.Count; i++)
        {
            second.Add(vertices[i]);
        }

        if (first.Count < 2 || second.Count < 2)
        {
            error = "Break point must be between polyline endpoints.";
            return false;
        }

        firstVertices = first;
        secondVertices = second;
        return true;
    }

    private static bool TryProjectPointToSegment(
        XYZ start,
        XYZ end,
        XYZ point,
        out XYZ projected,
        out double t,
        out double distanceSquared)
    {
        projected = XYZ.Zero;
        t = 0.0;
        distanceSquared = double.MaxValue;

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var dz = end.Z - start.Z;
        // LWPOLYLINE break works in 2D XY space; preserve segment elevation in projected result.
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= 1e-12)
        {
            return false;
        }

        var px = point.X - start.X;
        var py = point.Y - start.Y;
        t = ((px * dx) + (py * dy)) / lengthSquared;
        if (t < -LineBreakTolerance || t > 1.0 + LineBreakTolerance)
        {
            return false;
        }

        t = Math.Clamp(t, 0.0, 1.0);
        projected = new XYZ(
            start.X + dx * t,
            start.Y + dy * t,
            start.Z + dz * t);
        distanceSquared = DistanceSquared2D(projected, point);
        return true;
    }

    private static double DistanceSquared2D(XYZ a, XYZ b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    private static double DistanceSquared(XYZ a, XYZ b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private static bool TryProjectBreakPoint(
        Line line,
        XYZ point,
        bool requireInteriorPoint,
        out XYZ projected,
        out double t,
        out string? error)
    {
        error = null;
        projected = XYZ.Zero;
        t = 0.0;

        var dx = line.EndPoint.X - line.StartPoint.X;
        var dy = line.EndPoint.Y - line.StartPoint.Y;
        var dz = line.EndPoint.Z - line.StartPoint.Z;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= 1e-10)
        {
            error = "Cannot break a zero-length line.";
            return false;
        }

        var px = point.X - line.StartPoint.X;
        var py = point.Y - line.StartPoint.Y;
        var cross = Math.Abs((dx * py) - (dy * px));
        if (cross > 1e-6 * Math.Sqrt(lengthSquared))
        {
            error = "Break point is not on the target line.";
            return false;
        }

        t = ((px * dx) + (py * dy)) / lengthSquared;
        if (t < -LineBreakTolerance || t > 1.0 + LineBreakTolerance)
        {
            error = "Break point is not on the target line.";
            return false;
        }

        t = Math.Clamp(t, 0.0, 1.0);
        var projectedX = line.StartPoint.X + dx * t;
        var projectedY = line.StartPoint.Y + dy * t;
        var deltaX = projectedX - point.X;
        var deltaY = projectedY - point.Y;
        if ((deltaX * deltaX) + (deltaY * deltaY) > LineBreakDistanceToleranceSquared)
        {
            error = "Break point is not on the target line.";
            return false;
        }

        if (requireInteriorPoint &&
            (t <= LineBreakTolerance || t >= 1.0 - LineBreakTolerance))
        {
            error = "Break point must be between line endpoints.";
            return false;
        }

        projected = new XYZ(
            projectedX,
            projectedY,
            line.StartPoint.Z + dz * t);
        return true;
    }
}
