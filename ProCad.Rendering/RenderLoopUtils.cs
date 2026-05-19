using System;
using System.Collections.Generic;
using System.Numerics;

namespace ProCad.Rendering;

internal static class RenderLoopUtils
{
    public static float ComputeSignedArea(IReadOnlyList<Vector2> loop)
    {
        if (loop is null || loop.Count < 3)
        {
            return 0f;
        }

        double area = 0;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            var pi = loop[i];
            var pj = loop[j];
            area += (pj.X * pi.Y) - (pi.X * pj.Y);
        }

        return (float)(area * 0.5);
    }

    public static bool IsPointInLoops(Vector2 point, IReadOnlyList<IReadOnlyList<Vector2>> loops, RenderLoopFillMode fillMode)
    {
        if (loops is null || loops.Count == 0)
        {
            return false;
        }

        switch (fillMode)
        {
            case RenderLoopFillMode.NonZero:
                return WindingNumber(point, loops) != 0;
            case RenderLoopFillMode.Outer:
                return IsPointInOuterLoops(point, loops);
            case RenderLoopFillMode.Ignore:
                return IsPointInAnyLoop(point, loops);
            case RenderLoopFillMode.EvenOdd:
            default:
                return EvenOdd(point, loops);
        }
    }

    public static IReadOnlyList<IReadOnlyList<Vector2>> NormalizeLoopsForFill(
        IReadOnlyList<IReadOnlyList<Vector2>> loops,
        RenderLoopFillMode fillMode)
    {
        if (loops is null || loops.Count == 0)
        {
            return Array.Empty<IReadOnlyList<Vector2>>();
        }

        switch (fillMode)
        {
            case RenderLoopFillMode.Outer:
                return FilterOuterLoops(loops);
            case RenderLoopFillMode.Ignore:
                return NormalizeLoopOrientation(loops);
            default:
                return loops;
        }
    }

    private static bool EvenOdd(Vector2 point, IReadOnlyList<IReadOnlyList<Vector2>> loops)
    {
        var inside = false;
        for (var i = 0; i < loops.Count; i++)
        {
            if (PointInPolygonEvenOdd(point, loops[i]))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static int WindingNumber(Vector2 point, IReadOnlyList<IReadOnlyList<Vector2>> loops)
    {
        var winding = 0;
        for (var i = 0; i < loops.Count; i++)
        {
            winding += WindingNumber(point, loops[i]);
        }

        return winding;
    }

    private static int WindingNumber(Vector2 point, IReadOnlyList<Vector2> loop)
    {
        if (loop is null || loop.Count < 3)
        {
            return 0;
        }

        var winding = 0;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            var pi = loop[i];
            var pj = loop[j];

            if (pj.Y <= point.Y)
            {
                if (pi.Y > point.Y && IsLeft(pj, pi, point) > 0)
                {
                    winding++;
                }
            }
            else
            {
                if (pi.Y <= point.Y && IsLeft(pj, pi, point) < 0)
                {
                    winding--;
                }
            }
        }

        return winding;
    }

    private static float IsLeft(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
    }

    private static bool PointInPolygonEvenOdd(Vector2 point, IReadOnlyList<Vector2> polygon)
    {
        if (polygon is null || polygon.Count < 3)
        {
            return false;
        }

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

    private static bool IsPointInAnyLoop(Vector2 point, IReadOnlyList<IReadOnlyList<Vector2>> loops)
    {
        for (var i = 0; i < loops.Count; i++)
        {
            if (PointInPolygonEvenOdd(point, loops[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointInOuterLoops(Vector2 point, IReadOnlyList<IReadOnlyList<Vector2>> loops)
    {
        var outerLoops = FilterOuterLoops(loops);
        for (var i = 0; i < outerLoops.Count; i++)
        {
            if (PointInPolygonEvenOdd(point, outerLoops[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> FilterOuterLoops(IReadOnlyList<IReadOnlyList<Vector2>> loops)
    {
        var result = new List<IReadOnlyList<Vector2>>();
        for (var i = 0; i < loops.Count; i++)
        {
            var loop = loops[i];
            if (loop is null || loop.Count < 3)
            {
                continue;
            }

            var sample = loop[0];
            var isInside = false;
            for (var j = 0; j < loops.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                var other = loops[j];
                if (other is null || other.Count < 3)
                {
                    continue;
                }

                if (PointInPolygonEvenOdd(sample, other))
                {
                    isInside = true;
                    break;
                }
            }

            if (!isInside)
            {
                result.Add(loop);
            }
        }

        return result;
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> NormalizeLoopOrientation(IReadOnlyList<IReadOnlyList<Vector2>> loops)
    {
        var result = new List<IReadOnlyList<Vector2>>(loops.Count);
        if (loops.Count == 0)
        {
            return result;
        }

        var targetSign = 0f;
        for (var i = 0; i < loops.Count; i++)
        {
            var area = ComputeSignedArea(loops[i]);
            if (MathF.Abs(area) > 0.0001f)
            {
                targetSign = MathF.Sign(area);
                break;
            }
        }

        if (targetSign == 0f)
        {
            return loops;
        }

        for (var i = 0; i < loops.Count; i++)
        {
            var loop = loops[i];
            if (loop is null || loop.Count < 3)
            {
                continue;
            }

            var area = ComputeSignedArea(loop);
            if (MathF.Sign(area) != targetSign)
            {
                var reversed = new List<Vector2>(loop.Count);
                for (var j = loop.Count - 1; j >= 0; j--)
                {
                    reversed.Add(loop[j]);
                }

                result.Add(reversed);
            }
            else
            {
                result.Add(loop);
            }
        }

        return result;
    }
}
