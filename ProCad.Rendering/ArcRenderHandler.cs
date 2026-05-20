using ACadSharp.Entities;
using CSMath;

namespace ProCad.Rendering;

public sealed class ArcRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is Arc;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var arc = (Arc)entity;
        var builder = context.GetLayerBuilder(arc);
        var color = context.ResolveEntityColor(arc);
        var thickness = context.ResolveLineWeight(arc);
        var lineCap = context.ResolveLineCap(arc);
        var lineJoin = context.ResolveLineJoin(arc);
        var pattern = context.ResolveLinePattern(arc);

        var isDefaultNormal = arc.Normal.IsZero() || arc.Normal.Equals(XYZ.AxisZ);
        if (pattern.IsContinuous && isDefaultNormal && RenderTransformUtils.IsIdentity(transform))
        {
            builder.Add(new RenderArc(
                RenderTransformUtils.ToVector2(arc.Center),
                (float)arc.Radius,
                (float)arc.StartAngle,
                (float)arc.EndAngle,
                color,
                thickness,
                lineCap,
                lineJoin));
            return;
        }

        var points = context.GeometrySampler.SampleArc(arc, context.Settings.ResolveCirclePrecision());
        RenderPrimitiveBuilder.AddSampled(
            builder,
            points,
            transform,
            isClosed: false,
            color,
            thickness,
            lineCap,
            lineJoin,
            pattern,
            context.ShapeResolver,
            context.Settings);
    }
}
