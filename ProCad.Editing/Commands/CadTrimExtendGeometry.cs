using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Commands;

internal static class CadTrimExtendGeometry
{
    public static bool TryComputeTrimmedLine(
        Line target,
        Entity boundary,
        CadLineEndpoint endpoint,
        out XYZ newStart,
        out XYZ newEnd,
        out string? error)
    {
        return TryComputeAdjustedLine(
            target,
            boundary,
            endpoint,
            requireT: static (candidateT, currentLength) => candidateT > Epsilon && candidateT < currentLength - Epsilon,
            chooser: static (candidates, _) => candidates.Max(),
            out newStart,
            out newEnd,
            out error);
    }

    public static bool TryComputeExtendedLine(
        Line target,
        Entity boundary,
        CadLineEndpoint endpoint,
        out XYZ newStart,
        out XYZ newEnd,
        out string? error)
    {
        return TryComputeAdjustedLine(
            target,
            boundary,
            endpoint,
            requireT: static (candidateT, currentLength) => candidateT > currentLength + Epsilon,
            chooser: static (candidates, currentLength) => candidates.Where(t => t > currentLength + Epsilon).Min(),
            out newStart,
            out newEnd,
            out error);
    }

    private static bool TryComputeAdjustedLine(
        Line target,
        Entity boundary,
        CadLineEndpoint endpoint,
        Func<double, double, bool> requireT,
        Func<IReadOnlyList<double>, double, double> chooser,
        out XYZ newStart,
        out XYZ newEnd,
        out string? error)
    {
        newStart = target.StartPoint;
        newEnd = target.EndPoint;
        error = null;

        var lineVector = new XYZ(
            target.EndPoint.X - target.StartPoint.X,
            target.EndPoint.Y - target.StartPoint.Y,
            target.EndPoint.Z - target.StartPoint.Z);
        var lineLength = Math.Sqrt((lineVector.X * lineVector.X) + (lineVector.Y * lineVector.Y) + (lineVector.Z * lineVector.Z));
        if (lineLength <= Epsilon)
        {
            error = "Target line is zero-length.";
            return false;
        }

        var (anchor, direction) = endpoint == CadLineEndpoint.End
            ? (target.StartPoint, new XYZ(lineVector.X / lineLength, lineVector.Y / lineLength, lineVector.Z / lineLength))
            : (target.EndPoint, new XYZ(-lineVector.X / lineLength, -lineVector.Y / lineLength, -lineVector.Z / lineLength));

        if (!TryGetIntersectionCandidates(target, boundary, out var candidates, out error))
        {
            return false;
        }

        var validT = new List<double>();
        foreach (var candidate in candidates)
        {
            var v = new XYZ(candidate.X - anchor.X, candidate.Y - anchor.Y, candidate.Z - anchor.Z);
            var t = (v.X * direction.X) + (v.Y * direction.Y) + (v.Z * direction.Z);
            if (requireT(t, lineLength))
            {
                validT.Add(t);
            }
        }

        if (validT.Count == 0)
        {
            error = "No valid trim/extend intersection found for the requested endpoint.";
            return false;
        }

        var chosenT = chooser(validT, lineLength);
        var newEndpoint = new XYZ(
            anchor.X + direction.X * chosenT,
            anchor.Y + direction.Y * chosenT,
            anchor.Z + direction.Z * chosenT);

        if (endpoint == CadLineEndpoint.End)
        {
            newEnd = newEndpoint;
        }
        else
        {
            newStart = newEndpoint;
        }

        return true;
    }

