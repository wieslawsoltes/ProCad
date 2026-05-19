using ACadSharp.Entities;
using CSMath;

namespace ProCad.Rendering;

public sealed class CircleRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is Circle;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var circle = (Circle)entity;
        var builder = context.GetLayerBuilder(circle);
        var color = context.ResolveEntityColor(circle);
        var thickness = context.ResolveLineWeight(circle);
        var lineCap = context.ResolveLineCap(circle);
        var lineJoin = context.ResolveLineJoin(circle);
        var pattern = context.ResolveLinePattern(circle);

        var isDefaultNormal = circle.Normal.IsZero() || circle.Normal.Equals(XYZ.AxisZ);
        if (pattern.IsContinuous && isDefaultNormal && RenderTransformUtils.IsIdentity(transform))
        {
            builder.Add(new RenderCircle(
                RenderTransformUtils.ToVector2(circle.Center),
                (float)circle.Radius,
                color,
                thickness,
                lineCap,
                lineJoin));
            return;
        }

        var points = context.GeometrySampler.SampleCircle(circle, context.Settings.ResolveCirclePrecision());
        RenderPrimitiveBuilder.AddSampled(
            builder,
            points,
            transform,
            isClosed: true,
            color,
            thickness,
            lineCap,
            lineJoin,
            pattern,
            context.ShapeResolver,
            context.Settings);
    }
}
