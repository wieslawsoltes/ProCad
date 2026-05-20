using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Selection;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Commands;

public sealed class HatchCadCommand : ICadCommandHandler
{
    private const double Epsilon = 1e-8;
    private const double NormalAxisEpsilon = 1e-6;

    private readonly record struct HatchBoundaryData(
        IReadOnlyList<IReadOnlyList<XYZ>> Loops,
        XYZ Normal);

    public string Name => "HATCH";
    public IReadOnlyList<string> Aliases => ["H"];

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

        if (!CadCommandParsing.TryParseHatchArguments(
                context.Arguments,
                out var patternName,
                out var isSolid,
                out var consumed,
                out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var targetTokens = context.Arguments.Skip(consumed).ToArray();
        if (!CadCommandTargetResolver.TryResolve(session, targetTokens, out var targets, out var targetError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(targetError!));
        }

        var forward = new List<CadOperation>(targets.Count);
        var inverse = new List<CadOperation>(targets.Count);
        var createdIds = new List<CadEntityId>(targets.Count);
        var normalizedPatternName = isSolid ? "SOLID" : patternName;

        foreach (var target in targets)
        {
            if (!TryResolveBoundary(target, out var boundary, out var boundaryError))
            {
                return ValueTask.FromResult(CadCommandResult.Fail(boundaryError!));
            }

            var id = CadEntityId.New();
            forward.Add(
                CadOperationPayloadCodec.CreateHatch(id, boundary.Loops, isSolid, normalizedPatternName, boundary.Normal)
                    .WithCurrentProperties(session.Document));
            inverse.Add(CadOperationPayloadCodec.DeleteHatch(id, boundary.Loops, isSolid, normalizedPatternName, boundary.Normal));
            createdIds.Add(id);
        }

        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forward);
        var inverseBatch = session.NextBatch(actorId, inverse.AsEnumerable().Reverse().ToArray());
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

        return ValueTask.FromResult(CadCommandResult.Ok($"Created {forward.Count} HATCH entity(s).", forward));
    }

    private static bool TryResolveBoundary(
        Entity target,
        out HatchBoundaryData boundary,
        out string? error)
    {
        boundary = default;
        error = null;

        switch (target)
        {
            case Hatch hatch:
            {
                if (!CadHatchGeometry.TryGetLoops(hatch, out var loops, out error))
                {
                    return false;
                }

                boundary = new HatchBoundaryData(loops, ResolveNormal(hatch.Normal));
                return true;
            }
            case LwPolyline polyline:
            {
                if (!polyline.IsClosed)
                {
                    error = "HATCH requires a closed LWPOLYLINE boundary.";
                    return false;
                }

                var vertices = CadGeometryTransform.ToVertices(polyline).ToArray();
                if (vertices.Length < 3)
                {
                    error = "HATCH boundary polyline must contain at least three vertices.";
                    return false;
                }

                boundary = new HatchBoundaryData(
                    new IReadOnlyList<XYZ>[] { vertices },
                    ResolveNormal(polyline.Normal));
                return true;
            }
            case Circle circle:
            {
                var segmentCount = CadCurveSampling.ResolveSegmentCount(CadCurveSampling.Tau, circle.Radius, minSegments: 16);
                var vertices = CadCurveSampling.SampleCircle(circle.Center, circle.Radius, segmentCount);
                if (vertices.Count < 3)
                {
                    error = "HATCH could not sample a valid CIRCLE boundary.";
                    return false;
                }

                boundary = new HatchBoundaryData(
                    new IReadOnlyList<XYZ>[] { vertices },
                    ResolveNormal(circle.Normal));
                return true;
            }
            case Ellipse ellipse:
            {
                if (!CadCurveSampling.IsFullSweep(ellipse.StartParameter, ellipse.EndParameter))
                {
                    error = "HATCH requires a closed ELLIPSE boundary (full sweep).";
                    return false;
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
                    error = "HATCH could not sample a valid ELLIPSE boundary.";
                    return false;
                }

                boundary = new HatchBoundaryData(
                    new IReadOnlyList<XYZ>[] { vertices },
                    ResolveNormal(ellipse.Normal));
                return true;
            }
            case Spline spline:
            {
                if (!spline.IsClosed)
                {
                    error = "HATCH requires a closed SPLINE boundary.";
                    return false;
                }

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
                    error = "HATCH requires a closed SPLINE boundary with at least three points.";
                    return false;
                }

                boundary = new HatchBoundaryData(
                    new IReadOnlyList<XYZ>[] { vertices },
                    ResolveNormal(spline.Normal));
                return true;
            }
            default:
                error = $"HATCH does not support boundary entity type '{target.GetType().Name}' yet.";
                return false;
        }
    }

    private static double DistanceSquared(XYZ first, XYZ second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        var dz = first.Z - second.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private static XYZ ResolveNormal(XYZ normal)
    {
        if (normal.IsZero())
        {
            return XYZ.AxisZ;
        }

        var normalized = normal.Normalize();
        if (Math.Abs(normalized.X) <= NormalAxisEpsilon &&
            Math.Abs(normalized.Y) <= NormalAxisEpsilon)
        {
            return normalized.Z >= 0.0
                ? XYZ.AxisZ
                : new XYZ(0.0, 0.0, -1.0);
        }

        return normalized;
    }
}
