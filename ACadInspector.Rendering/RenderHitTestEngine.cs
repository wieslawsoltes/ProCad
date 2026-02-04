using System;
using System.Collections.Generic;
using System.Numerics;

namespace ACadInspector.Rendering;

/// <summary>
/// Performs broad-phase and narrow-phase hit testing against render primitives.
/// </summary>
public sealed class RenderHitTestEngine
{
    private const float TwoPi = MathF.PI * 2f;
    private readonly List<RenderSpatialHit> _candidates = new();

    /// <summary>
    /// Performs a point hit test with the provided tolerance.
    /// </summary>
    public void HitTestPoint(
        RenderScene scene,
        Vector2 point,
        float tolerance,
        List<RenderHitTestResult> results,
        RenderHitTestOptions? options = null)
    {
        HitTestPoint(scene, spatialIndex: null, point, tolerance, results, options);
    }

    public void HitTestPoint(
        RenderScene scene,
        RenderSpatialIndex? spatialIndex,
        Vector2 point,
        float tolerance,
        List<RenderHitTestResult> results,
        RenderHitTestOptions? options = null)
    {
        if (scene is null || results is null)
        {
            return;
        }

        results.Clear();
        _candidates.Clear();

        var resolvedOptions = options ?? RenderHitTestOptions.Default;
        var index = spatialIndex ?? scene.SpatialIndex ?? RenderSpatialIndex.Build(scene.Layers);
        index.QueryPoint(point, tolerance, _candidates, resolvedOptions.IncludeHiddenLayers);

        var maxResults = resolvedOptions.MaxResults;
        for (var i = 0; i < _candidates.Count; i++)
        {
            var candidate = _candidates[i];
            if (candidate.Primitive is RenderClipGroup clipGroup)
            {
                if (!TryHitClipGroupDetailed(clipGroup, point, tolerance, out var distance, out var inner))
                {
                    continue;
                }

                var target = inner ?? candidate.Primitive;
                var metadata = ResolveMetadata(scene, target);
                results.Add(new RenderHitTestResult(
                    candidate.Layer,
                    target,
                    distance,
                    target.Bounds,
                    metadata.OwnerEntity,
                    metadata.SourceEntity));
            }
            else
            {
                if (!TryHitTestPrimitive(candidate.Primitive, point, tolerance, out var distance))
                {
                    continue;
                }

                var metadata = ResolveMetadata(scene, candidate.Primitive);
                results.Add(new RenderHitTestResult(
                    candidate.Layer,
                    candidate.Primitive,
                    distance,
                    candidate.Bounds,
                    metadata.OwnerEntity,
                    metadata.SourceEntity));
            }
            if (maxResults > 0 && results.Count >= maxResults)
            {
                break;
            }
        }

        if (resolvedOptions.SortByDistance && results.Count > 1)
        {
            results.Sort(static (left, right) => left.Distance.CompareTo(right.Distance));
        }
    }

    /// <summary>
    /// Performs a bounds query that returns intersecting primitives.
    /// </summary>
    public void HitTestBounds(
        RenderScene scene,
        RenderBounds bounds,
        List<RenderHitTestResult> results,
        RenderHitTestOptions? options = null)
    {
        HitTestBounds(scene, spatialIndex: null, bounds, results, options);
    }

    public void HitTestBounds(
        RenderScene scene,
        RenderSpatialIndex? spatialIndex,
        RenderBounds bounds,
        List<RenderHitTestResult> results,
        RenderHitTestOptions? options = null)
    {
        if (scene is null || results is null)
        {
            return;
        }

        results.Clear();
        _candidates.Clear();

        var resolvedOptions = options ?? RenderHitTestOptions.Default;
        var index = spatialIndex ?? scene.SpatialIndex ?? RenderSpatialIndex.Build(scene.Layers);
        index.Query(bounds, _candidates, resolvedOptions.IncludeHiddenLayers);

        var maxResults = resolvedOptions.MaxResults;
        for (var i = 0; i < _candidates.Count; i++)
        {
            var candidate = _candidates[i];
            if (!candidate.Bounds.Intersects(bounds))
            {
                continue;
            }

            var metadata = ResolveMetadata(scene, candidate.Primitive);
            results.Add(new RenderHitTestResult(
                candidate.Layer,
                candidate.Primitive,
                distance: 0f,
                candidate.Bounds,
                metadata.OwnerEntity,
                metadata.SourceEntity));
            if (maxResults > 0 && results.Count >= maxResults)
            {
                break;
            }
        }
    }

