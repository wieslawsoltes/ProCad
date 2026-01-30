using System.Collections.Generic;
using System.Numerics;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class PolylineRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is IPolyline;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var polyline = (IPolyline)entity;
        var builder = context.GetLayerBuilder(entity);
        var color = context.ResolveEntityColor(entity);
        var thickness = context.ResolveLineWeight(entity);
        var lineCap = context.ResolveLineCap(entity);
        var lineJoin = context.ResolveLineJoin(entity);
        var pattern = context.ResolveLinePattern(entity);

        var points = context.GeometrySampler.SamplePolyline(polyline, context.Settings.ResolvePolylineArcPrecision());
        if (polyline.IsClosed && context.Document.Header.FillMode)
        {
            AddSolidFill(builder, points, transform, color);
        }
        RenderPrimitiveBuilder.AddSampled(
            builder,
            points,
            transform,
            polyline.IsClosed,
            color,
            thickness,
            lineCap,
            lineJoin,
            pattern,
            context.ShapeResolver,
            context.Settings);
    }

    private static void AddSolidFill(
        RenderLayerBuilder builder,
        IReadOnlyList<XYZ> points,
        Transform transform,
        RenderColor color)
    {
        if (points.Count < 3)
        {
            return;
        }

        var fillPoints = new List<Vector2>(points.Count);
        foreach (var point in points)
        {
            fillPoints.Add(RenderTransformUtils.Apply(transform, point));
        }

        TrimDuplicateEnd(fillPoints);
        if (fillPoints.Count < 3)
        {
            return;
        }

        builder.Add(new RenderFill(fillPoints, color));
    }

    private static void TrimDuplicateEnd(List<Vector2> points)
    {
        if (points.Count < 2)
        {
            return;
        }

        var first = points[0];
        for (var i = points.Count - 1; i > 0; i--)
        {
            var delta = points[i] - first;
            if (delta.LengthSquared() <= 0.0001f * 0.0001f)
            {
                points.RemoveAt(i);
            }
            else
            {
                break;
            }
        }
    }
}
