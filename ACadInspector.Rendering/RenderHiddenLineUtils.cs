using System;
using System.Collections.Generic;
using System.Numerics;

namespace ACadInspector.Rendering;

internal readonly struct RenderLineSegment
{
    public Vector2 Start { get; }
    public Vector2 End { get; }

    public RenderLineSegment(Vector2 start, Vector2 end)
    {
        Start = start;
        End = end;
    }
}

internal static class RenderHiddenLineUtils
{
    public const float DefaultDepthEpsilon = 1e-4f;

    public static bool TryBuildDepthBuffer(
        RenderScene scene,
        Matrix3x2 worldToScreen,
        RenderDepthBuffer depthBuffer,
        int width,
        int height)
    {
        if (scene is null)
        {
            return false;
        }

        var primitives = new List<IRenderPrimitive>();
        foreach (var layer in scene.Layers)
        {
            if (!layer.IsVisible)
            {
                continue;
            }

            CollectPrimitives(layer.Primitives, primitives, includeClipGroups: false);
        }

        return TryBuildDepthBuffer(primitives, worldToScreen, depthBuffer, width, height);
    }

    public static bool TryBuildDepthBuffer(
        IReadOnlyList<IRenderPrimitive> primitives,
        Matrix3x2 worldToScreen,
        RenderDepthBuffer depthBuffer,
        int width,
        int height,
        IReadOnlyList<IReadOnlyList<Vector2>>? clipLoops = null,
        bool includeClipGroups = false)
    {
        if (primitives is null || width <= 0 || height <= 0)
        {
            return false;
        }

        depthBuffer.EnsureSize(width, height);
        depthBuffer.Clear();

        IReadOnlyList<IReadOnlyList<Vector2>>? screenLoops = null;
        ClipBounds? clipBounds = null;
        if (clipLoops is not null && clipLoops.Count > 0)
        {
            screenLoops = TransformLoops(clipLoops, worldToScreen);
            if (screenLoops.Count > 0)
            {
                clipBounds = ClipBounds.FromLoops(screenLoops);
            }
        }

        var hasTriangles = false;
        for (var i = 0; i < primitives.Count; i++)
        {
            RasterizePrimitive(
                primitives[i],
                worldToScreen,
                depthBuffer,
                screenLoops,
                clipBounds,
                includeClipGroups,
                ref hasTriangles);
        }

        return hasTriangles && depthBuffer.HasDepth;
    }

    public static void AppendVisibleSegments(
        RenderDepthBuffer depthBuffer,
        Matrix3x2 worldToScreen,
        Vector2 worldStart,
        Vector2 worldEnd,
        float depthStart,
        float depthEnd,
        List<RenderLineSegment> segments,
        float depthEpsilon = DefaultDepthEpsilon)
    {
        if (segments is null)
        {
            throw new ArgumentNullException(nameof(segments));
        }

        if (depthBuffer.Width <= 0 || depthBuffer.Height <= 0 || !depthBuffer.HasDepth)
        {
            segments.Add(new RenderLineSegment(worldStart, worldEnd));
            return;
        }

        var screenStart = Vector2.Transform(worldStart, worldToScreen);
        var screenEnd = Vector2.Transform(worldEnd, worldToScreen);
        var delta = screenEnd - screenStart;
        var length = delta.Length();
        if (length <= 0.001f)
        {
            if (IsVisibleSample(depthBuffer, screenStart, depthStart, depthEpsilon))
            {
                segments.Add(new RenderLineSegment(worldStart, worldEnd));
            }
            return;
        }

        var steps = Math.Max(1, (int)MathF.Ceiling(length));
        var invSteps = 1f / steps;

        var runStart = -1;
        for (var i = 0; i <= steps; i++)
        {
            var t = i * invSteps;
            var screen = screenStart + delta * t;
            var depth = Lerp(depthStart, depthEnd, t);
            var visible = IsVisibleSample(depthBuffer, screen, depth, depthEpsilon);

            if (visible)
            {
                if (runStart < 0)
                {
                    runStart = i;
                }
            }
            else if (runStart >= 0)
            {
                AppendSegment(worldStart, worldEnd, runStart, i - 1, invSteps, segments);
                runStart = -1;
            }
        }

        if (runStart >= 0)
        {
            AppendSegment(worldStart, worldEnd, runStart, steps, invSteps, segments);
        }
    }

