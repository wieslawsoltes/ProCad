using System.Collections.Generic;
using System.Numerics;

namespace ProCad.Rendering;

/// <summary>
/// Represents cached vector geometry for a SHX/SHP shape.
/// </summary>
public sealed class RenderShapeGeometry
{
    /// <summary>
    /// Gets the shape contours as open polylines.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Vector2>> Contours { get; }

    /// <summary>
    /// Gets the bounding box of the shape.
    /// </summary>
    public RenderBounds Bounds { get; }

    /// <summary>
    /// Creates a new shape geometry instance.
    /// </summary>
    public RenderShapeGeometry(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        Contours = contours;
        Bounds = ComputeBounds(contours);
    }

    private static RenderBounds ComputeBounds(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var bounds = RenderBounds.Empty;
        foreach (var contour in contours)
        {
            foreach (var point in contour)
            {
                bounds = bounds.Expand(point);
            }
        }

        return bounds;
    }
}
