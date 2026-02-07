using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Editing.Commands;

internal static class CadHatchGeometry
{
    public static bool TryGetLoops(Hatch hatch, out IReadOnlyList<IReadOnlyList<XYZ>> loops, out string? error)
    {
        ArgumentNullException.ThrowIfNull(hatch);

        var collected = new List<IReadOnlyList<XYZ>>();
        foreach (var path in hatch.Paths)
        {
            if (path.Edges.Count == 1 && path.Edges[0] is Hatch.BoundaryPath.Polyline polyline)
            {
                var vertices = polyline.Vertices
                    .Select(vertex => new XYZ(vertex.X, vertex.Y, hatch.Elevation))
                    .ToArray();
                if (vertices.Length >= 3)
                {
                    collected.Add(vertices);
                }

                continue;
            }

            var sampled = path.GetPoints()
                .Select(point => new XYZ(point.X, point.Y, hatch.Elevation))
                .ToArray();
            if (sampled.Length < 3)
            {
                continue;
            }

            if (PointsApproximatelyEqual(sampled[0], sampled[^1]))
            {
                sampled = sampled[..^1];
            }

            if (sampled.Length >= 3)
            {
                collected.Add(sampled);
            }
        }

        if (collected.Count == 0)
        {
            loops = Array.Empty<IReadOnlyList<XYZ>>();
            error = "No valid hatch loops could be extracted.";
            return false;
        }

        loops = collected;
        error = null;
        return true;
    }

    public static IReadOnlyList<IReadOnlyList<XYZ>> TranslateLoops(IReadOnlyList<IReadOnlyList<XYZ>> loops, XYZ delta)
    {
        return loops
            .Select(loop => loop.Select(point => CadGeometryTransform.Translate(point, delta)).ToArray())
            .ToArray();
    }

    public static IReadOnlyList<IReadOnlyList<XYZ>> RotateLoops(
        IReadOnlyList<IReadOnlyList<XYZ>> loops,
        XYZ center,
        double angleRadians)
    {
        return loops
            .Select(loop => loop.Select(point => CadGeometryTransform.RotateAroundZ(point, center, angleRadians)).ToArray())
            .ToArray();
    }

    public static IReadOnlyList<IReadOnlyList<XYZ>> ScaleLoops(
        IReadOnlyList<IReadOnlyList<XYZ>> loops,
        XYZ center,
        double factor)
    {
        return loops
            .Select(loop => loop.Select(point => CadGeometryTransform.Scale(point, center, factor)).ToArray())
            .ToArray();
    }

    public static IReadOnlyList<IReadOnlyList<XYZ>> MirrorLoops(
        IReadOnlyList<IReadOnlyList<XYZ>> loops,
        XYZ axisStart,
        XYZ axisEnd)
    {
        return loops
            .Select(loop => loop.Select(point => CadGeometryTransform.MirrorAcrossLine(point, axisStart, axisEnd)).ToArray())
            .ToArray();
    }

    public static string ResolvePatternName(Hatch hatch)
    {
        ArgumentNullException.ThrowIfNull(hatch);

        return hatch.IsSolid
            ? "SOLID"
            : string.IsNullOrWhiteSpace(hatch.Pattern?.Name)
                ? "ANSI31"
                : hatch.Pattern.Name;
    }

    private static bool PointsApproximatelyEqual(XYZ first, XYZ second)
    {
        return Math.Abs(first.X - second.X) < 1e-6 &&
               Math.Abs(first.Y - second.Y) < 1e-6 &&
               Math.Abs(first.Z - second.Z) < 1e-6;
    }
}
