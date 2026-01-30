using System;
using System.Globalization;
using System.IO;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Avalonia.Media;

namespace ACadInspector.Rendering;

/// <summary>
/// Uses Avalonia text layout for more accurate text metrics when available.
/// </summary>
public sealed class AvaloniaRenderTextShaper : IRenderTextShaper
{
    private readonly DefaultRenderTextShaper _fallback = new();

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

    private static RenderTextLayout ShapeText(TextEntity text)
    {
        var value = text.Value ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return new RenderTextLayout(string.Empty, 0f, 0f);
        }

        var height = RenderTextUtils.ResolveTextHeight(text);
        var formatted = BuildFormattedText(value, ResolveTypeface(text.Style), height, lineHeight: null, maxWidth: null);
        return new RenderTextLayout(value, (float)formatted.Width, (float)formatted.Height);
    }

    private static RenderTextLayout ShapeMText(MText text)
    {
        var value = text.PlainText ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return new RenderTextLayout(string.Empty, 0f, 0f);
        }

        var lineHeight = text.Height * Math.Max(0.25, text.LineSpacing);
        var maxWidth = text.RectangleWidth > 0 ? text.RectangleWidth : (double?)null;
        var formatted = BuildFormattedText(value, ResolveTypeface(text.Style), text.Height, lineHeight, maxWidth);
        return new RenderTextLayout(value, (float)formatted.Width, (float)formatted.Height);
    }

    private static FormattedText BuildFormattedText(
        string text,
        Typeface typeface,
        double fontSize,
        double? lineHeight,
        double? maxWidth)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black);

        if (lineHeight.HasValue && lineHeight.Value > 0)
        {
            formatted.LineHeight = lineHeight.Value;
        }

        if (maxWidth.HasValue && maxWidth.Value > 0)
        {
            formatted.MaxTextWidth = maxWidth.Value;
        }

        return formatted;
    }

    private static Typeface ResolveTypeface(TextStyle style)
    {
        var family = ResolveFontFamily(style);
        var fontStyle = ResolveFontStyle(style);
        var fontWeight = ResolveFontWeight(style);
        if (!string.IsNullOrWhiteSpace(family))
        {
            return new Typeface(new FontFamily(family), fontStyle, fontWeight);
        }

        return new Typeface(FontFamily.Default, fontStyle, fontWeight);
    }

    private static string? ResolveFontFamily(TextStyle style)
    {
        if (style is null || string.IsNullOrWhiteSpace(style.Filename))
        {
            return null;
        }

        return Path.GetFileNameWithoutExtension(style.Filename);
    }

    private static FontStyle ResolveFontStyle(TextStyle style)
    {
        if (style is null)
        {
            return FontStyle.Normal;
        }

        return style.TrueType.HasFlag(FontFlags.Italic) ? FontStyle.Italic : FontStyle.Normal;
    }

    private static FontWeight ResolveFontWeight(TextStyle style)
    {
        if (style is null)
        {
            return FontWeight.Normal;
        }

        return style.TrueType.HasFlag(FontFlags.Bold) ? FontWeight.Bold : FontWeight.Normal;
    }

}