    private static RenderPrimitiveMetadata ResolveMetadata(RenderScene scene, IRenderPrimitive primitive)
    {
        if (scene.PrimitiveMetadata.TryGetValue(primitive, out var metadata))
        {
            return metadata;
        }

        return default;
    }

    private static bool TryHitTestPrimitive(IRenderPrimitive primitive, Vector2 point, float tolerance, out float distance)
    {
        switch (primitive)
        {
            case RenderLine line:
                return TryHitLine(line, point, tolerance, out distance);
            case RenderPolyline polyline:
                return TryHitPolyline(polyline, point, tolerance, out distance);
            case RenderCircle circle:
                return TryHitCircle(circle, point, tolerance, out distance);
            case RenderArc arc:
                return TryHitArc(arc, point, tolerance, out distance);
            case RenderPoint renderPoint:
                return TryHitPoint(renderPoint, point, tolerance, out distance);
            case RenderFill fill:
                return TryHitFill(fill, point, out distance);
            case RenderTriangle triangle:
                return TryHitTriangle(triangle, point, out distance);
            case RenderHatchFill hatchFill:
                return TryHitHatchFill(hatchFill, point, out distance);
            case RenderHatchPattern hatchPattern:
                return TryHitHatchPattern(hatchPattern, point, tolerance, out distance);
            case RenderImage image:
                return TryHitImage(image, point, out distance);
            case RenderClipGroup clipGroup:
                return TryHitClipGroup(clipGroup, point, tolerance, out distance);
            case RenderText text:
                return TryHitText(text, point, tolerance, out distance);
            default:
                distance = primitive.Bounds.Contains(point) ? 0f : float.PositiveInfinity;
                return distance == 0f;
        }
    }

    private static bool TryHitLine(RenderLine line, Vector2 point, float tolerance, out float distance)
    {
        var allowance = tolerance + line.Thickness * 0.5f;
        var allowanceSq = allowance * allowance;
        var distSq = DistancePointToSegmentSquared(point, line.Start, line.End);
        if (distSq > allowanceSq)
        {
            distance = float.PositiveInfinity;
            return false;
        }

        distance = MathF.Sqrt(distSq);
        return true;
    }

    private static bool TryHitPolyline(RenderPolyline polyline, Vector2 point, float tolerance, out float distance)
    {
        var points = polyline.Points;
        if (points is null || points.Count == 0)
        {
            distance = float.PositiveInfinity;
            return false;
        }

        if (points.Count == 1)
        {
            return TryHitPoint(points[0], point, tolerance + polyline.Thickness * 0.5f, out distance);
        }

        var allowance = tolerance + polyline.Thickness * 0.5f;
        var allowanceSq = allowance * allowance;
        var minSq = float.PositiveInfinity;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var distSq = DistancePointToSegmentSquared(point, points[i], points[i + 1]);
            if (distSq < minSq)
            {
                minSq = distSq;
            }
        }

        if (polyline.IsClosed && points.Count > 2)
        {
            var distSq = DistancePointToSegmentSquared(point, points[^1], points[0]);
            if (distSq < minSq)
            {
                minSq = distSq;
            }
        }

        if (minSq > allowanceSq)
        {
            distance = float.PositiveInfinity;
            return false;
        }

