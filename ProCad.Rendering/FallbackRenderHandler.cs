using ACadSharp.Entities;
using ACadSharp.Extensions;
using CSMath;

namespace ProCad.Rendering;

public sealed class FallbackRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => true;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        context.Diagnostics.TrackUnsupported(entity);

        if (!context.Settings.IncludeUnsupportedAsPoints)
        {
            return;
        }

        var builder = context.GetLayerBuilder(entity);
        var color = context.ResolveEntityColor(entity);
        var thickness = context.ResolveLineWeight(entity);
        var lineCap = context.ResolveLineCap(entity);
        var lineJoin = context.ResolveLineJoin(entity);

        var bounds = entity.GetBoundingBox();
        var location = RenderTransformUtils.Apply(transform, bounds.Center);
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
