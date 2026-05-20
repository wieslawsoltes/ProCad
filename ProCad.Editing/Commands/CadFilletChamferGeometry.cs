using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Commands;

internal static class CadFilletChamferGeometry
{
    public static bool TryComputeFillet(
        Line firstLine,
        Line secondLine,
        double radius,
        out CadFilletGeometry geometry,
        out string? error)
    {
        geometry = default;
        error = null;

        if (radius <= Epsilon)
        {
            error = "FILLET radius must be greater than zero.";
            return false;
        }

        if (!TryIntersectInfiniteLines(firstLine.StartPoint, firstLine.EndPoint, secondLine.StartPoint, secondLine.EndPoint, out var intersection))
        {
            error = "FILLET requires non-parallel lines.";
            return false;
        }

        if (!TryCreateLineSide(firstLine, intersection, out var firstSide, out error) ||
            !TryCreateLineSide(secondLine, intersection, out var secondSide, out error))
        {
            return false;
        }

        var dot = Clamp(
            Dot(firstSide.DirectionFromIntersection, secondSide.DirectionFromIntersection),
            -1.0,
            1.0);
        var theta = Math.Acos(dot);
        if (theta <= MinAngleRadians || theta >= Math.PI - MinAngleRadians)
        {
            error = "FILLET requires lines with a valid non-collinear angle.";
            return false;
        }

        var tangentOffset = radius / Math.Tan(theta * 0.5);
        if (!double.IsFinite(tangentOffset) || tangentOffset <= Epsilon)
        {
            error = "FILLET failed to compute tangent offset.";
            return false;
        }

        if (tangentOffset >= firstSide.DistanceToKeep - Epsilon ||
            tangentOffset >= secondSide.DistanceToKeep - Epsilon)
        {
            error = "FILLET radius is too large for the selected line segments.";
            return false;
        }

        var firstTangent = Add(intersection, Scale(firstSide.DirectionFromIntersection, tangentOffset));
        var secondTangent = Add(intersection, Scale(secondSide.DirectionFromIntersection, tangentOffset));

        var bisector = Add(firstSide.DirectionFromIntersection, secondSide.DirectionFromIntersection);
        var bisectorLength = Length(bisector);
        if (bisectorLength <= Epsilon)
        {
            error = "FILLET failed due to unstable bisector.";
            return false;
        }

        bisector = Scale(bisector, 1.0 / bisectorLength);
        var centerOffset = radius / Math.Sin(theta * 0.5);
        var center = Add(intersection, Scale(bisector, centerOffset));

        if (!TryComputeArcAngles(center, firstTangent, secondTangent, out var startAngle, out var endAngle))
        {
            error = "FILLET failed to compute arc angles.";
            return false;
        }

        geometry = CreateFilletGeometry(firstLine, secondLine, firstSide, secondSide, firstTangent, secondTangent, center, radius, startAngle, endAngle);
        return true;
    }

    public static bool TryComputeChamfer(
        Line firstLine,
        Line secondLine,
        double firstDistance,
        double secondDistance,
        out CadChamferGeometry geometry,
        out string? error)
    {
        geometry = default;
        error = null;

        if (firstDistance <= Epsilon || secondDistance <= Epsilon)
        {
            error = "CHAMFER distances must be greater than zero.";
            return false;
        }

        if (!TryIntersectInfiniteLines(firstLine.StartPoint, firstLine.EndPoint, secondLine.StartPoint, secondLine.EndPoint, out var intersection))
        {
            error = "CHAMFER requires non-parallel lines.";
            return false;
        }

        if (!TryCreateLineSide(firstLine, intersection, out var firstSide, out error) ||
            !TryCreateLineSide(secondLine, intersection, out var secondSide, out error))
        {
            return false;
        }

        if (firstDistance >= firstSide.DistanceToKeep - Epsilon ||
            secondDistance >= secondSide.DistanceToKeep - Epsilon)
        {
            error = "CHAMFER distances are too large for the selected line segments.";
            return false;
        }

        var firstChamferPoint = Add(intersection, Scale(firstSide.DirectionFromIntersection, firstDistance));
        var secondChamferPoint = Add(intersection, Scale(secondSide.DirectionFromIntersection, secondDistance));
        if (DistanceSquared(firstChamferPoint, secondChamferPoint) <= Epsilon * Epsilon)
        {
            error = "CHAMFER produced a zero-length edge.";
            return false;
        }

        geometry = CreateChamferGeometry(firstLine, secondLine, firstSide, secondSide, firstChamferPoint, secondChamferPoint);
        return true;
    }

    private static CadFilletGeometry CreateFilletGeometry(
        Line firstLine,
        Line secondLine,
        LineSide firstSide,
        LineSide secondSide,
        XYZ firstTangent,
        XYZ secondTangent,
        XYZ center,
        double radius,
        double startAngle,
        double endAngle)
    {
        var firstNewStart = firstSide.ReplaceStart ? firstTangent : firstLine.StartPoint;
        var firstNewEnd = firstSide.ReplaceStart ? firstLine.EndPoint : firstTangent;

        var secondNewStart = secondSide.ReplaceStart ? secondTangent : secondLine.StartPoint;
        var secondNewEnd = secondSide.ReplaceStart ? secondLine.EndPoint : secondTangent;

        return new CadFilletGeometry(
            firstNewStart,
            firstNewEnd,
            secondNewStart,
            secondNewEnd,
            center,
            radius,
            startAngle,
            endAngle);
    }

