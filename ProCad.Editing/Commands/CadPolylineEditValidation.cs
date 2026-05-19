using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Commands;

internal static class CadPolylineEditValidation
{
    private const double GeometryTolerance = 1e-9;
    private const double NormalTolerance = 1e-6;

    public static bool TryValidateLinearPolyline(LwPolyline polyline, string commandName, out string? error)
    {
        error = null;
        if (!HasSupportedNormal(polyline))
        {
            error = $"{commandName} currently supports only world-XY LWPOLYLINE targets.";
            return false;
        }

        for (var index = 0; index < polyline.Vertices.Count; index++)
        {
            var vertex = polyline.Vertices[index];
            if (Math.Abs(vertex.Bulge) > GeometryTolerance)
            {
                error = $"{commandName} currently does not support LWPOLYLINE arc segments.";
                return false;
            }

            if (Math.Abs(vertex.StartWidth) > GeometryTolerance ||
                Math.Abs(vertex.EndWidth) > GeometryTolerance)
            {
                error = $"{commandName} currently does not support variable-width LWPOLYLINE segments.";
                return false;
            }
        }

        return true;
    }

    private static bool HasSupportedNormal(LwPolyline polyline)
    {
        var normal = polyline.Normal;
        if (normal.IsZero())
        {
            return true;
        }

        var normalized = normal.Normalize();
        return Math.Abs(normalized.X) <= NormalTolerance &&
               Math.Abs(normalized.Y) <= NormalTolerance &&
               normalized.Z > 0.0;
    }
}
