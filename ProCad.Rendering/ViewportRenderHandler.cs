using System.Numerics;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Rendering;

/// <summary>
/// Renders viewport boundaries in paper space.
/// </summary>
public sealed class ViewportRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is Viewport;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var viewport = (Viewport)entity;
        if (ViewportRenderUtils.IsPaperViewport(viewport))
        {
            return;
        }

        if (viewport.Status.HasFlag(ViewportStatusFlags.ViewportOff))
        {
            return;
        }

        var width = (float)viewport.Width;
        var height = (float)viewport.Height;
        if (width <= 0f || height <= 0f)
        {
            return;
        }

        var builder = context.GetLayerBuilder(viewport);
        var color = context.ResolveEntityColor(viewport);
        var thickness = context.ResolveLineWeight(viewport);
        var lineCap = context.ResolveLineCap(viewport);
        var lineJoin = context.ResolveLineJoin(viewport);

        if (!TryAppendBoundary(viewport, transform, context, builder, color, thickness, lineCap, lineJoin))
        {
            var center = RenderTransformUtils.Apply(transform, viewport.Center);
            var halfW = width * 0.5f;
            var halfH = height * 0.5f;
            var points = new[]
            {
                new Vector2(center.X - halfW, center.Y - halfH),
                new Vector2(center.X + halfW, center.Y - halfH),
                new Vector2(center.X + halfW, center.Y + halfH),
                new Vector2(center.X - halfW, center.Y + halfH)
            };

            builder.Add(new RenderPolyline(points, isClosed: true, color, thickness, lineCap, lineJoin));
        }
    }

    private static bool TryAppendBoundary(
        Viewport viewport,
        Transform transform,
        RenderBuildContext context,
        RenderLayerBuilder builder,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin)
    {
        if (viewport.Boundary is null ||
            !viewport.Status.HasFlag(ViewportStatusFlags.NonRectangularClipping))
        {
            return false;
        }

        IReadOnlyList<CSMath.XYZ>? points = viewport.Boundary switch
        {
            IPolyline polyline => context.GeometrySampler.SamplePolyline(polyline, context.Settings.ResolvePolylineArcPrecision()),
            Circle circle => context.GeometrySampler.SampleCircle(circle, context.Settings.ResolveCirclePrecision()),
            Ellipse ellipse => context.GeometrySampler.SampleEllipse(ellipse, context.Settings.ResolveCirclePrecision()),
            Spline spline => context.GeometrySampler.SampleSpline(spline, context.Settings.ResolveSplinePrecision()),
            _ => null
        };

        if (points is null || points.Count < 3)
        {
            return false;
        }

        var loop = new List<Vector2>(points.Count);
        foreach (var point in points)
        {
            loop.Add(RenderTransformUtils.Apply(transform, point));
        }

        builder.Add(new RenderPolyline(loop, isClosed: true, color, thickness, lineCap, lineJoin));
        return true;
    }
}