        distance = MathF.Sqrt(minSq);
        return true;
    }

    private static bool TryHitCircle(RenderCircle circle, Vector2 point, float tolerance, out float distance)
    {
        var vector = point - circle.Center;
        var len = vector.Length();
        var radial = MathF.Abs(len - circle.Radius);
        var allowance = tolerance + circle.Thickness * 0.5f;
        if (radial > allowance)
        {
            distance = float.PositiveInfinity;
            return false;
        }

        distance = radial;
        return true;
    }

    private static bool TryHitArc(RenderArc arc, Vector2 point, float tolerance, out float distance)
    {
        var vector = point - arc.Center;
        var len = vector.Length();
        var radial = MathF.Abs(len - arc.Radius);
        var allowance = tolerance + arc.Thickness * 0.5f;
        if (radial > allowance)
        {
            distance = float.PositiveInfinity;
            return false;
        }

        var angle = MathF.Atan2(vector.Y, vector.X);
        if (!IsAngleWithinSweep(angle, arc.StartAngle, arc.EndAngle))
        {
            distance = float.PositiveInfinity;
            return false;
        }

        distance = radial;
        return true;
    }

    private static bool TryHitPoint(RenderPoint renderPoint, Vector2 point, float tolerance, out float distance)
    {
        var radius = renderPoint.DisplaySize > 0 ? (float)renderPoint.DisplaySize * 0.5f : tolerance;
        radius += renderPoint.Thickness * 0.5f;
        return TryHitPoint(renderPoint.Point, point, radius, out distance);
    }

    private static bool TryHitPoint(Vector2 target, Vector2 point, float radius, out float distance)
    {
        var delta = point - target;
        var distSq = delta.LengthSquared();
        var radiusSq = radius * radius;
        if (distSq > radiusSq)
        {
            distance = float.PositiveInfinity;
            return false;
        }

        distance = MathF.Sqrt(distSq);
        return true;
    }

    private static bool TryHitFill(RenderFill fill, Vector2 point, out float distance)
    {
        if (fill.Points is null || fill.Points.Count < 3)
        {
            distance = float.PositiveInfinity;
            return false;
        }

        var inside = RenderLoopUtils.IsPointInLoops(point, new[] { fill.Points }, RenderLoopFillMode.EvenOdd);
        distance = inside ? 0f : float.PositiveInfinity;
        return inside;
    }

    private static bool TryHitTriangle(RenderTriangle triangle, Vector2 point, out float distance)
    {
        var inside = PointInTriangle(point, triangle.A, triangle.B, triangle.C);
        distance = inside ? 0f : float.PositiveInfinity;
        return inside;
    }

    private static bool TryHitHatchFill(RenderHatchFill fill, Vector2 point, out float distance)
    {
        var inside = RenderLoopUtils.IsPointInLoops(point, fill.Loops, fill.FillMode);
        distance = inside ? 0f : float.PositiveInfinity;
        return inside;
    }

    private static bool TryHitHatchPattern(RenderHatchPattern pattern, Vector2 point, float tolerance, out float distance)
    {
        var allowance = tolerance + pattern.Thickness * 0.5f;
        var allowanceSq = allowance * allowance;
        var minSq = float.PositiveInfinity;
        var segments = pattern.Segments;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var distSq = DistancePointToSegmentSquared(point, segment.Start, segment.End);
            if (distSq < minSq)
            {
                minSq = distSq;
            }
        }

        if (minSq > allowanceSq)
        {
            distance = float.PositiveInfinity;
            return false;
        }

        distance = MathF.Sqrt(minSq);
        return true;
    }

    private static bool TryHitImage(RenderImage image, Vector2 point, out float distance)
    {
        var corners = GetImageCorners(image);
        var inside = RenderLoopUtils.IsPointInLoops(point, new[] { corners }, RenderLoopFillMode.EvenOdd);
        distance = inside ? 0f : float.PositiveInfinity;
        return inside;
    }

    private static bool TryHitClipGroup(RenderClipGroup clipGroup, Vector2 point, float tolerance, out float distance)
    {
        return TryHitClipGroupDetailed(clipGroup, point, tolerance, out distance, out _);
    }

    private static bool TryHitClipGroupDetailed(
        RenderClipGroup clipGroup,
        Vector2 point,
        float tolerance,
        out float distance,
        out IRenderPrimitive? hitPrimitive)
    {
        hitPrimitive = null;
        if (!RenderLoopUtils.IsPointInLoops(point, clipGroup.Loops, clipGroup.FillMode))
        {
            distance = float.PositiveInfinity;
            return false;
        }

        distance = float.PositiveInfinity;
        var primitives = clipGroup.Primitives;
        for (var i = 0; i < primitives.Count; i++)
        {
            if (TryHitTestPrimitive(primitives[i], point, tolerance, out var candidateDistance))
            {
                if (candidateDistance < distance)
                {
                    distance = candidateDistance;
                    hitPrimitive = primitives[i];
                }
            }
        }

        return distance < float.PositiveInfinity;
    }

    private static bool TryHitText(RenderText text, Vector2 point, float tolerance, out float distance)
    {
        var quad = RenderTextUtils.BuildTextQuad(
            text.Anchor,
            text.Offset,
            text.LayoutWidth,
            text.LayoutHeight,
            text.WidthFactor,
            text.Rotation,
            text.ObliqueAngle,
            text.MirrorX,
            text.MirrorY);
        if (RenderLoopUtils.IsPointInLoops(point, new[] { quad }, RenderLoopFillMode.EvenOdd))
        {
            distance = 0f;
            return true;
        }

        if (tolerance <= 0f)
        {
            distance = float.PositiveInfinity;
            return false;
        }

        var minSq = float.PositiveInfinity;
        for (var i = 0; i < quad.Count; i++)
        {
            var a = quad[i];
            var b = quad[(i + 1) % quad.Count];
            var distSq = DistancePointToSegmentSquared(point, a, b);
            if (distSq < minSq)
            {
                minSq = distSq;
            }
        }

        if (minSq > tolerance * tolerance)
        {
            distance = float.PositiveInfinity;
            return false;
        }

        distance = MathF.Sqrt(minSq);
        return true;
    }

    private static Vector2[] GetImageCorners(RenderImage image)
    {
        var origin = image.Origin;
        var u = image.UVector * image.Size.X;
        var v = image.VVector * image.Size.Y;
        return new[]
        {
            origin,
            origin + u,
            origin + u + v,
            origin + v
        };
    }

    private static float DistancePointToSegmentSquared(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSq = Vector2.Dot(ab, ab);
        if (lengthSq <= 0f)
        {
            return Vector2.DistanceSquared(point, a);
        }

        var t = Vector2.Dot(point - a, ab) / lengthSq;
        if (t <= 0f)
        {
            return Vector2.DistanceSquared(point, a);
        }

        if (t >= 1f)
        {
            return Vector2.DistanceSquared(point, b);
        }

        var projection = a + ab * t;
        return Vector2.DistanceSquared(point, projection);
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var v0 = c - a;
        var v1 = b - a;
        var v2 = p - a;

        var dot00 = Vector2.Dot(v0, v0);
        var dot01 = Vector2.Dot(v0, v1);
        var dot02 = Vector2.Dot(v0, v2);
        var dot11 = Vector2.Dot(v1, v1);
        var dot12 = Vector2.Dot(v1, v2);

        var denom = dot00 * dot11 - dot01 * dot01;
        if (MathF.Abs(denom) <= float.Epsilon)
        {
            return false;
        }

        var invDenom = 1f / denom;
        var u = (dot11 * dot02 - dot01 * dot12) * invDenom;
        var v = (dot00 * dot12 - dot01 * dot02) * invDenom;
        return (u >= 0f) && (v >= 0f) && (u + v <= 1f);
    }

    private static bool IsAngleWithinSweep(float angle, float startAngle, float endAngle)
    {
        var start = NormalizeAngle(startAngle);
        var end = NormalizeAngle(endAngle);
        var test = NormalizeAngle(angle);

        if (end < start)
        {
            end += TwoPi;
        }

        if (test < start)
        {
            test += TwoPi;
        }

        return test >= start && test <= end;
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= TwoPi;
        if (angle < 0f)
        {
            angle += TwoPi;
        }

        return angle;
    }
}

