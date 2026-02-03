using System;
using System.Collections.Generic;
using ACadSharp.Entities;
using ACadSharp.Extensions;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class DefaultRenderGeometrySampler : IRenderGeometrySampler
{
    private const int MinCircleSegments = 8;
    private const int MinArcSegments = 4;
    private const int MinEllipseSegments = 8;
    private const int MinSplinePoints = 4;

    public IReadOnlyList<XYZ> SampleCircle(Circle circle, int precision)
    {
        var segments = Math.Max(precision, MinCircleSegments);
        return circle.PolygonalVertexes(segments);
    }

    public IReadOnlyList<XYZ> SampleArc(Arc arc, int precision)
    {
        var sweep = ResolveSweep(arc.StartAngle, arc.EndAngle);
        var segments = ResolveSweepSegments(sweep, precision, MinArcSegments);
        return arc.PolygonalVertexes(segments);
    }

    public IReadOnlyList<XYZ> SampleEllipse(Ellipse ellipse, int precision)
    {
        var sweep = ResolveSweep(ellipse.StartParameter, ellipse.EndParameter);
        var segments = ResolveSweepSegments(sweep, precision, MinEllipseSegments);
        return ellipse.PolygonalVertexes(segments);
    }

    public IReadOnlyList<XYZ> SampleSpline(Spline spline, int precision)
    {
        var maxPoints = Math.Max(precision, MinSplinePoints);
        if (TrySampleSplineAdaptive(spline, maxPoints, out var points))
        {
            return points;
        }

        return spline.TryPolygonalVertexes(maxPoints, out var fallback) ? fallback : new List<XYZ>();
    }

    public IReadOnlyList<XYZ> SamplePolyline(IPolyline polyline, int precision)
    {
        var points = precision >= 2
            ? polyline.GetPoints<XYZ>(precision)
            : polyline.GetPoints<XYZ>();

        return ToList(points);
    }

    private static IReadOnlyList<XYZ> ToList(IEnumerable<XYZ> points)
    {
        if (points is IReadOnlyList<XYZ> list)
        {
            return list;
        }

        var result = new List<XYZ>();
        foreach (var point in points)
        {
            result.Add(point);
        }

        return result;
    }

    private static double ResolveSweep(double start, double end)
    {
        var sweep = end - start;
        if (sweep <= 0)
        {
            sweep += MathHelper.TwoPI;
        }

        return sweep;
    }

    private static int ResolveSweepSegments(double sweep, int precision, int minSegments)
    {
        var maxSegments = Math.Max(precision, minSegments);
        var ratio = Math.Abs(sweep) / MathHelper.TwoPI;
        var segments = (int)Math.Ceiling(maxSegments * ratio);
        return Math.Clamp(segments, minSegments, maxSegments);
    }

    private static bool TrySampleSplineAdaptive(Spline spline, int maxPoints, out List<XYZ> points)
    {
        points = new List<XYZ>(maxPoints);
        if (!spline.TryPointOnSpline(0, out var start) || !TryResolveSplineEnd(spline, out var end))
        {
            return false;
        }

        var tolerance = EstimateSplineTolerance(spline, maxPoints);
        if (tolerance <= 0)
        {
            return false;
        }

        points.Add(start);
        var stack = new Stack<SplineSegment>();
        stack.Push(new SplineSegment(0, start, 1, end, depth: 0));

        var maxDepth = Math.Max(4, (int)Math.Ceiling(Math.Log(maxPoints, 2)) + 1);

        while (stack.Count > 0)
        {
            var segment = stack.Pop();
            if (points.Count >= maxPoints)
            {
                break;
            }

            var midT = (segment.StartT + segment.EndT) * 0.5;
            if (!spline.TryPointOnSpline(midT, out var mid))
            {
                points.Add(segment.End);
                continue;
            }

            var error = DistanceToSegment(mid, segment.Start, segment.End);
            if (error <= tolerance || segment.Depth >= maxDepth)
            {
                points.Add(segment.End);
                continue;
            }

            stack.Push(new SplineSegment(midT, mid, segment.EndT, segment.End, segment.Depth + 1));
            stack.Push(new SplineSegment(segment.StartT, segment.Start, midT, mid, segment.Depth + 1));
        }

        EnsureSplineEnd(points, end, maxPoints);
        TrimSplineClosure(points, maxPoints, spline, tolerance);

        return points.Count >= 2;
    }

    private static bool TryResolveSplineEnd(Spline spline, out XYZ end)
    {
        end = XYZ.NaN;
        if (!spline.TryPointOnSpline(1, out end))
        {
            return false;
        }

        if (IsInvalidPoint(end) || IsNearZero(end))
        {
            const double epsilon = 1e-6;
            var t = 1d - epsilon;
            if (t > 0d && spline.TryPointOnSpline(t, out var candidate) && !IsInvalidPoint(candidate) && !IsNearZero(candidate))
            {
                end = candidate;
            }
            else if (IsInvalidPoint(end))
            {
                return false;
            }
        }

        return true;
    }

    private static float EstimateSplineTolerance(Spline spline, int maxPoints)
    {
        if (!TryGetSplineBounds(spline, out var min, out var max))
        {
            return 0f;
        }

        var dx = max.X - min.X;
        var dy = max.Y - min.Y;
        var dz = max.Z - min.Z;
        var diagonal = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (diagonal <= 0)
        {
            return 0f;
        }

        var divisor = Math.Max(maxPoints - 1, 1);
        var tolerance = diagonal / divisor;
        return (float)Math.Max(tolerance, MathHelper.Epsilon);
    }

    private static bool TryGetSplineBounds(Spline spline, out XYZ min, out XYZ max)
    {
        min = XYZ.NaN;
        max = XYZ.NaN;
        var hasPoint = false;

        foreach (var point in spline.ControlPoints)
        {
            if (!hasPoint)
            {
                min = point;
                max = point;
                hasPoint = true;
                continue;
            }

            min = new XYZ(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
            max = new XYZ(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
        }

        foreach (var point in spline.FitPoints)
        {
            if (!hasPoint)
            {
                min = point;
                max = point;
                hasPoint = true;
                continue;
            }

            min = new XYZ(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
            max = new XYZ(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
        }

        return hasPoint;
    }

    private static float DistanceToSegment(XYZ point, XYZ start, XYZ end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var dz = end.Z - start.Z;
        var lenSq = dx * dx + dy * dy + dz * dz;
        if (lenSq <= MathHelper.Epsilon)
        {
            var px = point.X - start.X;
            var py = point.Y - start.Y;
            var pz = point.Z - start.Z;
            return (float)Math.Sqrt(px * px + py * py + pz * pz);
        }

        var t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy + (point.Z - start.Z) * dz) / lenSq;
        if (t <= 0)
        {
            var px = point.X - start.X;
            var py = point.Y - start.Y;
            var pz = point.Z - start.Z;
            return (float)Math.Sqrt(px * px + py * py + pz * pz);
        }

        if (t >= 1)
        {
            var px = point.X - end.X;
            var py = point.Y - end.Y;
            var pz = point.Z - end.Z;
            return (float)Math.Sqrt(px * px + py * py + pz * pz);
        }

        var projX = start.X + dx * t;
        var projY = start.Y + dy * t;
        var projZ = start.Z + dz * t;
        var ex = point.X - projX;
        var ey = point.Y - projY;
        var ez = point.Z - projZ;
        return (float)Math.Sqrt(ex * ex + ey * ey + ez * ez);
    }

    private static bool IsInvalidPoint(XYZ point)
    {
        return double.IsNaN(point.X)
               || double.IsNaN(point.Y)
               || double.IsNaN(point.Z)
               || double.IsInfinity(point.X)
               || double.IsInfinity(point.Y)
               || double.IsInfinity(point.Z);
    }

    private static bool IsNearZero(XYZ point)
    {
        const double epsilon = 1e-6;
        return Math.Abs(point.X) <= epsilon
               && Math.Abs(point.Y) <= epsilon
               && Math.Abs(point.Z) <= epsilon;
    }

    private static void EnsureSplineEnd(List<XYZ> points, XYZ end, int maxPoints)
    {
        if (points.Count == 0)
        {
            points.Add(end);
            return;
        }

        var last = points[^1];
        if (IsSamePoint(last, end))
        {
            return;
        }

        if (points.Count < maxPoints)
        {
            points.Add(end);
        }
        else
        {
            points[^1] = end;
        }
    }

    private static void TrimSplineClosure(List<XYZ> points, int maxPoints, Spline spline, float tolerance)
    {
        if (points.Count < 2 || !(spline.IsClosed || spline.IsPeriodic))
        {
            return;
        }

        var first = points[0];
        var last = points[^1];
        if (DistanceToSegment(last, first, first) <= tolerance)
        {
            points.RemoveAt(points.Count - 1);
        }

        if (points.Count > maxPoints)
        {
            points.RemoveRange(maxPoints, points.Count - maxPoints);
        }
    }

    private readonly struct SplineSegment
    {
        public double StartT { get; }
        public XYZ Start { get; }
        public double EndT { get; }
        public XYZ End { get; }
        public int Depth { get; }

        public SplineSegment(double startT, XYZ start, double endT, XYZ end, int depth)
        {
            StartT = startT;
            Start = start;
            EndT = endT;
            End = end;
            Depth = depth;
        }
    }

    private static bool IsSamePoint(XYZ left, XYZ right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return dx * dx + dy * dy + dz * dz <= MathHelper.Epsilon * MathHelper.Epsilon;
    }
}
