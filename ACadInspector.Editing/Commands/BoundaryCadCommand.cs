using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Editing.Commands;

public sealed class BoundaryCadCommand : ICadCommandHandler
{
    public string Name => "BOUNDARY";
    public IReadOnlyList<string> Aliases => ["BO"];

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
        var inverse = new List<CadOperation>();
        var createdIds = new List<CadEntityId>();

        foreach (var target in targets)
        {
            if (target is Hatch hatch)
            {
                if (!CadHatchGeometry.TryGetLoops(hatch, out var loops, out var loopError))
                {
                    return ValueTask.FromResult(CadCommandResult.Fail(loopError!));
                }

                foreach (var loop in loops)
                {
                    var id = CadEntityId.New();
                    forward.Add(
                        CadOperationPayloadCodec.CreateLwPolyline(id, loop, isClosed: true)
                            .WithCurrentProperties(session.Document));
                    inverse.Add(CadOperationPayloadCodec.DeleteLwPolyline(id, loop, isClosed: true));
                    createdIds.Add(id);
                }

                continue;
            }

            if (target is LwPolyline polyline && polyline.IsClosed)
            {
                var vertices = CadGeometryTransform.ToVertices(polyline);
                var id = CadEntityId.New();
                forward.Add(
                    CadOperationPayloadCodec.CreateLwPolyline(id, vertices, isClosed: true)
                        .WithCurrentProperties(session.Document));
                inverse.Add(CadOperationPayloadCodec.DeleteLwPolyline(id, vertices, isClosed: true));
                createdIds.Add(id);
                continue;
            }

            if (target is Circle circle)
            {
                var segmentCount = CadCurveSampling.ResolveSegmentCount(CadCurveSampling.Tau, circle.Radius, minSegments: 16);
                var vertices = CadCurveSampling.SampleCircle(circle.Center, circle.Radius, segmentCount);
                var id = CadEntityId.New();
                forward.Add(
                    CadOperationPayloadCodec.CreateLwPolyline(id, vertices, isClosed: true)
                        .WithCurrentProperties(session.Document));
                inverse.Add(CadOperationPayloadCodec.DeleteLwPolyline(id, vertices, isClosed: true));
                createdIds.Add(id);
                continue;
            }

            if (target is Ellipse ellipse)
            {
                if (!CadCurveSampling.IsFullSweep(ellipse.StartParameter, ellipse.EndParameter))
                {
                    return ValueTask.FromResult(CadCommandResult.Fail("BOUNDARY requires a closed ELLIPSE (full sweep)."));
                }

                var majorLength = Math.Sqrt(
                    ellipse.MajorAxisEndPoint.X * ellipse.MajorAxisEndPoint.X +
                    ellipse.MajorAxisEndPoint.Y * ellipse.MajorAxisEndPoint.Y);
                var segmentCount = CadCurveSampling.ResolveSegmentCount(CadCurveSampling.Tau, majorLength, minSegments: 16);
                var vertices = CadCurveSampling.SampleEllipse(
                    ellipse.Center,
                    ellipse.MajorAxisEndPoint,
                    ellipse.RadiusRatio,
                    ellipse.StartParameter,
                    ellipse.EndParameter,
                    segmentCount,
                    closed: true);
                if (vertices.Count < 3)
                {
                    return ValueTask.FromResult(CadCommandResult.Fail("BOUNDARY could not sample a valid closed ELLIPSE."));
                }

                var id = CadEntityId.New();
                forward.Add(
                    CadOperationPayloadCodec.CreateLwPolyline(id, vertices, isClosed: true)
                        .WithCurrentProperties(session.Document));
                inverse.Add(CadOperationPayloadCodec.DeleteLwPolyline(id, vertices, isClosed: true));
                createdIds.Add(id);
                continue;
            }

            if (target is Spline spline && spline.IsClosed)
            {
                var vertices = spline.FitPoints.Count >= 3
                    ? spline.FitPoints.ToArray()
                    : spline.ControlPoints.Count >= 3
                        ? spline.ControlPoints.ToArray()
                        : Array.Empty<XYZ>();

                if (vertices.Length > 3 && DistanceSquared(vertices[0], vertices[^1]) <= Epsilon * Epsilon)
                {
                    vertices = vertices[..^1];
                }

                if (vertices.Length < 3)
                {
                    return ValueTask.FromResult(CadCommandResult.Fail("BOUNDARY requires a closed SPLINE with at least three points."));
                }

                var id = CadEntityId.New();
                forward.Add(
                    CadOperationPayloadCodec.CreateLwPolyline(id, vertices, isClosed: true)
                        .WithCurrentProperties(session.Document));
                inverse.Add(CadOperationPayloadCodec.DeleteLwPolyline(id, vertices, isClosed: true));
                createdIds.Add(id);
                continue;
            }

            return ValueTask.FromResult(CadCommandResult.Fail($"BOUNDARY does not support entity type '{target.GetType().Name}' yet."));
        }

        if (forward.Count == 0)
        {
            return ValueTask.FromResult(CadCommandResult.Fail("BOUNDARY did not produce any boundaries."));
        }

        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forward);
        var inverseBatch = session.NextBatch(actorId, inverse.AsEnumerable().Reverse().ToArray());
        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);

        var createdEntities = new List<object?>(createdIds.Count);
        foreach (var id in createdIds)
        {
            if (session.EntityIndex.TryGetEntity(id, out var created))
            {
                createdEntities.Add(created);
            }
        }

        if (createdEntities.Count > 0)
        {
            session.SetSelection(createdEntities, CadSelectionMode.Replace);
        }

        return ValueTask.FromResult(CadCommandResult.Ok($"Created {forward.Count} boundary entity(s).", forward));
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
