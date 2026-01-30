using System.Numerics;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public static class RenderTextUtils
{
    public static float ResolveAnnotationScale(bool isAnnotative, CadRenderSceneSettings settings)
    {
        if (!isAnnotative)
        {
            return 1f;
        }

        var scale = settings.AnnotationScaleFactor;
        if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
        {
            return 1f;
        }

        return scale;
    }

    public static float ResolveTextHeight(TextEntity text)
    {
        var height = text.Height;
        if (height <= MathHelper.Epsilon)
        {
            var styleHeight = text.Style?.Height ?? 0.0;
            if (styleHeight > MathHelper.Epsilon)
            {
                height = styleHeight;
            }
        }

        return (float)height;
    }

    public static IReadOnlyList<Vector2> BuildTextQuad(
        Vector2 anchor,
        Vector2 offset,
        float width,
        float height,
        float widthFactor,
        float rotation,
        float obliqueAngle,
        bool mirrorX,
        bool mirrorY)
    {
        var scaleX = widthFactor * (mirrorX ? -1f : 1f);
        var scaleY = mirrorY ? 1f : -1f;
        var corners = new[]
        {
            offset,
            offset + new Vector2(width, 0f),
            offset + new Vector2(width, height),
            offset + new Vector2(0f, height)
        };

        var sin = MathF.Sin(rotation);
        var cos = MathF.Cos(rotation);
        var hasOblique = MathF.Abs(obliqueAngle) > 0.0001f;
        var shear = hasOblique ? MathF.Tan(obliqueAngle) : 0f;
        var points = new List<Vector2>(4);

        foreach (var corner in corners)
        {
            var transformed = new Vector2(corner.X * scaleX, corner.Y * scaleY);
            if (hasOblique)
            {
                transformed = new Vector2(transformed.X + transformed.Y * shear, transformed.Y);
            }

            var rotated = new Vector2(
                transformed.X * cos - transformed.Y * sin,
                transformed.X * sin + transformed.Y * cos);
            points.Add(anchor + rotated);
        }

        return points;
    }
}
