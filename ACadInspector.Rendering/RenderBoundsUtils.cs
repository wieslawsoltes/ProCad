using System;
using System.Numerics;

namespace ACadInspector.Rendering;

internal static class RenderBoundsUtils
{
    private const float TwoPi = MathF.PI * 2f;

    public static RenderBounds InflateForStroke(RenderBounds bounds, float thickness)
    {
        if (bounds.IsEmpty || thickness <= 0f || float.IsNaN(thickness) || float.IsInfinity(thickness))
        {
            return bounds;
        }

        return bounds.Inflate(thickness * 0.5f);
    }

    public static RenderBounds ComputeArcBounds(Vector2 center, float radius, float startAngle, float endAngle)
    {
        if (radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius))
        {
            return RenderBounds.Empty.Expand(center);
        }

        if (float.IsNaN(startAngle) || float.IsInfinity(startAngle) ||
            float.IsNaN(endAngle) || float.IsInfinity(endAngle))
        {
            var min = center - new Vector2(radius, radius);
            var max = center + new Vector2(radius, radius);
            return new RenderBounds(min, max);
        }

        var start = NormalizeAngle(startAngle);
        var end = NormalizeAngle(endAngle);
        if (end < start)
        {
            end += TwoPi;
        }

        var sweep = end - start;
        if (sweep >= TwoPi - 0.0001f)
        {
            var min = center - new Vector2(radius, radius);
            var max = center + new Vector2(radius, radius);
            return new RenderBounds(min, max);
        }

        var bounds = RenderBounds.Empty;
        AddAnglePoint(ref bounds, center, radius, start);
        AddAnglePoint(ref bounds, center, radius, end);

        for (var i = 0; i < 4; i++)
        {
            var axisAngle = i * (MathF.PI * 0.5f);
            var candidate = axisAngle;
            if (candidate < start)
            {
                candidate += TwoPi;
            }

            if (candidate <= end + 0.0001f)
            {
                AddAnglePoint(ref bounds, center, radius, axisAngle);
            }
        }

        return bounds;
    }

    private static void AddAnglePoint(ref RenderBounds bounds, Vector2 center, float radius, float angle)
    {
        var point = new Vector2(
            center.X + MathF.Cos(angle) * radius,
            center.Y + MathF.Sin(angle) * radius);
        bounds = bounds.Expand(point);
    }

    private static float NormalizeAngle(float angle)
    {
        if (float.IsNaN(angle) || float.IsInfinity(angle))
        {
            return 0f;
        }

        angle %= TwoPi;
        if (angle < 0f)
        {
            angle += TwoPi;
        }

        return angle;
    }
}
