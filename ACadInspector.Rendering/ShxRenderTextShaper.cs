using System;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace ACadInspector.Rendering;

/// <summary>
/// Uses SHX/SHP fonts for text layout when available, otherwise falls back to the provided shaper.
/// </summary>
public sealed class ShxRenderTextShaper : IRenderTextShaper
{
    private readonly IShxFontResolver _fontResolver;
    private readonly IRenderTextShaper _fallback;

    public ShxRenderTextShaper(IShxFontResolver fontResolver, IRenderTextShaper fallback)
    {
        _fontResolver = fontResolver ?? throw new ArgumentNullException(nameof(fontResolver));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public RenderTextLayout Shape(TextEntity text, CadRenderSceneSettings settings)
    {
        if (!TryGetFont(text.Style, settings, out var font))
        {
            return _fallback.Shape(text, settings);
        }

        var value = text.Value ?? string.Empty;
        var height = RenderTextUtils.ResolveTextHeight(text);
        var width = EstimateLineWidth(value, height, settings.TextWidthFactor, font);
        return new RenderTextLayout(value, width, height);
    }

    public RenderTextLayout Shape(MText text, CadRenderSceneSettings settings)
    {
        if (!TryGetFont(text.Style, settings, out var font))
        {
            return _fallback.Shape(text, settings);
        }

        var value = text.PlainText ?? string.Empty;
        var lines = SplitLines(value);
        var height = (float)text.Height;
        var lineHeight = height * MathF.Max(0.25f, (float)text.LineSpacing);
        var maxWidth = 0f;
        foreach (var line in lines)
        {
            var lineWidth = EstimateLineWidth(line, height, settings.TextWidthFactor, font);
            if (lineWidth > maxWidth)
            {
                maxWidth = lineWidth;
            }
        }

        var totalHeight = lineHeight * Math.Max(lines.Length, 1);
        return new RenderTextLayout(value, maxWidth, totalHeight);
    }

    private bool TryGetFont(TextStyle style, CadRenderSceneSettings settings, out IShxFont font)
    {
        font = null!;
        var filename = style?.Filename;
        if (string.IsNullOrWhiteSpace(filename))
        {
            return false;
        }

        if (!IsShxFontName(filename))
        {
            return false;
        }

        if (!_fontResolver.TryGetFont(filename, settings, out var resolved))
        {
            font = null!;
            return false;
        }

        font = resolved;
        return true;
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

    private static float EstimateLineWidth(string value, float height, float fallbackWidthFactor, IShxFont font)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0f;
        }

        var width = 0f;
        foreach (var ch in value)
        {
            if (!font.TryGetGlyph(ch, out var glyph))
            {
                width += height * fallbackWidthFactor;
                continue;
            }

            var glyphHeight = glyph.NominalHeight;
            var scale = glyphHeight > 0f ? height / glyphHeight : height;
            var advance = glyph.NominalWidth * scale;
            if (advance <= 0f)
            {
                advance = height * fallbackWidthFactor;
            }

            width += advance;
        }

        return width;
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
