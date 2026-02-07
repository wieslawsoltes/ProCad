using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;
using ACadInspector.Editing.Sessions;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Editing.Commands;

public sealed class JoinCadCommand : ICadCommandHandler
{
    public string Name => "JOIN";
    public IReadOnlyList<string> Aliases => ["J"];

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

        if (!TryResolveJoinTargets(session, context.Arguments, out var first, out var second, out var resolveError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(resolveError!));
        }

        if (first is Line firstLine && second is Line secondLine)
        {
            return ExecuteJoinLines(session, firstLine, secondLine);
        }

        if (first is LwPolyline polyline && second is Line line)
        {
            return ExecuteJoinPolylineLine(session, polyline, line);
        }

        if (first is Line lineFirst && second is LwPolyline polylineSecond)
        {
            return ExecuteJoinPolylineLine(session, polylineSecond, lineFirst);
        }

        return ValueTask.FromResult(CadCommandResult.Fail(
            $"JOIN does not support entity types '{first.GetType().Name}' and '{second.GetType().Name}' yet."));
    }

    private static ValueTask<CadCommandResult> ExecuteJoinLines(CadDocumentSession session, Line first, Line second)
    {
        if (!TryJoinLines(first, second, out var mergedStart, out var mergedEnd, out var joinError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(joinError!));
        }

        if (!session.EntityIndex.TryGetId(first, out var firstId))
        {
            firstId = session.EntityIndex.Register(first);
        }

        if (!session.EntityIndex.TryGetId(second, out var secondId))
        {
            secondId = session.EntityIndex.Register(second);
        }

        var forwardOperations = new CadOperation[]
        {
            CadOperationPayloadCodec.TransformLine(firstId, first.StartPoint, first.EndPoint, mergedStart, mergedEnd),
            CadOperationPayloadCodec.DeleteLine(secondId, second.StartPoint, second.EndPoint)
        };

        var inverseOperations = new CadOperation[]
        {
            CadOperationPayloadCodec.CreateLine(secondId, second.StartPoint, second.EndPoint).WithSourceProperties(second),
            CadOperationPayloadCodec.TransformLine(firstId, mergedStart, mergedEnd, first.StartPoint, first.EndPoint)
        };

        ApplyWithUndo(session, forwardOperations, inverseOperations);
        session.SetSelection([first], CadSelectionMode.Replace);

        return ValueTask.FromResult(CadCommandResult.Ok("Join completed.", forwardOperations));
    }

    private static ValueTask<CadCommandResult> ExecuteJoinPolylineLine(CadDocumentSession session, LwPolyline polyline, Line line)
    {
        if (polyline.IsClosed)
        {
            return ValueTask.FromResult(CadCommandResult.Fail("JOIN currently supports only open polylines."));
        }

        var oldVertices = CadGeometryTransform.ToVertices(polyline);
        if (oldVertices.Count < 2)
        {
            return ValueTask.FromResult(CadCommandResult.Fail("JOIN cannot use polyline with fewer than 2 vertices."));
        }

        if (!TryJoinPolylineAndLine(oldVertices, line, out var newVertices, out var joinError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(joinError!));
        }

        if (!session.EntityIndex.TryGetId(polyline, out var polylineId))
        {
            polylineId = session.EntityIndex.Register(polyline);
        }

        if (!session.EntityIndex.TryGetId(line, out var lineId))
        {
            lineId = session.EntityIndex.Register(line);
        }

        var forwardOperations = new CadOperation[]
        {
            CadOperationPayloadCodec.TransformLwPolyline(polylineId, oldVertices, fromClosed: false, newVertices, toClosed: false),
            CadOperationPayloadCodec.DeleteLine(lineId, line.StartPoint, line.EndPoint)
        };

        var inverseOperations = new CadOperation[]
        {
            CadOperationPayloadCodec.CreateLine(lineId, line.StartPoint, line.EndPoint).WithSourceProperties(line),
            CadOperationPayloadCodec.TransformLwPolyline(polylineId, newVertices, fromClosed: false, oldVertices, toClosed: false)
        };

        ApplyWithUndo(session, forwardOperations, inverseOperations);
        session.SetSelection([polyline], CadSelectionMode.Replace);

        return ValueTask.FromResult(CadCommandResult.Ok("Join completed.", forwardOperations));
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

    private static bool TryResolveJoinTargets(
        CadDocumentSession session,
        IReadOnlyList<string> args,
        out Entity first,
        out Entity second,
        out string? error)
    {
        error = null;
        var resolved = new List<Entity>();

        if (args.Count > 0)
        {
            foreach (var token in args)
            {
                if (!CadCommandParsing.TryParseHandle(token, out var handle))
                {
                    first = null!;
                    second = null!;
                    error = $"Invalid handle '{token}'.";
                    return false;
                }

                if (!session.EntityIndex.TryGetByHandle(handle, out var entity, out _))
                {
                    first = null!;
                    second = null!;
                    error = $"Entity handle '{token}' was not found.";
                    return false;
                }

                if (entity is Entity cadEntity)
                {
                    resolved.Add(cadEntity);
                }
            }
        }
        else
        {
            foreach (var item in session.SelectionSet.Items)
            {
                if (item is Entity entity)
                {
                    resolved.Add(entity);
                }
            }
        }

        var unique = resolved.Distinct().ToArray();
        if (unique.Length != 2)
        {
            first = null!;
            second = null!;
            error = "JOIN currently requires exactly two target entities.";
            return false;
        }

        first = unique[0];
        second = unique[1];
        return true;
    }

    private static bool TryJoinLines(
        Line first,
        Line second,
        out XYZ mergedStart,
        out XYZ mergedEnd,
        out string? error)
    {
        mergedStart = XYZ.Zero;
        mergedEnd = XYZ.Zero;
        error = null;

        if (!TryResolveSharedEndpoint(first, second, out var firstOther, out var secondOther))
        {
            error = "JOIN lines require a shared endpoint.";
            return false;
        }

        if (!AreCollinear(first.StartPoint, first.EndPoint, secondOther))
        {
            error = "JOIN lines require collinear geometry.";
            return false;
        }

        mergedStart = firstOther;
        mergedEnd = secondOther;
        return true;
    }

    private static bool TryResolveSharedEndpoint(
        Line first,
        Line second,
        out XYZ firstOther,
        out XYZ secondOther)
    {
        firstOther = XYZ.Zero;
        secondOther = XYZ.Zero;

        if (NearlyEqual(first.StartPoint, second.StartPoint))
        {
            firstOther = first.EndPoint;
            secondOther = second.EndPoint;
            return true;
        }

        if (NearlyEqual(first.StartPoint, second.EndPoint))
        {
            firstOther = first.EndPoint;
            secondOther = second.StartPoint;
            return true;
        }

        if (NearlyEqual(first.EndPoint, second.StartPoint))
        {
            firstOther = first.StartPoint;
            secondOther = second.EndPoint;
            return true;
        }

        if (NearlyEqual(first.EndPoint, second.EndPoint))
        {
            firstOther = first.StartPoint;
            secondOther = second.StartPoint;
            return true;
        }

        return false;
    }

    private static bool TryJoinPolylineAndLine(
        IReadOnlyList<XYZ> vertices,
        Line line,
        out IReadOnlyList<XYZ> newVertices,
        out string? error)
    {
        newVertices = Array.Empty<XYZ>();
        error = null;

        var start = vertices[0];
        var end = vertices[^1];
        var lineStart = line.StartPoint;
        var lineEnd = line.EndPoint;

        if (NearlyEqual(lineStart, start))
        {
            newVertices = [lineEnd, .. vertices];
            return true;
        }

        if (NearlyEqual(lineEnd, start))
        {
            newVertices = [lineStart, .. vertices];
            return true;
        }

        if (NearlyEqual(lineStart, end))
        {
            newVertices = [.. vertices, lineEnd];
            return true;
        }

        if (NearlyEqual(lineEnd, end))
        {
            newVertices = [.. vertices, lineStart];
            return true;
        }

        error = "JOIN line/polyline requires touching endpoints.";
        return false;
    }

    private static bool NearlyEqual(XYZ a, XYZ b)
    {
        return DistanceSquared(a, b) <= 1e-10;
    }

    private static bool AreCollinear(XYZ a, XYZ b, XYZ p)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var px = p.X - a.X;
        var py = p.Y - a.Y;
        var cross = Math.Abs((dx * py) - (dy * px));
        return cross <= 1e-6 * Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double DistanceSquared(XYZ a, XYZ b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }
}
