using ProCad.Editing.Operations;
using ProCad.Editing.Selection;
using ProCad.Editing.Sessions;
using ProCad.Editing.Undo;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Commands;

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

        if (!TryResolveJoinTargets(session, context.Arguments, out var targets, out var resolveError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(resolveError!));
        }

        if (targets.Length == 2)
        {
            return ExecuteJoinPair(session, targets[0], targets[1], out _);
        }

        return ExecuteJoinMultiple(session, targets);
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

        if (!CadPolylineEditValidation.TryValidateLinearPolyline(polyline, "JOIN", out var unsupportedError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(unsupportedError!));
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

    private static ValueTask<CadCommandResult> ExecuteJoinPolylines(CadDocumentSession session, LwPolyline first, LwPolyline second)
    {
        if (first.IsClosed || second.IsClosed)
        {
            return ValueTask.FromResult(CadCommandResult.Fail("JOIN currently supports only open polylines."));
        }

        if (!CadPolylineEditValidation.TryValidateLinearPolyline(first, "JOIN", out var firstUnsupportedError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(firstUnsupportedError!));
        }

        if (!CadPolylineEditValidation.TryValidateLinearPolyline(second, "JOIN", out var secondUnsupportedError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(secondUnsupportedError!));
        }

        var firstVertices = CadGeometryTransform.ToVertices(first);
        var secondVertices = CadGeometryTransform.ToVertices(second);
        if (firstVertices.Count < 2 || secondVertices.Count < 2)
        {
            return ValueTask.FromResult(CadCommandResult.Fail("JOIN cannot use polyline with fewer than 2 vertices."));
        }

        if (!TryJoinPolylines(firstVertices, secondVertices, out var mergedVertices, out var joinError))
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
            CadOperationPayloadCodec.TransformLwPolyline(firstId, firstVertices, fromClosed: false, mergedVertices, toClosed: false),
            CadOperationPayloadCodec.DeleteLwPolyline(secondId, secondVertices, isClosed: false)
        };

        var inverseOperations = new CadOperation[]
        {
            CadOperationPayloadCodec.CreateLwPolyline(secondId, secondVertices, isClosed: false).WithSourceProperties(second),
            CadOperationPayloadCodec.TransformLwPolyline(firstId, mergedVertices, fromClosed: false, firstVertices, toClosed: false)
        };

        ApplyWithUndo(session, forwardOperations, inverseOperations);
        session.SetSelection([first], CadSelectionMode.Replace);

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

    private static ValueTask<CadCommandResult> ExecuteJoinPair(
        CadDocumentSession session,
        Entity first,
        Entity second,
        out Entity survivor)
    {
        if (first is Line firstLine && second is Line secondLine)
        {
            survivor = firstLine;
            return ExecuteJoinLines(session, firstLine, secondLine);
        }

        if (first is LwPolyline polyline && second is Line line)
        {
            survivor = polyline;
            return ExecuteJoinPolylineLine(session, polyline, line);
        }

        if (first is Line lineFirst && second is LwPolyline polylineSecond)
        {
            survivor = polylineSecond;
            return ExecuteJoinPolylineLine(session, polylineSecond, lineFirst);
        }

        if (first is LwPolyline firstPolyline && second is LwPolyline secondPolyline)
        {
            survivor = firstPolyline;
            return ExecuteJoinPolylines(session, firstPolyline, secondPolyline);
        }

        survivor = first;
        return ValueTask.FromResult(CadCommandResult.Fail(
            $"JOIN does not support entity types '{first.GetType().Name}' and '{second.GetType().Name}' yet."));
    }

    private static async ValueTask<CadCommandResult> ExecuteJoinMultiple(
        CadDocumentSession session,
        IReadOnlyList<Entity> targets)
    {
        var remaining = new List<Entity>(targets);
        var finalized = new List<Entity>(targets.Count);
        var primary = remaining[0];
        remaining.RemoveAt(0);
        var currentGroupSize = 1;

        var mergedCount = 0;
        var skippedSingleCount = 0;
        var forwardOperations = new List<CadOperation>();
        // Keep all pairwise joins from this command in one undo unit, but
        // never merge across distinct JOIN command executions.
        var mergeKey = $"join-{session.SessionId.Value:N}-{Guid.NewGuid():N}";
        using var undoScope = CadUndoExecutionContext.Push(
            new CadUndoRecordOptions(
                CommandId: "JOIN",
                Label: "Join",
                ActorId: session.SessionId.Value,
                Source: CadUndoSource.CommandLine,
                TimestampUtc: DateTimeOffset.UtcNow,
                MergeKey: mergeKey,
                MergeWindow: TimeSpan.FromMinutes(1)));

        while (true)
        {
            var mergedWithPrimary = false;
            for (var i = 0; i < remaining.Count;)
            {
                var candidate = remaining[i];
                var step = await ExecuteJoinPair(session, primary, candidate, out var survivor).ConfigureAwait(false);
                if (!step.Success)
                {
                    i++;
                    continue;
                }

                if (step.Operations is { Count: > 0 })
                {
                    forwardOperations.AddRange(step.Operations);
                }

                mergedCount++;
                mergedWithPrimary = true;
                currentGroupSize++;
                primary = survivor;
                remaining.RemoveAt(i);
            }

            if (mergedWithPrimary && remaining.Count > 0)
            {
                continue;
            }

            finalized.Add(primary);
            if (currentGroupSize == 1)
            {
                skippedSingleCount++;
            }

            if (remaining.Count == 0)
            {
                break;
            }

            primary = remaining[0];
            remaining.RemoveAt(0);
            currentGroupSize = 1;
        }

        if (mergedCount == 0)
        {
            return CadCommandResult.Fail("JOIN could not merge any selected entities.");
        }

        var selected = finalized.Cast<object?>().ToArray();
        session.SetSelection(selected, CadSelectionMode.Replace);
        if (skippedSingleCount > 0)
        {
            var mergedEntityCount = targets.Count - skippedSingleCount;
            return CadCommandResult.Ok(
                $"Join completed. Merged {mergedEntityCount} entities; skipped {skippedSingleCount}.",
                forwardOperations);
        }

        if (finalized.Count > 1)
        {
            return CadCommandResult.Ok(
                $"Join completed. Created {finalized.Count} joined result entities.",
                forwardOperations);
        }

        return CadCommandResult.Ok("Join completed.", forwardOperations);
    }

    private static bool TryResolveJoinTargets(
        CadDocumentSession session,
        IReadOnlyList<string> args,
        out Entity[] targets,
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
                    targets = Array.Empty<Entity>();
                    error = $"Invalid handle '{token}'.";
                    return false;
                }

                if (!session.EntityIndex.TryGetByHandle(handle, out var entity, out _))
                {
                    targets = Array.Empty<Entity>();
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
        if (unique.Length < 2)
        {
            targets = Array.Empty<Entity>();
            error = "JOIN requires at least two target entities.";
            return false;
        }

        targets = unique;
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

    private static bool TryJoinPolylines(
        IReadOnlyList<XYZ> firstVertices,
        IReadOnlyList<XYZ> secondVertices,
        out IReadOnlyList<XYZ> mergedVertices,
        out string? error)
    {
        mergedVertices = Array.Empty<XYZ>();
        error = null;

        var firstStart = firstVertices[0];
        var firstEnd = firstVertices[^1];
        var secondStart = secondVertices[0];
        var secondEnd = secondVertices[^1];

        if (NearlyEqual(firstEnd, secondStart))
        {
            mergedVertices = MergeForwardForward(firstVertices, secondVertices, skipSecondaryFirst: true);
            return true;
        }

        if (NearlyEqual(firstEnd, secondEnd))
        {
            mergedVertices = MergeForwardReverse(firstVertices, secondVertices, skipSecondaryLast: true);
            return true;
        }

        if (NearlyEqual(firstStart, secondEnd))
        {
            mergedVertices = MergeForwardForward(secondVertices, firstVertices, skipSecondaryFirst: true);
            return true;
        }

        if (NearlyEqual(firstStart, secondStart))
        {
            mergedVertices = MergeReverseForward(secondVertices, firstVertices, skipSecondaryFirst: true);
            return true;
        }

        error = "JOIN polylines require touching endpoints.";
        return false;
    }

    private static IReadOnlyList<XYZ> MergeForwardForward(
        IReadOnlyList<XYZ> primary,
        IReadOnlyList<XYZ> secondary,
        bool skipSecondaryFirst)
    {
        var merged = new XYZ[primary.Count + secondary.Count - 1];
        var index = 0;

        for (var i = 0; i < primary.Count; i++)
        {
            merged[index++] = primary[i];
        }

        var secondaryStart = skipSecondaryFirst ? 1 : 0;
        for (var i = secondaryStart; i < secondary.Count; i++)
        {
            merged[index++] = secondary[i];
        }

        return merged;
    }

    private static IReadOnlyList<XYZ> MergeForwardReverse(
        IReadOnlyList<XYZ> primary,
        IReadOnlyList<XYZ> secondary,
        bool skipSecondaryLast)
    {
        var merged = new XYZ[primary.Count + secondary.Count - 1];
        var index = 0;

        for (var i = 0; i < primary.Count; i++)
        {
            merged[index++] = primary[i];
        }

        var secondaryStart = skipSecondaryLast ? secondary.Count - 2 : secondary.Count - 1;
        for (var i = secondaryStart; i >= 0; i--)
        {
            merged[index++] = secondary[i];
        }

        return merged;
    }

    private static IReadOnlyList<XYZ> MergeReverseForward(
        IReadOnlyList<XYZ> primary,
        IReadOnlyList<XYZ> secondary,
        bool skipSecondaryFirst)
    {
        var merged = new XYZ[primary.Count + secondary.Count - 1];
        var index = 0;

        for (var i = primary.Count - 1; i >= 0; i--)
        {
            merged[index++] = primary[i];
        }

        var secondaryStart = skipSecondaryFirst ? 1 : 0;
        for (var i = secondaryStart; i < secondary.Count; i++)
        {
            merged[index++] = secondary[i];
        }

        return merged;
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