    private static CadChamferGeometry CreateChamferGeometry(
        Line firstLine,
        Line secondLine,
        LineSide firstSide,
        LineSide secondSide,
        XYZ firstChamferPoint,
        XYZ secondChamferPoint)
    {
        var firstNewStart = firstSide.ReplaceStart ? firstChamferPoint : firstLine.StartPoint;
        var firstNewEnd = firstSide.ReplaceStart ? firstLine.EndPoint : firstChamferPoint;

        var secondNewStart = secondSide.ReplaceStart ? secondChamferPoint : secondLine.StartPoint;
        var secondNewEnd = secondSide.ReplaceStart ? secondLine.EndPoint : secondChamferPoint;

        return new CadChamferGeometry(
            firstNewStart,
            firstNewEnd,
            secondNewStart,
            secondNewEnd,
            firstChamferPoint,
            secondChamferPoint);
    }

    private static bool TryCreateLineSide(Line line, XYZ intersection, out LineSide side, out string? error)
    {
        side = default;
        error = null;

        var startDistance = Distance(intersection, line.StartPoint);
        var endDistance = Distance(intersection, line.EndPoint);
        if (startDistance <= Epsilon && endDistance <= Epsilon)
        {
            error = "Selected line is degenerate at the intersection point.";
            return false;
        }

        var replaceStart = startDistance <= endDistance;
        var keepPoint = replaceStart ? line.EndPoint : line.StartPoint;
        var direction = Normalize(Subtract(keepPoint, intersection));
        var distanceToKeep = Distance(intersection, keepPoint);
        if (distanceToKeep <= Epsilon)
        {
            error = "Selected line has insufficient length from intersection.";
            return false;
        }

        side = new LineSide(replaceStart, direction, distanceToKeep);
        return true;
    }

    private static bool TryComputeArcAngles(XYZ center, XYZ firstTangent, XYZ secondTangent, out double startAngle, out double endAngle)
    {
        var first = CadGeometryTransform.NormalizeAngle(Math.Atan2(firstTangent.Y - center.Y, firstTangent.X - center.X));
        var second = CadGeometryTransform.NormalizeAngle(Math.Atan2(secondTangent.Y - center.Y, secondTangent.X - center.X));

        var forwardSweep = PositiveAngleDelta(first, second);
        var reverseSweep = PositiveAngleDelta(second, first);

        if (forwardSweep <= Epsilon || reverseSweep <= Epsilon)
        {
            startAngle = 0.0;
            endAngle = 0.0;
            return false;
        }

        if (forwardSweep <= reverseSweep)
        {
            startAngle = first;
            endAngle = second;
        }
        else
        {
            startAngle = second;
            endAngle = first;
        }

        return true;
    }

    private static bool TryIntersectInfiniteLines(
        XYZ firstStart,
        XYZ firstEnd,
        XYZ secondStart,
        XYZ secondEnd,
        out XYZ intersection)
    {
        var rX = firstEnd.X - firstStart.X;
        var rY = firstEnd.Y - firstStart.Y;
        var sX = secondEnd.X - secondStart.X;
        var sY = secondEnd.Y - secondStart.Y;

        var cross = (rX * sY) - (rY * sX);
        if (Math.Abs(cross) <= Epsilon)
        {
            intersection = XYZ.Zero;
            return false;
        }

        var qpX = secondStart.X - firstStart.X;
        var qpY = secondStart.Y - firstStart.Y;
        var t = ((qpX * sY) - (qpY * sX)) / cross;

        intersection = new XYZ(
            firstStart.X + t * rX,
            firstStart.Y + t * rY,
            firstStart.Z);
        return true;
    }

    private static XYZ Add(XYZ a, XYZ b)
    {
        return new XYZ(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    private static XYZ Subtract(XYZ a, XYZ b)
    {
        return new XYZ(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    private static XYZ Scale(XYZ value, double scalar)
    {
        return new XYZ(value.X * scalar, value.Y * scalar, value.Z * scalar);
    }

    private static double Dot(XYZ a, XYZ b)
    {
        return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
    }

    private static double Length(XYZ value)
    {
        return Math.Sqrt(Dot(value, value));
    }

    private static XYZ Normalize(XYZ value)
    {
        var length = Length(value);
        if (length <= Epsilon)
        {
            return XYZ.Zero;
        }

        return Scale(value, 1.0 / length);
    }

    private static double Distance(XYZ a, XYZ b)
    {
        return Math.Sqrt(DistanceSquared(a, b));
    }

    private static double DistanceSquared(XYZ a, XYZ b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private static double Clamp(double value, double min, double max)
    {
        return value < min ? min : (value > max ? max : value);
    }

    private static double PositiveAngleDelta(double start, double end)
    {
        var delta = end - start;
        while (delta < 0.0)
        {
            delta += Math.PI * 2.0;
        }

        while (delta >= Math.PI * 2.0)
        {
            delta -= Math.PI * 2.0;
        }

        return delta;
    }

    private readonly record struct LineSide(bool ReplaceStart, XYZ DirectionFromIntersection, double DistanceToKeep);

    private const double Epsilon = 1e-8;
    private const double MinAngleRadians = 1e-6;
}

internal readonly record struct CadFilletGeometry(
    XYZ FirstNewStart,
    XYZ FirstNewEnd,
    XYZ SecondNewStart,
    XYZ SecondNewEnd,
    XYZ ArcCenter,
    double ArcRadius,
    double ArcStartAngle,
    double ArcEndAngle);

internal readonly record struct CadChamferGeometry(
    XYZ FirstNewStart,
    XYZ FirstNewEnd,
    XYZ SecondNewStart,
    XYZ SecondNewEnd,
    XYZ ChamferStart,
    XYZ ChamferEnd);
