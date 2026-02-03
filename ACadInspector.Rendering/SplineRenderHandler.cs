using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class SplineRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is Spline;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var spline = (Spline)entity;
        var splineTransform = RenderTransformUtils.CombineWithNormal(transform, spline.Normal);
        var builder = context.GetLayerBuilder(spline);
        var color = context.ResolveEntityColor(spline);
        var thickness = context.ResolveLineWeight(spline);
        var lineCap = context.ResolveLineCap(spline);
        var lineJoin = context.ResolveLineJoin(spline);
        var pattern = context.ResolveLinePattern(spline);

        var points = context.GeometrySampler.SampleSpline(spline, context.Settings.ResolveSplinePrecision());
        RenderPrimitiveBuilder.AddSampled(
            builder,
            points,
            splineTransform,
            spline.IsClosed,
            color,
            thickness,
            lineCap,
            lineJoin,
            pattern,
            context.ShapeResolver,
            context.Settings);
    }
}
