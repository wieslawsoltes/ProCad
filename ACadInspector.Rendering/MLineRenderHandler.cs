using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class MLineRenderHandler : IRenderEntityHandler
{
    private const float Epsilon = 0.000001f;

    public bool CanHandle(Entity entity) => entity is MLine;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var mline = (MLine)entity;
        if (mline.Vertices.Count < 2)
        {
            return;
        }

        var style = mline.Style ?? MLineStyle.Default;
        var elements = style.Elements.ToList();
        if (elements.Count == 0)
        {
            return;
        }

        var builder = context.GetLayerBuilder(mline);
        var thickness = context.ResolveLineWeight(mline);
        var lineCap = context.ResolveLineCap(mline);
        var lineJoin = context.ResolveLineJoin(mline);
        var pattern = context.ResolveLinePattern(mline);
        var transformWithNormal = RenderTransformUtils.CombineWithNormal(transform, mline.Normal);
        var scale = (float)Math.Max(mline.ScaleFactor, 1e-6);

        var baseOffsets = elements.Select(e => (float)e.Offset * scale).ToArray();
        var justificationShift = ResolveJustificationShift(mline.Justification, baseOffsets);
        var offsets = baseOffsets.Select(offset => offset + justificationShift).ToArray();

        var minOffset = offsets.Min();
        var maxOffset = offsets.Max();

        if (style.Flags.HasFlag(MLineStyleFlags.FillOn) && context.Settings.FillMode)
        {
            TryAddFill(builder, mline, transformWithNormal, minOffset, maxOffset, ResolveFillColor(style, context));
        }

        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            var color = ResolveElementColor(element, context, mline);
            var elementOffsets = BuildOffsetPolyline(mline, transformWithNormal, offsets[i], i);
            if (elementOffsets.Count < 2)
            {
                continue;
            }

            RenderLinePatternStroker.AddPolyline(
                builder,
                elementOffsets,
                mline.Flags.HasFlag(MLineFlags.Closed),
                pattern,
                color,
                thickness,
                lineCap,
                lineJoin,
                context.ShapeResolver,
                context.Settings);
        }

        AddCaps(builder, mline, transformWithNormal, minOffset, maxOffset, style, context);
    }

    private static List<Vector2> BuildOffsetPolyline(MLine mline, Transform transform, float offset, int elementIndex)
    {
        var points = new List<Vector2>(mline.Vertices.Count);
        foreach (var vertex in mline.Vertices)
        {
            var offsetValue = ResolveVertexOffset(vertex, offset, elementIndex);
            var miter = ResolveMiter(vertex);
            var point = vertex.Position + new XYZ(miter.X * offsetValue, miter.Y * offsetValue, miter.Z * offsetValue);
            points.Add(RenderTransformUtils.Apply(transform, point));
        }

        return points;
    }

    private static float ResolveVertexOffset(MLine.Vertex vertex, float fallback, int elementIndex)
    {
        if (vertex.Segments is null || elementIndex < 0 || elementIndex >= vertex.Segments.Count)
        {
            return fallback;
        }

        var segment = vertex.Segments[elementIndex];
        if (segment.Parameters.Count > 0)
        {
            return (float)segment.Parameters[0];
        }

        return fallback;
    }

    private static XYZ ResolveMiter(MLine.Vertex vertex)
    {
        var miter = vertex.Miter;
        if (!miter.IsZero())
        {
            var length = miter.GetLength();
            if (length > Epsilon)
            {
                return miter / length;
            }
        }

        var dir = vertex.Direction;
        if (dir.IsZero())
        {
            return XYZ.AxisY;
        }

        var norm = new XYZ(-dir.Y, dir.X, 0);
        var normLen = norm.GetLength();
        if (normLen > Epsilon)
        {
            return norm / normLen;
        }

        return XYZ.AxisY;
    }

    private static float ResolveJustificationShift(MLineJustification justification, float[] offsets)
    {
        if (offsets.Length == 0)
        {
            return 0f;
        }

        var min = offsets.Min();
        var max = offsets.Max();
        return justification switch
        {
            MLineJustification.Top => -max,
            MLineJustification.Bottom => -min,
            _ => 0f
        };
    }

    private static RenderColor ResolveElementColor(MLineStyle.Element element, RenderBuildContext context, Entity entity)
    {
        var color = element.Color;
        if (color.IsByLayer || color.IsByBlock)
        {
            return context.ResolveEntityColor(entity);
        }

        return new RenderColor(color.R, color.G, color.B, 255);
    }

    private static RenderColor ResolveFillColor(MLineStyle style, RenderBuildContext context)
    {
        var color = style.FillColor;
        if (color.IsByLayer || color.IsByBlock)
        {
            return context.Settings.FallbackColor;
        }

        return new RenderColor(color.R, color.G, color.B, 255);
    }

    private static void TryAddFill(
        RenderLayerBuilder builder,
        MLine mline,
        Transform transform,
        float minOffset,
        float maxOffset,
        RenderColor color)
    {
        if (mline.Vertices.Count < 2)
        {
            return;
        }

        var minPoints = BuildOffsetPolyline(mline, transform, minOffset, elementIndex: 0);
        var maxPoints = BuildOffsetPolyline(mline, transform, maxOffset, elementIndex: 0);
        if (minPoints.Count < 2 || maxPoints.Count < 2)
        {
            return;
        }

        var loop = new List<Vector2>(minPoints.Count + maxPoints.Count);
        loop.AddRange(maxPoints);
        for (var i = minPoints.Count - 1; i >= 0; i--)
        {
            loop.Add(minPoints[i]);
        }

        builder.Add(new RenderFill(loop, color));
    }

    private static void AddCaps(
        RenderLayerBuilder builder,
        MLine mline,
        Transform transform,
        float minOffset,
        float maxOffset,
        MLineStyle style,
        RenderBuildContext context)
    {
        if (mline.Vertices.Count < 2)
        {
            return;
        }

        if (mline.Flags.HasFlag(MLineFlags.Closed))
        {
            return;
        }

        var thickness = context.ResolveLineWeight(mline);
        var lineCap = context.ResolveLineCap(mline);
        var lineJoin = context.ResolveLineJoin(mline);
        var color = context.ResolveEntityColor(mline);

        if (!mline.Flags.HasFlag(MLineFlags.NoStartCaps) && style.Flags.HasFlag(MLineStyleFlags.StartSquareCap))
        {
            AddCapLine(builder, mline.Vertices[0], transform, minOffset, maxOffset, color, thickness, lineCap, lineJoin);
        }

        if (!mline.Flags.HasFlag(MLineFlags.NoEndCaps) && style.Flags.HasFlag(MLineStyleFlags.EndSquareCap))
        {
            AddCapLine(builder, mline.Vertices[^1], transform, minOffset, maxOffset, color, thickness, lineCap, lineJoin);
        }
    }

    private static void AddCapLine(
        RenderLayerBuilder builder,
        MLine.Vertex vertex,
        Transform transform,
        float minOffset,
        float maxOffset,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin)
    {
        var miter = ResolveMiter(vertex);
        var minPoint = vertex.Position + new XYZ(miter.X * minOffset, miter.Y * minOffset, miter.Z * minOffset);
        var maxPoint = vertex.Position + new XYZ(miter.X * maxOffset, miter.Y * maxOffset, miter.Z * maxOffset);
        var start = RenderTransformUtils.Apply(transform, minPoint);
        var end = RenderTransformUtils.Apply(transform, maxPoint);
        builder.Add(new RenderLine(start, end, color, thickness, lineCap, lineJoin));
    }
}
