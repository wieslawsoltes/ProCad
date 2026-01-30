using System.Collections.Generic;
using CSMath;

namespace ACadInspector.Rendering;

internal static class RenderPrimitiveBuilder
{
    public static void AddSampled(
        RenderLayerBuilder builder,
        IEnumerable<XYZ> points,
        Transform transform,
        bool isClosed,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        RenderLinePattern pattern,
        IRenderShapeResolver shapeResolver,
        CadRenderSceneSettings settings)
    {
        var capacity = points is ICollection<XYZ> collection
            ? collection.Count
            : 0;
        var list = capacity > 0
            ? new List<System.Numerics.Vector2>(capacity)
            : new List<System.Numerics.Vector2>();
        var depths = capacity > 0
            ? new List<float>(capacity)
            : new List<float>();

        if (points is IReadOnlyList<XYZ> pointList)
        {
            for (var i = 0; i < pointList.Count; i++)
            {
                var transformed = RenderTransformUtils.Apply3D(transform, pointList[i]);
                list.Add(RenderTransformUtils.ToVector2(transformed));
                depths.Add((float)transformed.Z);
            }
        }
        else
        {
            foreach (var point in points)
            {
                var transformed = RenderTransformUtils.Apply3D(transform, point);
                list.Add(RenderTransformUtils.ToVector2(transformed));
                depths.Add((float)transformed.Z);
            }
        }

        if (list.Count == 0)
        {
            return;
        }

        if (list.Count == 1)
        {
            builder.Add(new RenderPoint(list[0], color, thickness, lineCap, lineJoin));
            return;
        }

        RenderLinePatternStroker.AddPolyline(
            builder,
            list,
            isClosed,
            pattern,
            color,
            thickness,
            lineCap,
            lineJoin,
            shapeResolver,
            settings,
            depths);
    }
}
