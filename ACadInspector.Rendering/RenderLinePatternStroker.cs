using System;
using System.Collections.Generic;
using System.Numerics;

namespace ACadInspector.Rendering;

internal static class RenderLinePatternStroker
{
    private const float Epsilon = 0.0001f;

    public static void AddLine(
        RenderLayerBuilder builder,
        Vector2 start,
        Vector2 end,
        RenderLinePattern pattern,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        IRenderShapeResolver shapeResolver,
        CadRenderSceneSettings settings,
        float? startDepth = null,
        float? endDepth = null)
    {
        if (pattern.IsContinuous)
        {
            builder.Add(new RenderLine(start, end, color, thickness, lineCap, lineJoin, startDepth, endDepth));
            return;
        }

        var points = new[] { start, end };
        var depths = startDepth.HasValue && endDepth.HasValue
            ? new[] { startDepth.Value, endDepth.Value }
            : null;
        AddPolyline(builder, points, isClosed: false, pattern, color, thickness, lineCap, lineJoin, shapeResolver, settings, depths);
    }

    public static void AddPolyline(
        RenderLayerBuilder builder,
        IReadOnlyList<Vector2> points,
        bool isClosed,
        RenderLinePattern pattern,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        IRenderShapeResolver shapeResolver,
        CadRenderSceneSettings settings,
        IReadOnlyList<float>? depths = null)
    {
        if (points.Count < 2)
        {
            return;
        }

        var useDepths = depths is not null && depths.Count == points.Count;
        if (pattern.IsContinuous)
        {
            if (points.Count == 2 && !isClosed)
            {
                var startDepth = useDepths ? depths![0] : (float?)null;
                var endDepth = useDepths ? depths![1] : (float?)null;
                builder.Add(new RenderLine(points[0], points[1], color, thickness, lineCap, lineJoin, startDepth, endDepth));
            }
            else
            {
                builder.Add(new RenderPolyline(points, isClosed, color, thickness, lineCap, lineJoin, useDepths ? depths : null));
            }

            return;
        }

        var segments = pattern.Segments;
        if (segments.Length == 0)
        {
            builder.Add(new RenderPolyline(points, isClosed, color, thickness, lineCap, lineJoin));
            return;
        }

        var patternIndex = 0;
        var patternOffset = 0f;
        var segmentCount = isClosed ? points.Count : points.Count - 1;

        for (var i = 0; i < segmentCount; i++)
        {
            var start = points[i];
            var end = i == points.Count - 1 ? points[0] : points[i + 1];
            var delta = end - start;
            var length = delta.Length();
            if (length <= Epsilon)
            {
                continue;
            }

            var depthStart = useDepths ? depths![i] : 0f;
            var depthEnd = useDepths ? depths![i == points.Count - 1 ? 0 : i + 1] : 0f;
            var direction = delta / length;
            var remaining = length;
            var current = start;
            var travelled = 0f;

            while (remaining > Epsilon)
            {
                var segment = segments[patternIndex];
                var available = segment.Length - patternOffset;
                if (available <= Epsilon)
                {
                    patternIndex = (patternIndex + 1) % segments.Length;
                    patternOffset = 0f;
                    continue;
                }

                var step = MathF.Min(remaining, available);
                var t0 = travelled / length;
                var t1 = (travelled + step) / length;
                if (segment.IsDraw && step > Epsilon)
                {
                    var segStartDepth = useDepths ? Lerp(depthStart, depthEnd, t0) : (float?)null;
                    var segEndDepth = useDepths ? Lerp(depthStart, depthEnd, t1) : (float?)null;
                    builder.Add(new RenderLine(
                        current,
                        current + direction * step,
                        color,
                        thickness,
                        lineCap,
                        lineJoin,
                        segStartDepth,
                        segEndDepth));
                }
                else if (patternOffset <= Epsilon && (segment.IsText || segment.IsShape))
                {
                    AddDecoration(builder, current, direction, segment, color, thickness, lineCap, lineJoin, shapeResolver, settings);
                }

                current += direction * step;
                remaining -= step;
                patternOffset += step;
                travelled += step;

                if (patternOffset >= segment.Length - Epsilon)
                {
                    patternIndex = (patternIndex + 1) % segments.Length;
                    patternOffset = 0f;
                }
            }
        }
    }

    private static void AddDecoration(
        RenderLayerBuilder builder,
        Vector2 position,
        Vector2 direction,
        RenderLinePatternSegment segment,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        IRenderShapeResolver shapeResolver,
        CadRenderSceneSettings settings)
    {
        if (segment.IsText)
        {
            AddTextDecoration(builder, position, direction, segment, color);
            return;
        }

        if (segment.IsShape)
        {
            AddShapeDecoration(builder, position, direction, segment, color, thickness, lineCap, lineJoin, shapeResolver, settings);
        }
    }

