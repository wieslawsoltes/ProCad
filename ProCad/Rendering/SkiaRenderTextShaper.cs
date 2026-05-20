using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using ACadSharp.Entities;
using ACadSharp.Tables;
using SkiaSharp;

namespace ProCad.Rendering;

/// <summary>
/// Uses Skia text metrics for layout to match Skia rendering output.
/// </summary>
public sealed class SkiaRenderTextShaper : IRenderTextShaper
{
    private readonly DefaultRenderTextShaper _fallback = new();
    private readonly ConcurrentDictionary<TypefaceKey, SKTypeface> _typefaces = new();

    public RenderTextLayout Shape(TextEntity text, CadRenderSceneSettings settings)
    {
        try
        {
            return ShapeText(text);
        }
        catch (Exception)
        {
            return _fallback.Shape(text, settings);
        }
    }

    public RenderTextLayout Shape(MText text, CadRenderSceneSettings settings)
    {
        try
        {
            return ShapeMText(text);
        }
        catch (Exception)
        {
            return _fallback.Shape(text, settings);
        }
    }

    private RenderTextLayout ShapeText(TextEntity text)
    {
        var value = text.Value ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return new RenderTextLayout(string.Empty, 0f, 0f);
        }

        var height = RenderTextUtils.ResolveTextHeight(text);
        var typeface = ResolveTypeface(text.Style);
        using var paint = CreatePaint(typeface, height);
        var width = MeasureLineWidth(value, paint);
        var lineHeight = ResolveLineHeight(paint, height);
        return new RenderTextLayout(value, width, lineHeight);
    }

    private RenderTextLayout ShapeMText(MText text)
    {
        var value = text.PlainText ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return new RenderTextLayout(string.Empty, 0f, 0f);
        }

        var height = (float)text.Height;
        var lineHeight = height * MathF.Max(0.25f, (float)text.LineSpacing);
        var maxWidth = text.RectangleWidth > 0 ? (float)text.RectangleWidth : 0f;
        var typeface = ResolveTypeface(text.Style);
        using var paint = CreatePaint(typeface, height);

        var lines = SplitLines(value);
        var measuredLines = maxWidth > 0 ? WrapLines(lines, paint, maxWidth) : new List<string>(lines);
        var maxLineWidth = 0f;
        foreach (var line in measuredLines)
        {
            var lineWidth = MeasureLineWidth(line, paint);
            if (lineWidth > maxLineWidth)
            {
                maxLineWidth = lineWidth;
            }
        }

        var totalHeight = lineHeight * Math.Max(measuredLines.Count, 1);
        return new RenderTextLayout(value, maxLineWidth, totalHeight);
    }

    private static SKPaint CreatePaint(SKTypeface typeface, float size)
    {
        return new SKPaint
        {
            IsAntialias = true,
            Typeface = typeface,
            TextSize = size
        };
    }

    private static float MeasureLineWidth(string value, SKPaint paint)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0f;
        }

        return paint.MeasureText(value);
    }

    private static float ResolveLineHeight(SKPaint paint, float fallbackHeight)
    {
        var metrics = paint.FontMetrics;
        var height = metrics.Descent - metrics.Ascent;
        if (height <= 0f || float.IsNaN(height) || float.IsInfinity(height))
        {
            return fallbackHeight;
        }

        return height;
    }

    private static List<string> WrapLines(string[] lines, SKPaint paint, float maxWidth)
    {
        var result = new List<string>();
        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line))
            {
                result.Add(string.Empty);
                continue;
            }

            if (MeasureLineWidth(line, paint) <= maxWidth)
            {
                result.Add(line);
                continue;
            }

            WrapLine(line, paint, maxWidth, result);
        }

        return result;
    }

    private static void WrapLine(string line, SKPaint paint, float maxWidth, List<string> output)
    {
        var start = 0;
        var lastBreak = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (char.IsWhiteSpace(line[i]))
            {
                lastBreak = i;
            }

            var current = line.Substring(start, i - start + 1);
            if (MeasureLineWidth(current, paint) <= maxWidth)
            {
                continue;
            }

            var breakIndex = lastBreak >= start ? lastBreak : i - 1;
            if (breakIndex < start)
            {
                breakIndex = i - 1;
            }

            var length = Math.Max(breakIndex - start + 1, 0);
            var segment = length > 0 ? line.Substring(start, length) : string.Empty;
            output.Add(segment.TrimEnd());

            start = breakIndex + 1;
            while (start < line.Length && char.IsWhiteSpace(line[start]))
            {
                start++;
            }

            i = start - 1;
            lastBreak = -1;
        }

        if (start < line.Length)
        {
            output.Add(line.Substring(start));
        }
        else if (output.Count == 0)
        {
            output.Add(string.Empty);
        }
    }

    private SKTypeface ResolveTypeface(TextStyle style)
    {
        var family = ResolveFontFamily(style);
        var weight = ResolveFontWeight(style);
        var slant = ResolveFontSlant(style);
        var key = new TypefaceKey(family, weight, (int)slant);
        if (_typefaces.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var fontStyle = new SKFontStyle(weight, (int)SKFontStyleWidth.Normal, slant);
        var resolved = SKTypeface.FromFamilyName(family, fontStyle) ?? SKTypeface.Default;
        _typefaces[key] = resolved;
        return resolved;
    }

    private static string ResolveFontFamily(TextStyle style)
    {
        if (style is null || string.IsNullOrWhiteSpace(style.Filename))
        {
            return SKTypeface.Default.FamilyName;
        }

        return Path.GetFileNameWithoutExtension(style.Filename);
    }

    private static int ResolveFontWeight(TextStyle style)
    {
        if (style is null)
        {
            return (int)SKFontStyleWeight.Normal;
        }

        return style.TrueType.HasFlag(FontFlags.Bold)
            ? (int)SKFontStyleWeight.Bold
            : (int)SKFontStyleWeight.Normal;
    }

    private static SKFontStyleSlant ResolveFontSlant(TextStyle style)
    {
        if (style is null)
        {
            return SKFontStyleSlant.Upright;
        }

        return style.TrueType.HasFlag(FontFlags.Italic)
            ? SKFontStyleSlant.Italic
            : SKFontStyleSlant.Upright;
    }

    private static string[] SplitLines(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Array.Empty<string>();
        }

        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }

    private readonly record struct TypefaceKey(
        string Family,
        int Weight,
        int Slant);
}