    private static void AppendSegment(
        Vector2 worldStart,
        Vector2 worldEnd,
        int startIndex,
        int endIndex,
        float invSteps,
        List<RenderLineSegment> segments)
    {
        if (endIndex < startIndex)
        {
            return;
        }

        var startT = startIndex * invSteps;
        var endT = endIndex * invSteps;
        if (endT - startT <= 0.0001f)
        {
            return;
        }

        var start = Vector2.Lerp(worldStart, worldEnd, startT);
        var end = Vector2.Lerp(worldStart, worldEnd, endT);
        segments.Add(new RenderLineSegment(start, end));
    }

    private static bool IsVisibleSample(
        RenderDepthBuffer depthBuffer,
        Vector2 screen,
        float depth,
        float depthEpsilon)
    {
        if (float.IsNaN(depth) || float.IsInfinity(depth))
        {
            return true;
        }

        var x = (int)MathF.Round(screen.X);
        var y = (int)MathF.Round(screen.Y);
        if (x < 0 || y < 0 || x >= depthBuffer.Width || y >= depthBuffer.Height)
        {
            return true;
        }

        var occluderDepth = depthBuffer.GetDepth(x, y);
        if (occluderDepth <= RenderDepthBuffer.EmptyDepth)
        {
            return true;
        }

        return depth >= occluderDepth - depthEpsilon;
    }

    private static void RasterizeTriangle(
        RenderDepthBuffer depthBuffer,
        Matrix3x2 worldToScreen,
        RenderTriangle triangle,
        IReadOnlyList<IReadOnlyList<Vector2>>? clipLoops,
        ClipBounds? clipBounds)
    {
        var a = Vector2.Transform(triangle.A, worldToScreen);
        var b = Vector2.Transform(triangle.B, worldToScreen);
        var c = Vector2.Transform(triangle.C, worldToScreen);
        RasterizeTriangle(depthBuffer, a, triangle.DepthA, b, triangle.DepthB, c, triangle.DepthC, clipLoops, clipBounds);
    }