/// <summary>
/// Represents a render hit test result.
/// </summary>
public readonly struct RenderHitTestResult
{
    /// <summary>
    /// Gets the layer that owns the primitive.
    /// </summary>
    public RenderLayer Layer { get; }

    /// <summary>
    /// Gets the primitive that was hit.
    /// </summary>
    public IRenderPrimitive Primitive { get; }

    /// <summary>
    /// Gets the hit distance in world units.
    /// </summary>
    public float Distance { get; }

    /// <summary>
    /// Gets the bounds of the primitive.
    /// </summary>
    public RenderBounds Bounds { get; }
    /// <summary>
    /// Gets the logical owner entity, such as an INSERT for block contents.
    /// </summary>
    public ACadSharp.Entities.Entity? OwnerEntity { get; }
    /// <summary>
    /// Gets the source entity that produced the primitive.
    /// </summary>
    public ACadSharp.Entities.Entity? SourceEntity { get; }

    public RenderHitTestResult(
        RenderLayer layer,
        IRenderPrimitive primitive,
        float distance,
        RenderBounds bounds,
        ACadSharp.Entities.Entity? ownerEntity,
        ACadSharp.Entities.Entity? sourceEntity)
    {
        Layer = layer;
        Primitive = primitive;
        Distance = distance;
        Bounds = bounds;
        OwnerEntity = ownerEntity;
        SourceEntity = sourceEntity;
    }
}

/// <summary>
/// Configures hit testing behavior.
/// </summary>
public readonly struct RenderHitTestOptions
{
    /// <summary>
    /// Gets the default hit test options.
    /// </summary>
    public static readonly RenderHitTestOptions Default = new(includeHiddenLayers: false, maxResults: 0, sortByDistance: true);

    /// <summary>
    /// Gets a value indicating whether hidden layers are included.
    /// </summary>
    public bool IncludeHiddenLayers { get; }

    /// <summary>
    /// Gets the maximum number of results to return. A value of 0 means no limit.
    /// </summary>
    public int MaxResults { get; }

    /// <summary>
    /// Gets a value indicating whether results are sorted by distance.
    /// </summary>
    public bool SortByDistance { get; }

    public RenderHitTestOptions(bool includeHiddenLayers, int maxResults, bool sortByDistance)
    {
        IncludeHiddenLayers = includeHiddenLayers;
        MaxResults = maxResults;
        SortByDistance = sortByDistance;
    }
}