    private static void AddTextDecoration(
        RenderLayerBuilder builder,
        Vector2 position,
        Vector2 direction,
        RenderLinePatternSegment segment,
        RenderColor color)
    {
        if (string.IsNullOrWhiteSpace(segment.Text) || segment.FontSize <= 0f)
        {
            return;
        }

        var normal = new Vector2(-direction.Y, direction.X);
        var anchor = position + direction * segment.Offset.X + normal * segment.Offset.Y;
        var offset = new Vector2(-segment.LayoutWidth * 0.5f, -segment.LayoutHeight * 0.5f);
        var rotation = segment.RotationIsAbsolute
            ? segment.Rotation
            : MathF.Atan2(direction.Y, direction.X) + segment.Rotation;

        builder.Add(new RenderText(
            segment.Text,
            anchor,
            offset,
            segment.LayoutWidth,
            segment.LayoutHeight,
            segment.FontSize,
            segment.WidthFactor,
            rotation,
            segment.ObliqueAngle,
            segment.IsBold,
            segment.IsItalic,
            segment.MirrorX,
            segment.MirrorY,
            color,
            segment.FontFamily));
    }

    private static void AddShapeDecoration(
        RenderLayerBuilder builder,
        Vector2 position,
        Vector2 direction,
        RenderLinePatternSegment segment,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        IRenderShapeResolver shapeResolver,
        CadRenderSceneSettings settings)
    {
        var normal = new Vector2(-direction.Y, direction.X);
        var anchor = position + direction * segment.Offset.X + normal * segment.Offset.Y;
        if (shapeResolver.TryResolveShape(segment.ShapeFile, segment.ShapeNumber, settings, out var geometry))
        {
            AddShapeGeometry(builder, anchor, direction, segment, geometry, color, thickness, lineCap, lineJoin, settings);
            return;
        }

        AddFallbackShape(builder, anchor, direction, segment, color, thickness, lineCap, lineJoin);
    }

    private static void AddShapeGeometry(
        RenderLayerBuilder builder,
        Vector2 anchor,
        Vector2 direction,
        RenderLinePatternSegment segment,
        RenderShapeGeometry geometry,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        CadRenderSceneSettings settings)
    {
        var baseAngle = segment.RotationIsAbsolute
            ? segment.Rotation
            : MathF.Atan2(direction.Y, direction.X) + segment.Rotation;
        var cos = MathF.Cos(baseAngle);
        var sin = MathF.Sin(baseAngle);

        foreach (var contour in geometry.Contours)
        {
            if (contour.Count == 0)
            {
                continue;
            }

            var transformed = new List<Vector2>(contour.Count);
            foreach (var point in contour)
            {
                var scaled = point * segment.Scale;
                var rotated = new Vector2(
                    scaled.X * cos - scaled.Y * sin,
                    scaled.X * sin + scaled.Y * cos);
                transformed.Add(anchor + rotated);
            }

            if (transformed.Count == 1)
            {
                builder.Add(new RenderPoint(
                    transformed[0],
                    color,
                    thickness,
                    lineCap,
                    lineJoin,
                    settings.PointDisplayMode,
                    settings.PointDisplaySize));
                continue;
            }

            var isClosed = IsClosedContour(transformed);
            if (isClosed)
            {
                transformed.RemoveAt(transformed.Count - 1);
            }

            if (transformed.Count == 2 && !isClosed)
            {
                builder.Add(new RenderLine(transformed[0], transformed[1], color, thickness, lineCap, lineJoin));
            }
            else
            {
                builder.Add(new RenderPolyline(transformed, isClosed, color, thickness, lineCap, lineJoin));
            }
        }
    }

    private static bool IsClosedContour(IReadOnlyList<Vector2> points)
    {
        if (points.Count < 2)
        {
            return false;
        }

        var first = points[0];
        var last = points[^1];
        return Vector2.DistanceSquared(first, last) <= Epsilon * Epsilon;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    private static void AddFallbackShape(
        RenderLayerBuilder builder,
        Vector2 anchor,
        Vector2 direction,
        RenderLinePatternSegment segment,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin)
    {
        var baseAngle = segment.RotationIsAbsolute
            ? segment.Rotation
            : MathF.Atan2(direction.Y, direction.X) + segment.Rotation;
        var axis = new Vector2(MathF.Cos(baseAngle), MathF.Sin(baseAngle));
        var perp = new Vector2(-axis.Y, axis.X);
        var size = MathF.Max(segment.Scale, thickness * 2f);
        var half = size * 0.5f;
        builder.Add(new RenderLine(anchor - axis * half, anchor + axis * half, color, thickness, lineCap, lineJoin));
        builder.Add(new RenderLine(anchor - perp * half, anchor + perp * half, color, thickness, lineCap, lineJoin));
    }
}
