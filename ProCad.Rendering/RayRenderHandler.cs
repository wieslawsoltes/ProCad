using System.Numerics;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Rendering;

public sealed class RayRenderHandler : IRenderEntityHandler
{
    private const float Epsilon = 0.000001f;

    public bool CanHandle(Entity entity) => entity is Ray;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var ray = (Ray)entity;
        var builder = context.GetLayerBuilder(ray);
        var color = context.ResolveEntityColor(ray);
        var thickness = context.ResolveLineWeight(ray);
        var lineCap = context.ResolveLineCap(ray);
        var lineJoin = context.ResolveLineJoin(ray);

        var origin = RenderTransformUtils.Apply(transform, ray.StartPoint);
        var direction = ResolveDirection(transform, ray.StartPoint, ray.Direction);
        if (direction.LengthSquared() <= Epsilon * Epsilon)
        {
            return;
        }

        var bounds = RenderViewBoundsResolver.Resolve(context.Document, context.Settings);
        if (!RenderLineClipper.TryClipInfiniteLine(origin, direction, bounds, isRay: true, out var start, out var end))
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
