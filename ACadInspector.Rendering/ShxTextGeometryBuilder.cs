using System;
using System.Collections.Generic;
using System.Numerics;
using ACadSharp.Tables;

namespace ACadInspector.Rendering;

internal static class ShxTextGeometryBuilder
{
    private const float Epsilon = 0.000001f;

    public static bool TryAddText(
        RenderLayerBuilder builder,
        string text,
        TextStyle style,
        RenderBuildContext context,
        Vector2 anchor,
        Vector2 offset,
        float height,
        float widthFactor,
        float rotation,
        float obliqueAngle,
        bool mirrorX,
        bool mirrorY,
        RenderColor color)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (style is null || string.IsNullOrWhiteSpace(style.Filename))
        {
            return false;
        }

        if (!IsShxFontName(style.Filename))
        {
            return false;
        }

        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);
        var shear = MathF.Tan(obliqueAngle);
        var penX = 0f;
        var any = false;

        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                continue;
            }

            if (!context.ShapeResolver.TryResolveShape(style.Filename, (short)ch, context.Settings, out var geometry))
            {
                penX += height * widthFactor;
                continue;
            }

            var bounds = geometry.Bounds;
            var nominalHeight = bounds.Size.Y;
            if (nominalHeight <= Epsilon)
            {
                nominalHeight = 1f;
            }

            var scale = height / nominalHeight;
            var scaleX = scale * widthFactor;
            var scaleY = scale;
            var advance = bounds.Size.X * scaleX;
            if (advance <= Epsilon)
            {
                advance = height * widthFactor;
            }

            foreach (var contour in geometry.Contours)
            {
                if (contour.Count == 0)
                {
                    continue;
                }

                var points = new List<Vector2>(contour.Count);
                foreach (var point in contour)
                {
                    var localX = (point.X - bounds.Min.X) * scaleX + penX + offset.X;
                    var localY = point.Y * scaleY + offset.Y;
                    localX += localY * shear;

                    if (mirrorX)
                    {
                        localX = -localX;
                    }

                    if (mirrorY)
                    {
                        localY = -localY;
                    }

                    var rotated = new Vector2(
                        localX * cos - localY * sin,
                        localX * sin + localY * cos);

                    points.Add(anchor + rotated);
                }

                if (points.Count == 1)
                {
                    builder.Add(new RenderPoint(
                        points[0],
                        color,
                        thickness: 0f,
                        RenderLineCap.Round,
                        RenderLineJoin.Round,
                        context.Settings.PointDisplayMode,
                        context.Settings.PointDisplaySize));
                }
                else
                {
                    builder.Add(new RenderPolyline(points, isClosed: false, color, thickness: 0f, RenderLineCap.Round, RenderLineJoin.Round));
                }
            }

            penX += advance;
            any = true;
        }

        return any;
    }

    private static bool IsShxFontName(string filename)
    {
        var extension = System.IO.Path.GetExtension(filename);
        if (string.IsNullOrEmpty(extension))
        {
            return true;
        }

        return extension.Equals(".shx", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".shp", StringComparison.OrdinalIgnoreCase);
    }
}
