using System.Collections.Generic;
using ACadSharp.Entities;
using ACadSharp.Extensions;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class PolylineRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is IPolyline;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var polyline = (IPolyline)entity;
        var polylineTransform = RenderTransformUtils.CombineWithNormal(transform, polyline.Normal);
        var builder = context.GetLayerBuilder(entity);
        var color = context.ResolveEntityColor(entity);
        var thickness = context.ResolveLineWeight(entity);
        var lineCap = context.ResolveLineCap(entity);
        var lineJoin = context.ResolveLineJoin(entity);
        var pattern = context.ResolveLinePattern(entity);

        var points = context.GeometrySampler.SamplePolyline(polyline, context.Settings.ResolvePolylineArcPrecision());
        if (context.Settings.FillMode)
        {
            WidePolylineTessellator.TryAddWidePolylineFill(
                builder,
                polyline,
                polylineTransform,
                color,
                context.Settings.ResolvePolylineArcPrecision());
        }

        if (!pattern.IsContinuous && ShouldRestartLinePattern(polyline, context.Settings))
        {
            foreach (var segment in polyline.Explode())
            {
                context.Dispatcher.Append(segment, transform, context);
            }

            return;
        }

        RenderPrimitiveBuilder.AddSampled(
            builder,
            points,
            polylineTransform,
            polyline.IsClosed,
            color,
            thickness,
            lineCap,
            lineJoin,
            pattern,
            context.ShapeResolver,
            context.Settings);
    }

    private static bool ShouldRestartLinePattern(IPolyline polyline, CadRenderSceneSettings settings)
    {
        var defaultContinuous = settings.PolylineLineTypeGeneration;
        return polyline switch
        {
            LwPolyline lw => !(lw.Flags.HasFlag(LwPolylineFlags.Plinegen) || defaultContinuous),
            Polyline<Vertex2D> poly2d => !(poly2d.Flags.HasFlag(PolylineFlags.ContinuousLinetypePattern) || defaultContinuous),
            Polyline<Vertex3D> poly3d => !(poly3d.Flags.HasFlag(PolylineFlags.ContinuousLinetypePattern) || defaultContinuous),
            _ => true
        };
    }
}
