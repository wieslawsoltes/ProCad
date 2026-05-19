using System;
using System.Collections.Generic;

namespace ProCad.Rendering;

public enum RenderAnnotationKind
{
    Hover,
    Selection
}

public readonly struct RenderAnnotationStyle
{
    public RenderColor StrokeColor { get; }
    public RenderColor? FillColor { get; }
    public RenderColor LabelTextColor { get; }
    public RenderColor LabelBackgroundColor { get; }
    public float StrokeWidthPixels { get; }
    public float LabelTextSizePixels { get; }
    public float LabelPaddingPixels { get; }
    public float LabelGapPixels { get; }

    public RenderAnnotationStyle(
        RenderColor strokeColor,
        RenderColor? fillColor,
        RenderColor labelTextColor,
        RenderColor labelBackgroundColor,
        float strokeWidthPixels,
        float labelTextSizePixels,
        float labelPaddingPixels,
        float labelGapPixels)
    {
        StrokeColor = strokeColor;
        FillColor = fillColor;
        LabelTextColor = labelTextColor;
        LabelBackgroundColor = labelBackgroundColor;
        StrokeWidthPixels = strokeWidthPixels;
        LabelTextSizePixels = labelTextSizePixels;
        LabelPaddingPixels = labelPaddingPixels;
        LabelGapPixels = labelGapPixels;
    }

    public static RenderAnnotationStyle Hover =>
        new(
            strokeColor: new RenderColor(255, 200, 40, 220),
            fillColor: null,
            labelTextColor: new RenderColor(255, 255, 255, 230),
            labelBackgroundColor: new RenderColor(32, 32, 32, 190),
            strokeWidthPixels: 1.5f,
            labelTextSizePixels: 12f,
            labelPaddingPixels: 4f,
            labelGapPixels: 4f);

    public static RenderAnnotationStyle Selection =>
        new(
            strokeColor: new RenderColor(0, 200, 255, 230),
            fillColor: null,
            labelTextColor: new RenderColor(255, 255, 255, 230),
            labelBackgroundColor: new RenderColor(16, 32, 38, 200),
            strokeWidthPixels: 1.8f,
            labelTextSizePixels: 12f,
            labelPaddingPixels: 4f,
            labelGapPixels: 4f);
}

public readonly struct RenderAnnotation
{
    public RenderAnnotationKind Kind { get; }
    public RenderBounds Bounds { get; }
    public string Label { get; }
    public RenderAnnotationStyle Style { get; }
    public IReadOnlyList<IRenderPrimitive>? Geometry { get; }

    public RenderAnnotation(
        RenderAnnotationKind kind,
        RenderBounds bounds,
        string label,
        RenderAnnotationStyle style,
        IReadOnlyList<IRenderPrimitive>? geometry = null)
    {
        Kind = kind;
        Bounds = bounds;
        Label = label ?? string.Empty;
        Style = style;
        Geometry = geometry;
    }

    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);
    public bool HasGeometry => Geometry is { Count: > 0 };
}
