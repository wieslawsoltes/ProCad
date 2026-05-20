using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Selection;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Commands;

public sealed class ExplodeCadCommand : ICadCommandHandler
{
    public string Name => "EXPLODE";
    public IReadOnlyList<string> Aliases => ["X"];

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

        if (!CadCommandTargetResolver.TryResolve(session, context.Arguments, out var targets, out var targetError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(targetError!));
        }

        var forward = new List<CadOperation>();
        var inverseChunks = new List<IReadOnlyList<CadOperation>>(targets.Count);
        var createdIds = new List<CadEntityId>();

        foreach (var target in targets)
        {
            if (target is not Entity sourceEntity)
            {
                return ValueTask.FromResult(CadCommandResult.Fail(
                    $"EXPLODE does not support entity type '{target.GetType().Name}' yet."));
            }

            if (!session.EntityIndex.TryGetId(sourceEntity, out var sourceId))
            {
                sourceId = session.EntityIndex.Register(sourceEntity);
            }

            var targetForward = new List<CadOperation>();
            var targetInverse = new List<CadOperation>();
            var targetCreated = new List<(CadEntityId Id, XYZ Start, XYZ End)>();

            switch (sourceEntity)
            {
                case LwPolyline polyline:
                {
                    var vertices = CadGeometryTransform.ToVertices(polyline);
                    if (!TryAppendExplodedSegments(vertices, polyline.IsClosed, sourceEntity, targetForward, targetCreated, createdIds, out var polylineError))
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail(polylineError!));
                    }

                    targetForward.Add(CadOperationPayloadCodec.DeleteLwPolyline(sourceId, vertices, polyline.IsClosed));
                    targetInverse.Add(CadOperationPayloadCodec.CreateLwPolyline(sourceId, vertices, polyline.IsClosed).WithSourceProperties(sourceEntity));
                    break;
                }
                case Arc arc:
                {
                    var sweep = CadCurveSampling.NormalizeSweep(arc.StartAngle, arc.EndAngle);
                    var segmentCount = CadCurveSampling.ResolveSegmentCount(sweep, arc.Radius, minSegments: 6);
                    var vertices = CadCurveSampling.SampleArc(arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle, segmentCount);
                    if (!TryAppendExplodedSegments(vertices, isClosed: false, sourceEntity, targetForward, targetCreated, createdIds, out var arcError))
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail(arcError!));
                    }

                    targetForward.Add(CadOperationPayloadCodec.DeleteArc(sourceId, arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle));
                    targetInverse.Add(CadOperationPayloadCodec.CreateArc(sourceId, arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle).WithSourceProperties(sourceEntity));
                    break;
                }
                case Circle circle:
                {
                    var segmentCount = CadCurveSampling.ResolveSegmentCount(CadCurveSampling.Tau, circle.Radius, minSegments: 16);
                    var vertices = CadCurveSampling.SampleCircle(circle.Center, circle.Radius, segmentCount);
                    if (!TryAppendExplodedSegments(vertices, isClosed: true, sourceEntity, targetForward, targetCreated, createdIds, out var circleError))
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail(circleError!));
                    }

                    targetForward.Add(CadOperationPayloadCodec.DeleteCircle(sourceId, circle.Center, circle.Radius));
                    targetInverse.Add(CadOperationPayloadCodec.CreateCircle(sourceId, circle.Center, circle.Radius).WithSourceProperties(sourceEntity));
                    break;
                }
                case Ellipse ellipse:
                {
                    var isClosed = CadCurveSampling.IsFullSweep(ellipse.StartParameter, ellipse.EndParameter);
                    var majorLength = Math.Sqrt(
                        ellipse.MajorAxisEndPoint.X * ellipse.MajorAxisEndPoint.X +
                        ellipse.MajorAxisEndPoint.Y * ellipse.MajorAxisEndPoint.Y);
                    var sweep = CadCurveSampling.NormalizeSweep(ellipse.StartParameter, ellipse.EndParameter);
                    var segmentCount = CadCurveSampling.ResolveSegmentCount(sweep, majorLength, minSegments: isClosed ? 16 : 8);
                    var vertices = CadCurveSampling.SampleEllipse(
                        ellipse.Center,
                        ellipse.MajorAxisEndPoint,
                        ellipse.RadiusRatio,
                        ellipse.StartParameter,
                        ellipse.EndParameter,
                        segmentCount,
                        isClosed);
                    if (!TryAppendExplodedSegments(vertices, isClosed, sourceEntity, targetForward, targetCreated, createdIds, out var ellipseError))
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail(ellipseError!));
                    }

                    targetForward.Add(CadOperationPayloadCodec.DeleteEllipse(
                        sourceId,
                        ellipse.Center,
                        ellipse.MajorAxisEndPoint,
                        ellipse.RadiusRatio,
                        ellipse.StartParameter,
                        ellipse.EndParameter,
                        ellipse.Normal));
                    targetInverse.Add(CadOperationPayloadCodec.CreateEllipse(
                            sourceId,
                            ellipse.Center,
                            ellipse.MajorAxisEndPoint,
                            ellipse.RadiusRatio,
                            ellipse.StartParameter,
                            ellipse.EndParameter,
                            ellipse.Normal)
                        .WithSourceProperties(sourceEntity));
                    break;
                }
                case Spline spline:
                {
                    if (!TryGetSplineVertices(spline, out var splineVertices, out var isClosed, out var splineError))
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail(splineError!));
                    }

                    if (!TryAppendExplodedSegments(splineVertices, isClosed, sourceEntity, targetForward, targetCreated, createdIds, out var segmentError))
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail(segmentError!));
                    }

                    targetForward.Add(CadOperationPayloadCodec.DeleteSpline(
                        sourceId,
                        spline.Degree,
                        spline.IsClosed,
                        spline.IsPeriodic,
                        spline.FitPoints.ToArray(),
                        spline.ControlPoints.ToArray(),
                        spline.Knots.ToArray(),
                        spline.Weights.ToArray(),
                        spline.StartTangent,
                        spline.EndTangent,
                        spline.Normal));
                    targetInverse.Add(CadOperationPayloadCodec.CreateSpline(
                            sourceId,
                            spline.Degree,
                            spline.IsClosed,
                            spline.IsPeriodic,
                            spline.FitPoints.ToArray(),
                            spline.ControlPoints.ToArray(),
                            spline.Knots.ToArray(),
                            spline.Weights.ToArray(),
                            spline.StartTangent,
                            spline.EndTangent,
                            spline.Normal)
                        .WithSourceProperties(sourceEntity));
                    break;
                }
                case Hatch hatch:
                {
                    if (!CadHatchGeometry.TryGetLoops(hatch, out var loops, out var loopError))
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail($"EXPLODE could not process HATCH: {loopError}"));
                    }

                    foreach (var loop in loops)
                    {
                        if (!TryAppendExplodedSegments(loop, isClosed: true, sourceEntity, targetForward, targetCreated, createdIds, out var hatchError))
                        {
                            return ValueTask.FromResult(CadCommandResult.Fail(hatchError!));
                        }
                    }

                    var patternName = CadHatchGeometry.ResolvePatternName(hatch);
                    targetForward.Add(CadOperationPayloadCodec.DeleteHatch(sourceId, loops, hatch.IsSolid, patternName, hatch.Normal));
                    targetInverse.Add(CadOperationPayloadCodec.CreateHatch(sourceId, loops, hatch.IsSolid, patternName, hatch.Normal).WithSourceProperties(sourceEntity));
                    break;
                }
                default:
                    return ValueTask.FromResult(CadCommandResult.Fail(
                        $"EXPLODE does not support entity type '{target.GetType().Name}' yet."));
            }

            for (var index = targetCreated.Count - 1; index >= 0; index--)
            {
                var item = targetCreated[index];
                targetInverse.Add(CadOperationPayloadCodec.DeleteLine(item.Id, item.Start, item.End));
            }

            forward.AddRange(targetForward);
            inverseChunks.Add(targetInverse);
        }

        var inverse = inverseChunks
            .AsEnumerable()
            .Reverse()
            .SelectMany(static chunk => chunk)
            .ToArray();

        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forward);
        var inverseBatch = session.NextBatch(actorId, inverse);
        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);

        var createdEntities = new List<object?>(createdIds.Count);
        foreach (var id in createdIds)
        {
            if (session.EntityIndex.TryGetEntity(id, out var entity))
            {
                createdEntities.Add(entity);
            }
        }

        if (createdEntities.Count > 0)
        {
            session.SetSelection(createdEntities, CadSelectionMode.Replace);
        }

        return ValueTask.FromResult(CadCommandResult.Ok($"Exploded {targets.Count} entity(s).", forward));
    }

    private static bool TryGetSplineVertices(
        Spline spline,
        out IReadOnlyList<XYZ> vertices,
        out bool isClosed,
        out string? error)
    {
        error = null;
        isClosed = spline.IsClosed;

        var source = spline.FitPoints.Count >= 2
            ? spline.FitPoints.ToArray()
            : spline.ControlPoints.Count >= 2
                ? spline.ControlPoints.ToArray()
                : Array.Empty<XYZ>();

        if (source.Length < 2)
        {
            vertices = Array.Empty<XYZ>();
            error = "EXPLODE requires a Spline with at least two fit/control points.";
            return false;
        }

        if (isClosed &&
            source.Length > 2 &&
            DistanceSquared(source[0], source[^1]) <= Epsilon * Epsilon)
        {
            source = source[..^1];
        }

        if (source.Length < (isClosed ? 3 : 2))
        {
            vertices = Array.Empty<XYZ>();
            error = "EXPLODE requires a closed Spline with at least three distinct points.";
            return false;
        }

        vertices = source;
        return true;
    }

    private static bool TryAppendExplodedSegments(
        IReadOnlyList<XYZ> vertices,
        bool isClosed,
        Entity sourceEntity,
        List<CadOperation> targetForward,
        List<(CadEntityId Id, XYZ Start, XYZ End)> targetCreated,
        List<CadEntityId> createdIds,
        out string? error)
    {
        error = null;
        if (vertices.Count < (isClosed ? 3 : 2))
        {
            error = "EXPLODE requires enough sampled points to produce segments.";
            return false;
        }

        var segmentCount = isClosed ? vertices.Count : vertices.Count - 1;
        var createdCount = 0;
        for (var index = 0; index < segmentCount; index++)
        {
            var start = vertices[index];
            var end = vertices[(index + 1) % vertices.Count];
            if (DistanceSquared(start, end) <= Epsilon * Epsilon)
            {
                continue;
            }

            var lineId = CadEntityId.New();
            createdIds.Add(lineId);
            targetCreated.Add((lineId, start, end));
            targetForward.Add(CadOperationPayloadCodec.CreateLine(lineId, start, end).WithSourceProperties(sourceEntity));
            createdCount++;
        }

        if (createdCount == 0)
        {
            error = "EXPLODE cannot create zero-length line segments.";
            return false;
        }

        return true;
    }

    private static double DistanceSquared(XYZ first, XYZ second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        var dz = first.Z - second.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private const double Epsilon = 1e-8;
}