    private static bool TryGetIntersectionCandidates(
        Line target,
        Entity boundary,
        out IReadOnlyList<XYZ> candidates,
        out string? error)
    {
        error = null;
        switch (boundary)
        {
            case Line lineBoundary:
            {
                if (!TryIntersectInfiniteLines(target.StartPoint, target.EndPoint, lineBoundary.StartPoint, lineBoundary.EndPoint, out var intersection))
                {
                    candidates = Array.Empty<XYZ>();
                    error = "Boundary line is parallel to target line.";
                    return false;
                }

                candidates = new[] { intersection };
                return true;
            }
            case XLine xlineBoundary:
            {
                var secondPoint = new XYZ(
                    xlineBoundary.FirstPoint.X + xlineBoundary.Direction.X,
                    xlineBoundary.FirstPoint.Y + xlineBoundary.Direction.Y,
                    xlineBoundary.FirstPoint.Z + xlineBoundary.Direction.Z);
                if (!TryIntersectInfiniteLines(target.StartPoint, target.EndPoint, xlineBoundary.FirstPoint, secondPoint, out var intersection))
                {
                    candidates = Array.Empty<XYZ>();
                    error = "Boundary xline is parallel to target line.";
                    return false;
                }

                candidates = new[] { intersection };
                return true;
            }
            case Ray rayBoundary:
            {
                var secondPoint = new XYZ(
                    rayBoundary.StartPoint.X + rayBoundary.Direction.X,
                    rayBoundary.StartPoint.Y + rayBoundary.Direction.Y,
                    rayBoundary.StartPoint.Z + rayBoundary.Direction.Z);
                if (!TryIntersectLineAndRay(
                        target.StartPoint,
                        target.EndPoint,
                        rayBoundary.StartPoint,
                        secondPoint,
                        out var intersection))
                {
                    candidates = Array.Empty<XYZ>();
                    error = "Target line does not intersect boundary ray.";
                    return false;
                }

                candidates = new[] { intersection };
                return true;
            }
            case Arc arcBoundary:
            {
                var intersections = IntersectInfiniteLineCircle(target.StartPoint, target.EndPoint, arcBoundary.Center, arcBoundary.Radius);
                if (intersections.Count == 0)
                {
                    candidates = Array.Empty<XYZ>();
                    error = "Target line does not intersect boundary arc.";
                    return false;
                }

                var filtered = intersections
                    .Where(candidate => IsPointOnArc(arcBoundary, candidate))
                    .ToArray();
                candidates = filtered;
                if (candidates.Count == 0)
                {
                    error = "Target line does not intersect boundary arc sweep.";
                    return false;
                }

                return true;
            }
            case Circle circleBoundary:
            {
                candidates = IntersectInfiniteLineCircle(target.StartPoint, target.EndPoint, circleBoundary.Center, circleBoundary.Radius);
                if (candidates.Count == 0)
                {
                    error = "Target line does not intersect boundary circle.";
                    return false;
                }

                return true;
            }
            case Ellipse ellipseBoundary:
            {
                var isClosed = CadCurveSampling.IsFullSweep(ellipseBoundary.StartParameter, ellipseBoundary.EndParameter);
                var majorLength = Math.Sqrt(
                    ellipseBoundary.MajorAxisEndPoint.X * ellipseBoundary.MajorAxisEndPoint.X +
                    ellipseBoundary.MajorAxisEndPoint.Y * ellipseBoundary.MajorAxisEndPoint.Y);
                var sweep = CadCurveSampling.NormalizeSweep(ellipseBoundary.StartParameter, ellipseBoundary.EndParameter);
                var segmentCount = CadCurveSampling.ResolveSegmentCount(sweep, majorLength, minSegments: isClosed ? 16 : 8);
                var sampled = CadCurveSampling.SampleEllipse(
                    ellipseBoundary.Center,
                    ellipseBoundary.MajorAxisEndPoint,
                    ellipseBoundary.RadiusRatio,
                    ellipseBoundary.StartParameter,
                    ellipseBoundary.EndParameter,
                    segmentCount,
                    isClosed);
                if (TryCollectSampledBoundaryIntersections(target.StartPoint, target.EndPoint, sampled, isClosed, out var ellipseIntersections))
                {
                    candidates = ellipseIntersections;
                    return true;
                }

                candidates = Array.Empty<XYZ>();
                error = "Target line does not intersect boundary ellipse.";
                return false;
            }
            case Spline splineBoundary:
            {
                var sampled = splineBoundary.FitPoints.Count >= 2
                    ? splineBoundary.FitPoints.ToArray()
                    : splineBoundary.ControlPoints.Count >= 2
                        ? splineBoundary.ControlPoints.ToArray()
                        : Array.Empty<XYZ>();
                if (sampled.Length > 3 &&
                    splineBoundary.IsClosed &&
                    Math.Abs(sampled[0].X - sampled[^1].X) <= 1e-6 &&
                    Math.Abs(sampled[0].Y - sampled[^1].Y) <= 1e-6 &&
                    Math.Abs(sampled[0].Z - sampled[^1].Z) <= 1e-6)
                {
                    sampled = sampled[..^1];
                }

                if (TryCollectSampledBoundaryIntersections(target.StartPoint, target.EndPoint, sampled, splineBoundary.IsClosed, out var splineIntersections))
                {
                    candidates = splineIntersections;
                    return true;
                }

                candidates = Array.Empty<XYZ>();
                error = "Target line does not intersect boundary spline.";
                return false;
            }
            case Hatch hatchBoundary:
            {
                if (!CadHatchGeometry.TryGetLoops(hatchBoundary, out var loops, out var loopError))
                {
                    candidates = Array.Empty<XYZ>();
                    error = $"Boundary hatch loops are invalid: {loopError}";
                    return false;
                }

                var intersections = new List<XYZ>();
                foreach (var loop in loops)
                {
                    if (!TryCollectSampledBoundaryIntersections(
                            target.StartPoint,
                            target.EndPoint,
                            loop,
                            isClosed: true,
                            out var loopIntersections))
                    {
                        continue;
                    }

                    foreach (var hit in loopIntersections)
                    {
                        AddUniqueIntersection(intersections, hit);
                    }
                }

                if (intersections.Count == 0)
                {
                    candidates = Array.Empty<XYZ>();
                    error = "Target line does not intersect boundary hatch.";
                    return false;
                }

                candidates = intersections;
                return true;
            }
            case LwPolyline lwPolylineBoundary:
            {
                if (TryCollectPolylineIntersections(target.StartPoint, target.EndPoint, lwPolylineBoundary, out var intersections))
                {
                    candidates = intersections;
                    return true;
                }

                candidates = Array.Empty<XYZ>();
                error = "Target line does not intersect boundary polyline.";
                return false;
            }
            case IPolyline polylineBoundary:
            {
                if (TryCollectPolylineIntersections(target.StartPoint, target.EndPoint, polylineBoundary, out var intersections))
                {
                    candidates = intersections;
                    return true;
                }

                candidates = Array.Empty<XYZ>();
                error = "Target line does not intersect boundary polyline.";
                return false;
            }
            default:
                candidates = Array.Empty<XYZ>();
                error = $"Boundary type '{boundary.GetType().Name}' is not supported for TRIM/EXTEND yet.";
                return false;
        }
    }

