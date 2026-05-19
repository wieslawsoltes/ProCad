using ACadSharp.Entities;
using CSMath;

namespace ProCad.Rendering;

public sealed class EllipseRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is Ellipse;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var ellipse = (Ellipse)entity;
        var builder = context.GetLayerBuilder(ellipse);
        var color = context.ResolveEntityColor(ellipse);
        var thickness = context.ResolveLineWeight(ellipse);
        var lineCap = context.ResolveLineCap(ellipse);
        var lineJoin = context.ResolveLineJoin(ellipse);
        var pattern = context.ResolveLinePattern(ellipse);

        var points = context.GeometrySampler.SampleEllipse(ellipse, context.Settings.ResolveCirclePrecision());
        RenderPrimitiveBuilder.AddSampled(
            builder,
            points,
            transform,
            ellipse.IsFullEllipse,
            color,
            thickness,
            lineCap,
            lineJoin,
            pattern,
            context.ShapeResolver,
            context.Settings);
    }
}