    private static void RasterizeTriangle(
        RenderDepthBuffer depthBuffer,
        Vector2 a,
        float depthA,
        Vector2 b,
        float depthB,
        Vector2 c,
        float depthC,
        IReadOnlyList<IReadOnlyList<Vector2>>? clipLoops,
        ClipBounds? clipBounds)
    {
        var area = Edge(a, b, c);
        if (MathF.Abs(area) <= 0.000001f)
        {
            return;
        }

        var minX = MathF.Min(a.X, MathF.Min(b.X, c.X));
        var minY = MathF.Min(a.Y, MathF.Min(b.Y, c.Y));
        var maxX = MathF.Max(a.X, MathF.Max(b.X, c.X));
        var maxY = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));

        if (clipBounds.HasValue)
        {
            var bounds = clipBounds.Value;
            if (!bounds.Intersects(minX, minY, maxX, maxY))
            {
                return;
            }

            minX = MathF.Max(minX, bounds.MinX);
            minY = MathF.Max(minY, bounds.MinY);
            maxX = MathF.Min(maxX, bounds.MaxX);
            maxY = MathF.Min(maxY, bounds.MaxY);
        }

        var startX = Math.Clamp((int)MathF.Floor(minX), 0, depthBuffer.Width - 1);
        var startY = Math.Clamp((int)MathF.Floor(minY), 0, depthBuffer.Height - 1);
        var endX = Math.Clamp((int)MathF.Ceiling(maxX), 0, depthBuffer.Width - 1);
        var endY = Math.Clamp((int)MathF.Ceiling(maxY), 0, depthBuffer.Height - 1);

        var isAreaPositive = area > 0f;
        for (var y = startY; y <= endY; y++)
        {
            var py = y + 0.5f;
            for (var x = startX; x <= endX; x++)
            {
                var p = new Vector2(x + 0.5f, py);
                if (clipLoops is not null && !IsInsideClip(p, clipLoops))
                {
                    continue;
                }

                var w0 = Edge(b, c, p);
                var w1 = Edge(c, a, p);
                var w2 = Edge(a, b, p);

                if (!IsInside(w0, w1, w2, isAreaPositive))
                {
                    continue;
                }

                var invArea = 1f / area;
                var alpha = w0 * invArea;
                var beta = w1 * invArea;
                var gamma = w2 * invArea;
                var depth = alpha * depthA + beta * depthB + gamma * depthC;
                if (float.IsNaN(depth) || float.IsInfinity(depth))
                {
                    continue;
                }

                depthBuffer.SetDepth(x, y, depth);
            }
        }
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 c)
    {
        return (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);
    }

    private static bool IsInside(float w0, float w1, float w2, bool areaPositive)
    {
        if (areaPositive)
        {
            return w0 >= 0f && w1 >= 0f && w2 >= 0f;
        }

        return w0 <= 0f && w1 <= 0f && w2 <= 0f;
    }

    private static void CollectPrimitives(
        IReadOnlyList<IRenderPrimitive> primitives,
        List<IRenderPrimitive> target,
        bool includeClipGroups)
    {
        for (var i = 0; i < primitives.Count; i++)
        {
            var primitive = primitives[i];
            if (primitive is RenderClipGroup clipGroup)
            {
                if (includeClipGroups)
                {
                    CollectPrimitives(clipGroup.Primitives, target, includeClipGroups: true);
                }
                continue;
            }

            target.Add(primitive);
        }
    }

    private static void RasterizePrimitive(
        IRenderPrimitive primitive,
        Matrix3x2 worldToScreen,
        RenderDepthBuffer depthBuffer,
        IReadOnlyList<IReadOnlyList<Vector2>>? clipLoops,
        ClipBounds? clipBounds,
        bool includeClipGroups,
        ref bool hasTriangles)
    {
        if (primitive is RenderTriangle triangle)
        {
            hasTriangles = true;
            RasterizeTriangle(depthBuffer, worldToScreen, triangle, clipLoops, clipBounds);
            return;
        }

        if (includeClipGroups && primitive is RenderClipGroup clipGroup)
        {
            foreach (var child in clipGroup.Primitives)
            {
                RasterizePrimitive(child, worldToScreen, depthBuffer, clipLoops, clipBounds, includeClipGroups, ref hasTriangles);
            }
        }
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> TransformLoops(
        IReadOnlyList<IReadOnlyList<Vector2>> loops,
        Matrix3x2 worldToScreen)
    {
        var transformed = new List<IReadOnlyList<Vector2>>(loops.Count);
        foreach (var loop in loops)
        {
            if (loop.Count == 0)
            {
                continue;
            }

            var points = new List<Vector2>(loop.Count);
            for (var i = 0; i < loop.Count; i++)
            {
                points.Add(Vector2.Transform(loop[i], worldToScreen));
            }

            transformed.Add(points);
        }

        return transformed;
    }

    private static bool IsInsideClip(Vector2 point, IReadOnlyList<IReadOnlyList<Vector2>> loops)
    {
        var inside = false;
        for (var i = 0; i < loops.Count; i++)
        {
            if (PointInPolygon(point, loops[i]))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static bool PointInPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];
            var intersect = ((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                            (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y + float.Epsilon) + pi.X);
            if (intersect)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    private readonly struct ClipBounds
    {
        public float MinX { get; }
        public float MinY { get; }
        public float MaxX { get; }
        public float MaxY { get; }

        private ClipBounds(float minX, float minY, float maxX, float maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public static ClipBounds FromLoops(IReadOnlyList<IReadOnlyList<Vector2>> loops)
        {
            var minX = float.PositiveInfinity;
            var minY = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            var maxY = float.NegativeInfinity;

            foreach (var loop in loops)
            {
                foreach (var point in loop)
                {
                    if (point.X < minX)
                    {
                        minX = point.X;
                    }

                    if (point.Y < minY)
                    {
                        minY = point.Y;
                    }

                    if (point.X > maxX)
                    {
                        maxX = point.X;
                    }

                    if (point.Y > maxY)
                    {
                        maxY = point.Y;
                    }
                }
            }

            if (float.IsInfinity(minX) || float.IsInfinity(minY))
            {
                return new ClipBounds(0f, 0f, -1f, -1f);
            }

            return new ClipBounds(minX, minY, maxX, maxY);
        }

        public bool Intersects(float minX, float minY, float maxX, float maxY)
        {
            if (MaxX < minX || MinX > maxX)
            {
                return false;
            }

            if (MaxY < minY || MinY > maxY)
            {
                return false;
            }

            return true;
        }
    }
}
