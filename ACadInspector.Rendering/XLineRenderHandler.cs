using System.Numerics;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class XLineRenderHandler : IRenderEntityHandler
{
    private const float Epsilon = 0.000001f;

    public bool CanHandle(Entity entity) => entity is XLine;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var xline = (XLine)entity;
        var builder = context.GetLayerBuilder(xline);
        var color = context.ResolveEntityColor(xline);
        var thickness = context.ResolveLineWeight(xline);
        var lineCap = context.ResolveLineCap(xline);
        var lineJoin = context.ResolveLineJoin(xline);

        var origin = RenderTransformUtils.Apply(transform, xline.FirstPoint);
        var direction = ResolveDirection(transform, xline.FirstPoint, xline.Direction);
        if (direction.LengthSquared() <= Epsilon * Epsilon)
        {
            return;
        }

        var bounds = RenderViewBoundsResolver.Resolve(context.Document, context.Settings);
        if (!RenderLineClipper.TryClipInfiniteLine(origin, direction, bounds, isRay: false, out var start, out var end))
        {
            return;
        }

        builder.Add(new RenderLine(start, end, color, thickness, lineCap, lineJoin));
    }

    private static Vector2 ResolveDirection(Transform transform, XYZ origin, XYZ direction)
    {
        var worldOrigin = RenderTransformUtils.Apply(transform, origin);
        var worldTarget = RenderTransformUtils.Apply(transform, origin + direction);
        return worldTarget - worldOrigin;
    }
}
