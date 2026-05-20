using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Commands;

internal static class CadGeometryTransform
{
    public static XYZ Translate(XYZ value, XYZ delta)
    {
        return new XYZ(value.X + delta.X, value.Y + delta.Y, value.Z + delta.Z);
    }

    public static XYZ RotateAroundZ(XYZ value, XYZ center, double angleRadians)
    {
        var dx = value.X - center.X;
        var dy = value.Y - center.Y;
        var cos = Math.Cos(angleRadians);
        var sin = Math.Sin(angleRadians);

        return new XYZ(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos,
            value.Z);
    }

    public static XYZ Scale(XYZ value, XYZ center, double factor)
    {
        return new XYZ(
            center.X + (value.X - center.X) * factor,
            center.Y + (value.Y - center.Y) * factor,
            center.Z + (value.Z - center.Z) * factor);
    }

    public static XYZ MirrorAcrossLine(XYZ value, XYZ axisStart, XYZ axisEnd)
    {
        var vx = axisEnd.X - axisStart.X;
        var vy = axisEnd.Y - axisStart.Y;
        var lengthSquared = (vx * vx) + (vy * vy);
        if (lengthSquared <= double.Epsilon)
        {
            return value;
        }

        var px = value.X - axisStart.X;
        var py = value.Y - axisStart.Y;
        var t = ((px * vx) + (py * vy)) / lengthSquared;
        var projX = axisStart.X + (t * vx);
        var projY = axisStart.Y + (t * vy);

        return new XYZ(
            2.0 * projX - value.X,
            2.0 * projY - value.Y,
            value.Z);
    }

    public static XYZ RotateDirectionAroundZ(XYZ direction, double angleRadians)
    {
        var rotated = RotateAroundZ(direction, XYZ.Zero, angleRadians);
        return NormalizeDirection(rotated);
    }

    public static XYZ RotateVectorAroundZ(XYZ vector, double angleRadians)
    {
        return RotateAroundZ(vector, XYZ.Zero, angleRadians);
    }

    public static XYZ NormalizeDirection(XYZ direction)
    {
        var length = Math.Sqrt(
            (direction.X * direction.X) +
            (direction.Y * direction.Y) +
            (direction.Z * direction.Z));
        if (length <= double.Epsilon)
        {
            return XYZ.Zero;
        }

        return new XYZ(
            direction.X / length,
            direction.Y / length,
            direction.Z / length);
    }

    public static IReadOnlyList<XYZ> ToVertices(LwPolyline polyline)
    {
        return polyline.Vertices
            .Select(vertex => new XYZ(vertex.Location.X, vertex.Location.Y, polyline.Elevation))
            .ToArray();
    }

    public static IReadOnlyList<XYZ> TranslateVertices(IReadOnlyList<XYZ> vertices, XYZ delta)
    {
        return vertices.Select(vertex => Translate(vertex, delta)).ToArray();
    }

    public static IReadOnlyList<XYZ> RotateVertices(IReadOnlyList<XYZ> vertices, XYZ center, double angleRadians)
    {
        return vertices.Select(vertex => RotateAroundZ(vertex, center, angleRadians)).ToArray();
    }

    public static IReadOnlyList<XYZ> ScaleVertices(IReadOnlyList<XYZ> vertices, XYZ center, double factor)
    {
        return vertices.Select(vertex => Scale(vertex, center, factor)).ToArray();
    }

    public static IReadOnlyList<XYZ> MirrorVertices(IReadOnlyList<XYZ> vertices, XYZ axisStart, XYZ axisEnd)
    {
        return vertices.Select(vertex => MirrorAcrossLine(vertex, axisStart, axisEnd)).ToArray();
    }

    public static XYZ MirrorDirection(XYZ direction, XYZ axisStart, XYZ axisEnd)
    {
        var axisVector = new XYZ(axisEnd.X - axisStart.X, axisEnd.Y - axisStart.Y, axisEnd.Z - axisStart.Z);
        var mirrored = MirrorAcrossLine(direction, XYZ.Zero, axisVector);
        return NormalizeDirection(mirrored);
    }

    public static XYZ MirrorVector(XYZ vector, XYZ axisStart, XYZ axisEnd)
    {
        var axisVector = new XYZ(axisEnd.X - axisStart.X, axisEnd.Y - axisStart.Y, axisEnd.Z - axisStart.Z);
        var axisLengthSquared =
            (axisVector.X * axisVector.X) +
            (axisVector.Y * axisVector.Y) +
            (axisVector.Z * axisVector.Z);
        if (axisLengthSquared <= double.Epsilon)
        {
            return vector;
        }

        return MirrorAcrossLine(vector, XYZ.Zero, axisVector);
    }

    public static XYZ ScaleVector(XYZ vector, double factor)
    {
        return new XYZ(vector.X * factor, vector.Y * factor, vector.Z * factor);
    }

    public static (XYZ Center, double StartAngle, double EndAngle) MirrorArc(
        XYZ center,
        double radius,
        double startAngle,
        double endAngle,
        XYZ axisStart,
        XYZ axisEnd)
    {
        var startPoint = new XYZ(
            center.X + radius * Math.Cos(startAngle),
            center.Y + radius * Math.Sin(startAngle),
            center.Z);
        var endPoint = new XYZ(
            center.X + radius * Math.Cos(endAngle),
            center.Y + radius * Math.Sin(endAngle),
            center.Z);

        var mirroredCenter = MirrorAcrossLine(center, axisStart, axisEnd);
        var mirroredStartPoint = MirrorAcrossLine(startPoint, axisStart, axisEnd);
        var mirroredEndPoint = MirrorAcrossLine(endPoint, axisStart, axisEnd);

        // Reflection flips orientation. Swap start/end-derived angles to preserve geometry.
        var mirroredStartAngle = NormalizeAngle(Math.Atan2(
            mirroredEndPoint.Y - mirroredCenter.Y,
            mirroredEndPoint.X - mirroredCenter.X));
        var mirroredEndAngle = NormalizeAngle(Math.Atan2(
            mirroredStartPoint.Y - mirroredCenter.Y,
            mirroredStartPoint.X - mirroredCenter.X));
        return (mirroredCenter, mirroredStartAngle, mirroredEndAngle);
    }

    public static double NormalizeAngle(double angleRadians)
    {
        var angle = angleRadians % (Math.PI * 2.0);
        if (angle < 0)
        {
            angle += Math.PI * 2.0;
        }

        return angle;
    }
}
