using System.Numerics;

namespace ACadInspector.Rendering;

public enum RenderOverlayPrimitiveKind
{
    PointMarker = 0,
    Line,
    Rectangle,
    FilledRectangle,
    Text,
    SquareMarker,
    DiamondMarker,
    CrossMarker
}

public enum RenderOverlayStrokeStyle
{
    Solid = 0,
    Dashed,
    Dotted
}

public sealed record RenderOverlayPrimitive(
    RenderOverlayPrimitiveKind Kind,
    Vector2 Start,
    Vector2 End,
    RenderColor Color,
    float StrokeWidth = 1f,
    float MarkerRadius = 4f,
    string? Text = null,
    RenderOverlayStrokeStyle StrokeStyle = RenderOverlayStrokeStyle.Solid,
    RenderColor? FillColor = null,
    int Priority = 0);