    private static bool TryIntersectInfiniteLines(
        XYZ p1,
        XYZ p2,
        XYZ q1,
        XYZ q2,
        out XYZ intersection)
    {
        if (!TryIntersectParametricLines(p1, p2, q1, q2, out var t, out _, out _))
        {
            intersection = XYZ.Zero;
            return false;
        }

        intersection = new XYZ(
            p1.X + (p2.X - p1.X) * t,
            p1.Y + (p2.Y - p1.Y) * t,
            p1.Z + (p2.Z - p1.Z) * t);
        return true;
    }

    private static bool TryIntersectLineAndRay(
        XYZ lineStart,
        XYZ lineEnd,
        XYZ rayStart,
        XYZ rayDirectionPoint,
        out XYZ intersection)
    {
        intersection = XYZ.Zero;
        if (!TryIntersectParametricLines(lineStart, lineEnd, rayStart, rayDirectionPoint, out var lineT, out var rayT, out _))
        {
            return false;
        }

        if (rayT < -Epsilon)
        {
            return false;
        }

        intersection = new XYZ(
            lineStart.X + (lineEnd.X - lineStart.X) * lineT,
            lineStart.Y + (lineEnd.Y - lineStart.Y) * lineT,
            lineStart.Z + (lineEnd.Z - lineStart.Z) * lineT);
        return true;
    }

    private static bool TryIntersectLineSegment(
        XYZ lineStart,
        XYZ lineEnd,
        XYZ segmentStart,
        XYZ segmentEnd,
        out XYZ intersection)
    {
        intersection = XYZ.Zero;
        if (!TryIntersectParametricLines(lineStart, lineEnd, segmentStart, segmentEnd, out var lineT, out var segmentT, out _))
        {
            return false;
        }

        if (segmentT < -Epsilon || segmentT > 1.0 + Epsilon)
        {
            return false;
        }

        intersection = new XYZ(
            lineStart.X + (lineEnd.X - lineStart.X) * lineT,
            lineStart.Y + (lineEnd.Y - lineStart.Y) * lineT,
            lineStart.Z + (lineEnd.Z - lineStart.Z) * lineT);
        return true;
    }

    private static bool TryIntersectParametricLines(
        XYZ p1,
        XYZ p2,
        XYZ q1,
        XYZ q2,
        out double t,
        out double u,
        out double cross)
    {
        var rX = p2.X - p1.X;
        var rY = p2.Y - p1.Y;
        var sX = q2.X - q1.X;
        var sY = q2.Y - q1.Y;

        cross = (rX * sY) - (rY * sX);
        if (Math.Abs(cross) <= Epsilon)
        {
            t = 0.0;
            u = 0.0;
            return false;
        }

        var qpX = q1.X - p1.X;
        var qpY = q1.Y - p1.Y;
        t = ((qpX * sY) - (qpY * sX)) / cross;
        u = ((qpX * rY) - (qpY * rX)) / cross;
        return true;
    }

