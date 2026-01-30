using System.Numerics;
using CSMath;

namespace ACadInspector.Rendering;

internal static class RenderTransformUtils
{
    public static bool IsIdentity(Transform transform)
    {
        return transform.Matrix.Equals(Matrix4.Identity);
    }

    public static Transform Combine(Transform left, Transform right)
    {
        return new Transform(left.Matrix * right.Matrix);
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
