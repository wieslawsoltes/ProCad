using System;
using System.Numerics;
using CSMath;

namespace ACadInspector.Rendering;

internal static class RenderTransformUtils
{
    // ACadSharp's Matrix4.GetArbitraryAxis uses (1 / 64) with integer division,
    // causing near-axis normals to generate unstable axes. Snap near-Z normals
    // to exact axis directions before building OCS transforms.
    public static XYZ NormalizeNormal(XYZ normal)
    {
        if (normal.IsZero())
        {
            return XYZ.AxisZ;
        }

        var normalized = normal.Normalize();
        const double epsilon = 1e-6;

        if (Math.Abs(normalized.X) <= epsilon && Math.Abs(normalized.Y) <= epsilon)
        {
            return normalized.Z >= 0 ? XYZ.AxisZ : new XYZ(0, 0, -1);
        }

        return normalized;
    }

    public static bool IsIdentity(Transform transform)
    {
        return transform.Matrix.Equals(Matrix4.Identity);
    }

    public static Transform Combine(Transform left, Transform right)
    {
        return new Transform(left.Matrix * right.Matrix);
    }

    public static Transform CombineWithNormal(Transform transform, XYZ normal)
    {
        var resolved = NormalizeNormal(normal);
        if (resolved.Equals(XYZ.AxisZ))
        {
            return transform;
        }

        var ocs = new Transform(Matrix4.GetArbitraryAxis(resolved));
        return Combine(transform, ocs);
    }

    public static Vector2 Apply(Transform transform, XYZ point)
    {
        return ToVector2(transform.ApplyTransform(point));
    }

    public static XYZ Apply3D(Transform transform, XYZ point)
    {
        return transform.ApplyTransform(point);
    }

    public static Vector2 ToVector2(XYZ point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }
}