    private static bool TryCollectPolylineIntersections(
        XYZ lineStart,
        XYZ lineEnd,
        IPolyline polyline,
        out IReadOnlyList<XYZ> intersections)
    {
        intersections = Array.Empty<XYZ>();
        var points = new List<XYZ>();
        foreach (var vertex in polyline.Vertices)
        {
            if (TryConvertVector(vertex.Location, polyline.Elevation, out var point))
            {
                points.Add(point);
            }
        }

        if (points.Count < 2)
        {
            return false;
        }

        var results = new List<XYZ>(points.Count);
        var segmentCount = polyline.IsClosed ? points.Count : points.Count - 1;
        for (var index = 0; index < segmentCount; index++)
        {
            var start = points[index];
            var end = points[(index + 1) % points.Count];
            if (!TryIntersectLineSegment(lineStart, lineEnd, start, end, out var hit))
            {
                continue;
            }

            AddUniqueIntersection(results, hit);
        }

        if (results.Count == 0)
        {
            return false;
        }

        intersections = results;
        return true;
    }

    private static bool TryCollectSampledBoundaryIntersections(
        XYZ lineStart,
        XYZ lineEnd,
        IReadOnlyList<XYZ> sampledVertices,
        bool isClosed,
        out IReadOnlyList<XYZ> intersections)
    {
        intersections = Array.Empty<XYZ>();
        if (sampledVertices.Count < (isClosed ? 3 : 2))
        {
            return false;
        }

        var results = new List<XYZ>();
        var segmentCount = isClosed ? sampledVertices.Count : sampledVertices.Count - 1;
        for (var index = 0; index < segmentCount; index++)
        {
            var segmentStart = sampledVertices[index];
            var segmentEnd = sampledVertices[(index + 1) % sampledVertices.Count];
            if (!TryIntersectLineSegment(lineStart, lineEnd, segmentStart, segmentEnd, out var hit))
            {
                continue;
            }

            AddUniqueIntersection(results, hit);
        }

        if (results.Count == 0)
        {
            return false;
        }

        intersections = results;
        return true;
    }

    private static bool TryConvertVector(IVector vector, double fallbackZ, out XYZ point)
    {
        point = XYZ.Zero;
        if (vector is null || vector.Dimension < 2)
        {
            return false;
        }

        var x = vector[0];
        var y = vector[1];
        var z = vector.Dimension > 2 ? vector[2] : fallbackZ;
        point = new XYZ(x, y, z);
        return true;
    }

    private static void AddUniqueIntersection(ICollection<XYZ> results, XYZ candidate)
    {
        foreach (var existing in results)
        {
            if (Math.Abs(existing.X - candidate.X) <= 1e-6 &&
                Math.Abs(existing.Y - candidate.Y) <= 1e-6 &&
                Math.Abs(existing.Z - candidate.Z) <= 1e-6)
            {
                return;
            }
        }

        results.Add(candidate);
    }

    private static bool IsPointOnArc(Arc arc, XYZ point)
    {
        var angle = NormalizeAngle(Math.Atan2(point.Y - arc.Center.Y, point.X - arc.Center.X));
        var start = NormalizeAngle(arc.StartAngle);
        var end = NormalizeAngle(arc.EndAngle);

        if (end < start)
        {
            end += TwoPi;
        }

        if (angle < start)
        {
            angle += TwoPi;
        }

        return angle >= start - 1e-7 && angle <= end + 1e-7;
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= TwoPi;
        if (angle < 0.0)
        {
            angle += TwoPi;
        }

        return angle;
    }

    private static IReadOnlyList<XYZ> IntersectInfiniteLineCircle(
        XYZ lineStart,
        XYZ lineEnd,
        XYZ center,
        double radius)
    {
        var dx = lineEnd.X - lineStart.X;
        var dy = lineEnd.Y - lineStart.Y;
        var a = (dx * dx) + (dy * dy);
        if (a <= Epsilon)
        {
            return Array.Empty<XYZ>();
        }

        var fx = lineStart.X - center.X;
        var fy = lineStart.Y - center.Y;
        var b = 2.0 * ((fx * dx) + (fy * dy));
        var c = (fx * fx) + (fy * fy) - (radius * radius);

        var discriminant = (b * b) - (4.0 * a * c);
        if (discriminant < -Epsilon)
        {
            return Array.Empty<XYZ>();
        }

        if (Math.Abs(discriminant) <= Epsilon)
        {
            var t = -b / (2.0 * a);
            return
            [
                new XYZ(lineStart.X + t * dx, lineStart.Y + t * dy, lineStart.Z)
            ];
        }

        var sqrt = Math.Sqrt(discriminant);
        var t1 = (-b - sqrt) / (2.0 * a);
        var t2 = (-b + sqrt) / (2.0 * a);
        return
        [
            new XYZ(lineStart.X + t1 * dx, lineStart.Y + t1 * dy, lineStart.Z),
            new XYZ(lineStart.X + t2 * dx, lineStart.Y + t2 * dy, lineStart.Z)
        ];
    }

    private const double Epsilon = 1e-8;
    private const double TwoPi = Math.PI * 2.0;
}
