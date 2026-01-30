using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

/// <summary>
/// Renders solid hatch boundaries as filled polygons.
/// </summary>
public sealed class HatchRenderHandler : IRenderEntityHandler
{
    private const float Epsilon = 0.0001f;

    public bool CanHandle(Entity entity) => entity is Hatch;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var hatch = (Hatch)entity;
        if (hatch.Paths.Count == 0)
        {
            return;
        }

        var builder = context.GetLayerBuilder(hatch);
        var color = context.ResolveEntityColor(hatch);
        var thickness = context.ResolveLineWeight(hatch);
        var lineCap = context.ResolveLineCap(hatch);
        var lineJoin = context.ResolveLineJoin(hatch);
        var settings = context.Settings;
        var precision = Math.Max(settings.ResolveCirclePrecision(), settings.ResolveSplinePrecision());
        var localLoops = BuildLocalLoops(hatch, precision);
        if (localLoops.Count == 0)
        {
            return;
        }

        var worldLoops = TransformLoops(localLoops, transform);
        var gradient = BuildGradient(hatch.GradientColor, color.A);
        var enableFills = settings.EnableHatchFills;
        var enablePatterns = settings.EnableHatchPatterns && settings.Quality != RenderQuality.Draft;
        var enableGradients = settings.EnableHatchGradients && settings.Quality == RenderQuality.High;

        if (gradient is not null)
        {
            if (enableFills)
            {
                builder.Add(new RenderHatchFill(worldLoops, color, enableGradients ? gradient : null));
            }
            else
            {
                AddBoundaryOutlines(builder, worldLoops, color, thickness, lineCap, lineJoin);
            }

            return;
        }

        if (hatch.IsSolid)
        {
            if (enableFills)
            {
                builder.Add(new RenderHatchFill(worldLoops, color, null));
            }
            else
            {
                AddBoundaryOutlines(builder, worldLoops, color, thickness, lineCap, lineJoin);
            }

            return;
        }

        if (!enablePatterns || hatch.Pattern is null || hatch.Pattern.Lines.Count == 0)
        {
            AddBoundaryOutlines(builder, worldLoops, color, thickness, lineCap, lineJoin);
            return;
        }

        var segments = BuildPatternSegments(hatch.Pattern.Lines, localLoops, transform);
        if (segments.Count == 0)
        {
            AddBoundaryOutlines(builder, worldLoops, color, thickness, lineCap, lineJoin);
            return;
        }

