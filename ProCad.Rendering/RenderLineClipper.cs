using System;
using System.Numerics;

namespace ProCad.Rendering;

internal static class RenderLineClipper
{
    private const float Epsilon = 0.000001f;

    public static bool TryClipInfiniteLine(
        Vector2 origin,
        Vector2 direction,
        RenderBounds bounds,
        bool isRay,
        out Vector2 start,
        out Vector2 end)
    {
        start = default;
        end = default;

        if (bounds.IsEmpty)
        {
            return false;
        }

        var dx = direction.X;
        var dy = direction.Y;
        if (MathF.Abs(dx) <= Epsilon && MathF.Abs(dy) <= Epsilon)
        {
            return false;
        }

        double t0 = isRay ? 0.0 : double.NegativeInfinity;
        double t1 = double.PositiveInfinity;

        if (!Clip(-dx, origin.X - bounds.Min.X, ref t0, ref t1))
        {
            return false;
        }

        if (!Clip(dx, bounds.Max.X - origin.X, ref t0, ref t1))
        {
            return false;
        }

        if (!Clip(-dy, origin.Y - bounds.Min.Y, ref t0, ref t1))
        {
            return false;
        }

        if (!Clip(dy, bounds.Max.Y - origin.Y, ref t0, ref t1))
        {
            return false;
        }

        if (double.IsInfinity(t0) || double.IsInfinity(t1))
        {
            return false;
        }

        if (t0 > t1)
        {
            return false;
        }

        start = origin + direction * (float)t0;
        end = origin + direction * (float)t1;
        return true;
    }

    private static bool Clip(double p, double q, ref double t0, ref double t1)
    {
        if (Math.Abs(p) <= Epsilon)
        {
            return q >= 0;
        }

        var r = q / p;
        if (p < 0)
        {
            if (r > t1)
            {
                return false;
            }

            if (r > t0)
            {
                t0 = r;
            }
        }
        else
        {
            if (r < t0)
            {
                return false;
            }

            if (r < t1)
            {
                t1 = r;
            }
        }

        return true;
    }
}
