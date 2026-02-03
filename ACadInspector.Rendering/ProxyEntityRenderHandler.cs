using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class ProxyEntityRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is ProxyEntity;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        context.Diagnostics.TrackUnsupported(entity);

        if (!context.Settings.IncludeUnsupportedAsPoints)
        {
            return;
        }

        var bounds = entity.GetBoundingBox();
        if (bounds.Extent == BoundingBoxExtent.Infinite)
        {
            return;
        }

        var center = bounds.Extent == BoundingBoxExtent.Null ? XYZ.Zero : bounds.Center;
        var location = RenderTransformUtils.Apply(transform, center);

        var builder = context.GetLayerBuilder(entity);
        var color = context.ResolveEntityColor(entity);
        var thickness = context.ResolveLineWeight(entity);
        var lineCap = context.ResolveLineCap(entity);
        var lineJoin = context.ResolveLineJoin(entity);
        builder.Add(new RenderPoint(
            location,
            color,
            thickness,
            lineCap,
            lineJoin,
            context.Settings.PointDisplayMode,
            context.Settings.PointDisplaySize));
    }
}