        builder.Add(new RenderHatchPattern(worldLoops, segments, color, thickness, lineCap, lineJoin));
    }

    private static List<List<Vector2>> BuildLocalLoops(Hatch hatch, int precision)
    {
        var loops = new List<List<Vector2>>();
        foreach (var path in hatch.Paths)
        {
            if (path.Flags.HasFlag(BoundaryPathFlags.NotClosed))
            {
                continue;
            }

            if (path.Edges.Count == 0 && path.Entities.Count > 0)
            {
                path.UpdateEdges();
            }

            var points = new List<Vector2>();
            foreach (var point in path.GetPoints(precision))
            {
                points.Add(new Vector2((float)point.X, (float)point.Y));
            }

            if (points.Count < 3)
            {
                continue;
            }

            EnsureClosed(points);
            loops.Add(points);
        }

        return loops;
    }

    private static List<IReadOnlyList<Vector2>> TransformLoops(
        IReadOnlyList<IReadOnlyList<Vector2>> loops,
        Transform transform)
    {
        if (RenderTransformUtils.IsIdentity(transform))
        {
            return loops.Select(loop => (IReadOnlyList<Vector2>)loop).ToList();
        }

        var transformed = new List<IReadOnlyList<Vector2>>(loops.Count);
        foreach (var loop in loops)
        {
            var list = new List<Vector2>(loop.Count);
            foreach (var point in loop)
            {
                var world = RenderTransformUtils.Apply(transform, new XYZ(point.X, point.Y, 0));
                list.Add(world);
            }
            transformed.Add(list);
        }

        return transformed;
    }

    private static List<RenderHatchLineSegment> BuildPatternSegments(
        IReadOnlyList<HatchPattern.Line> patternLines,
        IReadOnlyList<IReadOnlyList<Vector2>> loops,
        Transform transform)
    {
        var bounds = ComputeBounds(loops);
        if (bounds.IsEmpty)
        {
            return new List<RenderHatchLineSegment>();
        }

        var size = bounds.Size;
        var extent = MathF.Sqrt(size.X * size.X + size.Y * size.Y);
        if (extent <= Epsilon)
        {
            return new List<RenderHatchLineSegment>();
        }

        var segments = new List<RenderHatchLineSegment>();
        foreach (var line in patternLines)
        {
            AddPatternLineSegments(segments, line, bounds, extent, transform);
        }

        return segments;
    }

    private static void AddPatternLineSegments(
        List<RenderHatchLineSegment> segments,
        HatchPattern.Line line,
        RenderBounds bounds,
        float extent,
        Transform transform)
    {
        var angle = (float)line.Angle;
        var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var normal = new Vector2(-dir.Y, dir.X);
        var basePoint = new Vector2((float)line.BasePoint.X, (float)line.BasePoint.Y);
        var offset = new Vector2((float)line.Offset.X, (float)line.Offset.Y);
        var spacing = Vector2.Dot(offset, normal);

        var minProj = float.PositiveInfinity;
        var maxProj = float.NegativeInfinity;
        foreach (var corner in GetBoundsCorners(bounds))
        {
            var proj = Vector2.Dot(corner, normal);
            minProj = MathF.Min(minProj, proj);
            maxProj = MathF.Max(maxProj, proj);
        }

        var baseProj = Vector2.Dot(basePoint, normal);
        var startIndex = 0;
        var endIndex = 0;
        if (MathF.Abs(spacing) > Epsilon)
        {
            var kMin = (minProj - baseProj) / spacing;
            var kMax = (maxProj - baseProj) / spacing;
            startIndex = (int)MathF.Floor(MathF.Min(kMin, kMax)) - 1;
            endIndex = (int)MathF.Ceiling(MathF.Max(kMin, kMax)) + 1;
        }

        var half = extent * 1.5f;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var origin = basePoint + offset * i;
            var start = origin - dir * half;
            var end = origin + dir * half;
            AddDashedSegments(segments, start, end, line.DashLengths, transform);
        }
    }

    private static void AddDashedSegments(
        List<RenderHatchLineSegment> segments,
        Vector2 start,
        Vector2 end,
        IReadOnlyList<double> dashes,
        Transform transform)
    {
        var delta = end - start;
        var totalLength = delta.Length();
        if (totalLength <= Epsilon)
        {
            return;
        }

        var direction = delta / totalLength;
        if (dashes.Count == 0)
        {
            segments.Add(TransformSegment(start, end, transform));
            return;
        }

        var index = 0;
        var patternOffset = 0f;
        var current = start;
        var remaining = totalLength;

        while (remaining > Epsilon)
        {
            var rawLength = dashes[index];
            var segmentLength = (float)Math.Abs(rawLength);
            if (segmentLength <= Epsilon)
            {
                index = (index + 1) % dashes.Count;
                patternOffset = 0f;
                continue;
            }

            var available = segmentLength - patternOffset;
            var step = MathF.Min(remaining, available);
            if (rawLength > 0 && step > Epsilon)
            {
                var segmentEnd = current + direction * step;
                segments.Add(TransformSegment(current, segmentEnd, transform));
            }

            current += direction * step;
            remaining -= step;
            patternOffset += step;

            if (patternOffset >= segmentLength - Epsilon)
            {
                index = (index + 1) % dashes.Count;
                patternOffset = 0f;
            }
        }
    }

    private static RenderHatchLineSegment TransformSegment(Vector2 start, Vector2 end, Transform transform)
    {
        if (RenderTransformUtils.IsIdentity(transform))
        {
            return new RenderHatchLineSegment(start, end);
        }

        var worldStart = RenderTransformUtils.Apply(transform, new XYZ(start.X, start.Y, 0));
        var worldEnd = RenderTransformUtils.Apply(transform, new XYZ(end.X, end.Y, 0));
        return new RenderHatchLineSegment(worldStart, worldEnd);
    }

    private static RenderBounds ComputeBounds(IReadOnlyList<IReadOnlyList<Vector2>> loops)
    {
        var bounds = RenderBounds.Empty;
        foreach (var loop in loops)
        {
            foreach (var point in loop)
            {
                bounds = bounds.Expand(point);
            }
        }

        return bounds;
    }

    private static IEnumerable<Vector2> GetBoundsCorners(RenderBounds bounds)
    {
        yield return bounds.Min;
        yield return new Vector2(bounds.Max.X, bounds.Min.Y);
        yield return new Vector2(bounds.Min.X, bounds.Max.Y);
        yield return bounds.Max;
    }

    private static RenderHatchGradient? BuildGradient(HatchGradientPattern gradient, byte alpha)
    {
        if (gradient is null || !gradient.Enabled || gradient.Colors.Count == 0)
        {
            return null;
        }

        var stops = gradient.Colors
            .OrderBy(stop => stop.Value)
            .Select(stop => new RenderHatchGradientStop((float)stop.Value, ToRenderColor(stop.Color, alpha)))
            .ToList();

        if (gradient.IsSingleColorGradient && stops.Count == 1)
        {
            var baseColor = stops[0].Color;
            var tinted = ApplyTint(baseColor, (float)gradient.ColorTint);
            stops.Add(new RenderHatchGradientStop(1f, tinted));
        }
        else if (stops.Count == 1)
        {
            stops.Add(new RenderHatchGradientStop(1f, stops[0].Color));
        }

        var angle = (float)gradient.Angle;
        var shift = (float)gradient.Shift;
        return new RenderHatchGradient(RenderHatchGradientType.Linear, angle, shift, stops);
    }

    private static RenderColor ApplyTint(RenderColor color, float tint)
    {
        if (tint <= 0f)
        {
            return color;
        }

        var t = Math.Clamp(tint, 0f, 1f);
        var r = (byte)Math.Clamp(color.R + (255 - color.R) * t, 0f, 255f);
        var g = (byte)Math.Clamp(color.G + (255 - color.G) * t, 0f, 255f);
        var b = (byte)Math.Clamp(color.B + (255 - color.B) * t, 0f, 255f);
        return new RenderColor(r, g, b, color.A);
    }

    private static RenderColor ToRenderColor(ACadSharp.Color color, byte alpha)
    {
        return new RenderColor(color.R, color.G, color.B, alpha);
    }

    private static void AddBoundaryOutlines(
        RenderLayerBuilder builder,
        IReadOnlyList<IReadOnlyList<Vector2>> loops,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin)
    {
        foreach (var loop in loops)
        {
            if (loop.Count < 2)
            {
                continue;
            }

            var points = new List<Vector2>(loop);
            if (IsClosed(points))
            {
                points.RemoveAt(points.Count - 1);
            }

            if (points.Count >= 2)
            {
                builder.Add(new RenderPolyline(points, isClosed: true, color, thickness, lineCap, lineJoin));
            }
        }
    }

    private static bool IsClosed(IReadOnlyList<Vector2> points)
    {
        if (points.Count < 2)
        {
            return false;
        }

        var first = points[0];
        var last = points[^1];
        var delta = first - last;
        return delta.LengthSquared() <= Epsilon * Epsilon;
    }

    private static void EnsureClosed(List<Vector2> points)
    {
        if (points.Count == 0)
        {
            return;
        }

        var first = points[0];
        var last = points[^1];
        var delta = first - last;
        if (delta.LengthSquared() > Epsilon * Epsilon)
        {
            points.Add(first);
        }
    }
}
