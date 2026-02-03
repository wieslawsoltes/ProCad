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
        var solidTransform = RenderTransformUtils.CombineWithNormal(transform, solid.Normal);
        var points = new List<Vector2>
        {
            RenderTransformUtils.Apply(solidTransform, solid.FirstCorner),
            RenderTransformUtils.Apply(solidTransform, solid.SecondCorner),
            RenderTransformUtils.Apply(solidTransform, solid.ThirdCorner),
            RenderTransformUtils.Apply(solidTransform, solid.FourthCorner)
        };

        TrimDuplicateEnd(points);
        if (points.Count < 3)
        {
            return;
        }

        var builder = context.GetLayerBuilder(solid);
        var color = context.ResolveEntityColor(solid);
        if (context.Settings.FillMode)
        {
            builder.Add(new RenderFill(points, color));
            return;
        }

        var thickness = context.ResolveLineWeight(solid);
        var lineCap = context.ResolveLineCap(solid);
        var lineJoin = context.ResolveLineJoin(solid);
        builder.Add(new RenderPolyline(points, isClosed: true, color, thickness, lineCap, lineJoin));
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
