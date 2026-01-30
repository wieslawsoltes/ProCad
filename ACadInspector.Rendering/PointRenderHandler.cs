using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class PointRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is Point;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var point = (Point)entity;
        var builder = context.GetLayerBuilder(point);
        var color = context.ResolveEntityColor(point);
        var thickness = context.ResolveLineWeight(point);
        var lineCap = context.ResolveLineCap(point);
        var lineJoin = context.ResolveLineJoin(point);
        var location = RenderTransformUtils.Apply(transform, point.Location);
        builder.Add(new RenderPoint(location, color, thickness, lineCap, lineJoin));
    }
}
