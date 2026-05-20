using System;
using ACadSharp.Entities;

namespace ProCad.Rendering;

/// <summary>
/// Provides a basic, deterministic text layout approximation for rendering.
/// </summary>
public sealed class DefaultRenderTextShaper : IRenderTextShaper
{
    public RenderTextLayout Shape(TextEntity text, CadRenderSceneSettings settings)
    {
        var value = text.Value ?? string.Empty;
        var height = RenderTextUtils.ResolveTextHeight(text);
        var width = EstimateLineWidth(value, height, settings.TextWidthFactor);
        return new RenderTextLayout(value, width, height);
    }

    public RenderTextLayout Shape(MText text, CadRenderSceneSettings settings)
    {
        var value = text.PlainText ?? string.Empty;
        var lines = SplitLines(value);
        var height = (float)text.Height;
        var lineHeight = height * MathF.Max(0.25f, (float)text.LineSpacing);
        var maxWidth = 0f;
        foreach (var line in lines)
        {
            var lineWidth = EstimateLineWidth(line, height, settings.TextWidthFactor);
            if (lineWidth > maxWidth)
            {
                maxWidth = lineWidth;
            }
        }

        var totalHeight = lineHeight * Math.Max(lines.Length, 1);
        return new RenderTextLayout(value, maxWidth, totalHeight);
    }

    private static float EstimateLineWidth(string value, float height, float widthFactor)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0f;
        }

        return value.Length * height * widthFactor;
    }


    private static string[] SplitLines(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Array.Empty<string>();
        }

        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }
}
