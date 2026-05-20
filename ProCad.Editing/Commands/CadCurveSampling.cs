using CSMath;

namespace ProCad.Editing.Commands;

internal static class CadCurveSampling
{
    public const double Tau = Math.PI * 2.0;

    public static double NormalizeSweep(double start, double end)
    {
        var sweep = end - start;
        while (sweep <= 0.0)
        {
            sweep += Tau;
        }

        return sweep;
    }

    public static bool IsFullSweep(double start, double end, double tolerance = 1e-4)
    {
        var sweep = NormalizeSweep(start, end);
        return Math.Abs(sweep - Tau) <= tolerance;
    }

    public static int ResolveSegmentCount(double sweepRadians, double radius, int minSegments = 8, int maxSegments = 128)
    {
        var sweep = Math.Clamp(Math.Abs(sweepRadians), 0.0, Tau);
        var angleDriven = (int)Math.Ceiling(sweep / (Math.PI / 18.0)); // ~10 degree segments
        var radiusDriven = (int)Math.Ceiling(Math.Abs(radius) / 1.5);
        return Math.Clamp(Math.Max(minSegments, Math.Max(angleDriven, radiusDriven)), minSegments, maxSegments);
    }

    public static IReadOnlyList<XYZ> SampleCircle(XYZ center, double radius, int segmentCount)
    {
        var safeRadius = Math.Abs(radius);
        var count = Math.Max(4, segmentCount);
        var points = new XYZ[count];
        for (var index = 0; index < count; index++)
        {
            var t = Tau * index / count;
            points[index] = new XYZ(
                center.X + safeRadius * Math.Cos(t),
                center.Y + safeRadius * Math.Sin(t),
                center.Z);
        }

        return points;
    }

    public static IReadOnlyList<XYZ> SampleArc(XYZ center, double radius, double startAngle, double endAngle, int segmentCount)
    {
        var safeRadius = Math.Abs(radius);
        var sweep = NormalizeSweep(startAngle, endAngle);
        var count = Math.Max(1, segmentCount);
        var points = new XYZ[count + 1];
        for (var index = 0; index <= count; index++)
        {
            var ratio = index / (double)count;
            var t = startAngle + sweep * ratio;
            points[index] = new XYZ(
                center.X + safeRadius * Math.Cos(t),
                center.Y + safeRadius * Math.Sin(t),
                center.Z);
        }

        return points;
    }

    public static IReadOnlyList<XYZ> SampleEllipse(
        XYZ center,
        XYZ majorAxisEndPoint,
        double radiusRatio,
        double startParameter,
        double endParameter,
        int segmentCount,
        bool closed)
    {
        var majorLength = Math.Sqrt(
            majorAxisEndPoint.X * majorAxisEndPoint.X +
            majorAxisEndPoint.Y * majorAxisEndPoint.Y);
        if (majorLength <= 1e-9)
        {
            return Array.Empty<XYZ>();
        }

        var safeRatio = Math.Max(Math.Abs(radiusRatio), 1e-6);
        var minorLength = majorLength * safeRatio;
        var ux = majorAxisEndPoint.X / majorLength;
        var uy = majorAxisEndPoint.Y / majorLength;
        var major = new XYZ(ux * majorLength, uy * majorLength, 0.0);
        var minor = new XYZ(-uy * minorLength, ux * minorLength, 0.0);

        var count = Math.Max(4, segmentCount);
        var sweep = NormalizeSweep(startParameter, endParameter);
        var sampleCount = closed ? count : count + 1;
        var points = new XYZ[sampleCount];
        for (var index = 0; index < sampleCount; index++)
        {
            var ratio = closed
                ? index / (double)count
                : index / (double)(sampleCount - 1);
            var t = startParameter + sweep * ratio;
            var c = Math.Cos(t);
            var s = Math.Sin(t);
            points[index] = new XYZ(
                center.X + major.X * c + minor.X * s,
                center.Y + major.Y * c + minor.Y * s,
                center.Z);
        }

        return points;
    }
}
