using System.Collections.Generic;
using System.Numerics;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

/// <summary>
/// Renders solid entities as filled quadrilaterals.
/// </summary>
public sealed class SolidRenderHandler : IRenderEntityHandler
{
    private const float Epsilon = 0.0001f;

    public bool CanHandle(Entity entity) => entity is Solid;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var solid = (Solid)entity;
        var points = new List<Vector2>
        {
            RenderTransformUtils.Apply(transform, solid.FirstCorner),
            RenderTransformUtils.Apply(transform, solid.SecondCorner),
            RenderTransformUtils.Apply(transform, solid.ThirdCorner),
            RenderTransformUtils.Apply(transform, solid.FourthCorner)
        };

        TrimDuplicateEnd(points);
        if (points.Count < 3)
        {
            return;
        }

        var builder = context.GetLayerBuilder(solid);
        var color = context.ResolveEntityColor(solid);
        builder.Add(new RenderFill(points, color));
    }

    private static void TrimDuplicateEnd(List<Vector2> points)
    {
        if (points.Count < 2)
        {
            return;
        }

        var first = points[0];
        for (var i = points.Count - 1; i > 0; i--)
        {
            var delta = points[i] - first;
            if (delta.LengthSquared() <= Epsilon * Epsilon)
            {
                points.RemoveAt(i);
            }
            else
            {
                break;
            }
        }
    }
}
