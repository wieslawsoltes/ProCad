using System.Numerics;

namespace ACadInspector.Rendering;

public interface IRenderPrimitive
{
    RenderColor Color { get; }
    float Thickness { get; }
    RenderBounds Bounds { get; }
}

public sealed class RenderLine : IRenderPrimitive
{
    public Vector2 Start { get; }
    public Vector2 End { get; }
    public RenderColor Color { get; }
    public float Thickness { get; }
    public RenderLineCap LineCap { get; }
    public RenderLineJoin LineJoin { get; }
    public float? StartDepth { get; }
    public float? EndDepth { get; }
    public RenderBounds Bounds { get; }

    public RenderLine(
        Vector2 start,
        Vector2 end,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        float? startDepth = null,
        float? endDepth = null)
    {
        Start = start;
        End = end;
        Color = color;
        Thickness = thickness;
        LineCap = lineCap;
        LineJoin = lineJoin;
        StartDepth = startDepth;
        EndDepth = endDepth;
        Bounds = RenderBounds.Empty.Expand(start).Expand(end);
    }

    public bool HasDepth => StartDepth.HasValue && EndDepth.HasValue;
}

public sealed class RenderPolyline : IRenderPrimitive
{
    public IReadOnlyList<Vector2> Points { get; }
    public bool IsClosed { get; }
    public RenderColor Color { get; }
    public float Thickness { get; }
    public RenderLineCap LineCap { get; }
    public RenderLineJoin LineJoin { get; }
    public IReadOnlyList<float>? Depths { get; }
    public RenderBounds Bounds { get; }

    public RenderPolyline(
        IReadOnlyList<Vector2> points,
        bool isClosed,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        IReadOnlyList<float>? depths = null)
    {
        Points = points;
        IsClosed = isClosed;
        Color = color;
        Thickness = thickness;
        LineCap = lineCap;
        LineJoin = lineJoin;
        Depths = depths;

        var bounds = RenderBounds.Empty;
        foreach (var point in points)
        {
            bounds = bounds.Expand(point);
        }
        Bounds = bounds;
    }

    public bool HasDepths => Depths is not null && Depths.Count == Points.Count;
}

public sealed class RenderFill : IRenderPrimitive
{
    public IReadOnlyList<Vector2> Points { get; }
    public RenderColor Color { get; }
    public float Thickness { get; }
    public RenderBounds Bounds { get; }

    public RenderFill(IReadOnlyList<Vector2> points, RenderColor color)
    {
        Points = points;
        Color = color;
        Thickness = 0f;
        var bounds = RenderBounds.Empty;
        foreach (var point in points)
        {
            bounds = bounds.Expand(point);
        }
        Bounds = bounds;
    }
}

/// <summary>
/// Represents a filled triangle primitive.
/// </summary>
public sealed class RenderTriangle : IRenderPrimitive
{
    public Vector2 A { get; }
    public Vector2 B { get; }
    public Vector2 C { get; }
    public float DepthA { get; }
    public float DepthB { get; }
    public float DepthC { get; }
    public RenderColor Color { get; }
    public float Shade { get; }
    public float Thickness { get; }
    public RenderBounds Bounds { get; }

    public RenderTriangle(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        RenderColor color,
        float shade = 1f,
        float depthA = 0f,
        float depthB = 0f,
        float depthC = 0f)
    {
        A = a;
        B = b;
        C = c;
        DepthA = depthA;
        DepthB = depthB;
        DepthC = depthC;
        Color = color;
        Shade = shade;
        Thickness = 0f;
        Bounds = RenderBounds.Empty.Expand(a).Expand(b).Expand(c);
    }
}

/// <summary>
/// Represents an image placed in world space.
/// </summary>
public sealed class RenderImage : IRenderPrimitive
{
    public string? SourcePath { get; }
    public string? Label { get; }
    public Vector2 Origin { get; }
    public Vector2 UVector { get; }
    public Vector2 VVector { get; }
    public Vector2 Size { get; }
    public RenderColor Color { get; }
    public float Opacity { get; }
    public float Thickness { get; }
    public RenderBounds Bounds { get; }

    public RenderImage(
        string? sourcePath,
        string? label,
        Vector2 origin,
        Vector2 uVector,
        Vector2 vVector,
        Vector2 size,
        RenderColor color,
        float opacity)
    {
        SourcePath = sourcePath;
        Label = label;
        Origin = origin;
        UVector = uVector;
        VVector = vVector;
        Size = size;
        Color = color;
        Opacity = opacity;
        Thickness = 0f;
        Bounds = ComputeBounds(origin, uVector, vVector, size);
    }

    private static RenderBounds ComputeBounds(Vector2 origin, Vector2 uVector, Vector2 vVector, Vector2 size)
    {
        var p0 = origin;
        var p1 = origin + uVector * size.X;
        var p2 = origin + vVector * size.Y;
        var p3 = origin + uVector * size.X + vVector * size.Y;
        return RenderBounds.Empty.Expand(p0).Expand(p1).Expand(p2).Expand(p3);
    }
}

/// <summary>
/// Represents a hatch fill with optional gradient and support for holes.
/// </summary>
public sealed class RenderHatchFill : IRenderPrimitive
{
    public IReadOnlyList<IReadOnlyList<Vector2>> Loops { get; }
    public RenderColor Color { get; }
    public RenderHatchGradient? Gradient { get; }
    public float Thickness { get; }
    public RenderBounds Bounds { get; }

    public RenderHatchFill(
        IReadOnlyList<IReadOnlyList<Vector2>> loops,
        RenderColor color,
        RenderHatchGradient? gradient)
    {
        Loops = loops;
        Color = color;
        Gradient = gradient;
        Thickness = 0f;
        Bounds = ComputeBounds(loops);
    }

    private static RenderBounds ComputeBounds(IReadOnlyList<IReadOnlyList<Vector2>> loops)
    {
        var bounds = RenderBounds.Empty;
        foreach (var loop in loops)
        {
            foreach (var point in loop)
            {
                bounds = bounds.Expand(point);
            }
        }

        return bounds;
    }
}

/// <summary>
/// Represents a hatch pattern rendered as clipped line segments.
/// </summary>
public sealed class RenderHatchPattern : IRenderPrimitive
{
    public IReadOnlyList<IReadOnlyList<Vector2>> Loops { get; }
    public IReadOnlyList<RenderHatchLineSegment> Segments { get; }
    public RenderColor Color { get; }
    public float Thickness { get; }
    public RenderLineCap LineCap { get; }
    public RenderLineJoin LineJoin { get; }
    public RenderBounds Bounds { get; }

    public RenderHatchPattern(
        IReadOnlyList<IReadOnlyList<Vector2>> loops,
        IReadOnlyList<RenderHatchLineSegment> segments,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin)
    {
        Loops = loops;
        Segments = segments;
        Color = color;
        Thickness = thickness;
        LineCap = lineCap;
        LineJoin = lineJoin;
        Bounds = ComputeBounds(loops);
    }

    private static RenderBounds ComputeBounds(IReadOnlyList<IReadOnlyList<Vector2>> loops)
    {
        var bounds = RenderBounds.Empty;
        foreach (var loop in loops)
        {
            foreach (var point in loop)
            {
                bounds = bounds.Expand(point);
            }
        }

        return bounds;
    }
}

/// <summary>
/// Represents a clipped group of primitives.
/// </summary>
public sealed class RenderClipGroup : IRenderPrimitive
{
    public IReadOnlyList<IReadOnlyList<Vector2>> Loops { get; }
    public IReadOnlyList<IRenderPrimitive> Primitives { get; }
    public RenderColor Color { get; }
    public float Thickness { get; }
    public RenderBounds Bounds { get; }

    public RenderClipGroup(
        IReadOnlyList<IReadOnlyList<Vector2>> loops,
        IReadOnlyList<IRenderPrimitive> primitives)
    {
        Loops = loops;
        Primitives = primitives;
        Color = new RenderColor(0, 0, 0, 0);
        Thickness = 0f;
        Bounds = ComputeBounds(loops, primitives);
    }

    private static RenderBounds ComputeBounds(
        IReadOnlyList<IReadOnlyList<Vector2>> loops,
        IReadOnlyList<IRenderPrimitive> primitives)
    {
        var bounds = RenderBounds.Empty;
        if (loops is not null)
        {
            foreach (var loop in loops)
            {
                foreach (var point in loop)
                {
                    bounds = bounds.Expand(point);
                }
            }
        }

        if (!bounds.IsEmpty || primitives is null)
        {
            return bounds;
        }

        foreach (var primitive in primitives)
        {
            bounds = bounds.Expand(primitive.Bounds);
        }

        return bounds;
    }
}

/// <summary>
/// Describes a hatch line segment.
/// </summary>
public readonly struct RenderHatchLineSegment
{
    public Vector2 Start { get; }
    public Vector2 End { get; }

    public RenderHatchLineSegment(Vector2 start, Vector2 end)
    {
        Start = start;
        End = end;
    }
}

/// <summary>
/// Describes a hatch gradient fill.
/// </summary>
public sealed class RenderHatchGradient
{
    public RenderHatchGradientType Type { get; }
    public float Angle { get; }
    public float Shift { get; }
    public IReadOnlyList<RenderHatchGradientStop> Stops { get; }

    public RenderHatchGradient(RenderHatchGradientType type, float angle, float shift, IReadOnlyList<RenderHatchGradientStop> stops)
    {
        Type = type;
        Angle = angle;
        Shift = shift;
        Stops = stops;
    }
}

/// <summary>
/// Defines a hatch gradient stop.
/// </summary>
public readonly struct RenderHatchGradientStop
{
    public float Offset { get; }
    public RenderColor Color { get; }

    public RenderHatchGradientStop(float offset, RenderColor color)
    {
        Offset = offset;
        Color = color;
    }
}

/// <summary>
/// Defines supported hatch gradient types.
/// </summary>
public enum RenderHatchGradientType
{
    Linear = 0,
    Radial = 1
}

public sealed class RenderCircle : IRenderPrimitive
{
    public Vector2 Center { get; }
    public float Radius { get; }
    public RenderColor Color { get; }
    public float Thickness { get; }
    public RenderLineCap LineCap { get; }
    public RenderLineJoin LineJoin { get; }
    public RenderBounds Bounds { get; }

    public RenderCircle(
        Vector2 center,
        float radius,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin)
    {
        Center = center;
        Radius = radius;
        Color = color;
        Thickness = thickness;
        LineCap = lineCap;
        LineJoin = lineJoin;
        var min = center - new Vector2(radius, radius);
        var max = center + new Vector2(radius, radius);
        Bounds = new RenderBounds(min, max);
    }
}

public sealed class RenderArc : IRenderPrimitive
{
    public Vector2 Center { get; }
    public float Radius { get; }
    public float StartAngle { get; }
    public float EndAngle { get; }
    public RenderColor Color { get; }
    public float Thickness { get; }
    public RenderLineCap LineCap { get; }
    public RenderLineJoin LineJoin { get; }
    public RenderBounds Bounds { get; }

    public RenderArc(
        Vector2 center,
        float radius,
        float startAngle,
        float endAngle,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin)
    {
        Center = center;
        Radius = radius;
        StartAngle = startAngle;
        EndAngle = endAngle;
        Color = color;
        Thickness = thickness;
        LineCap = lineCap;
        LineJoin = lineJoin;
        var min = center - new Vector2(radius, radius);
        var max = center + new Vector2(radius, radius);
        Bounds = new RenderBounds(min, max);
    }
}

public sealed class RenderPoint : IRenderPrimitive
{
    public Vector2 Point { get; }
    public RenderColor Color { get; }
    public float Thickness { get; }
    public RenderLineCap LineCap { get; }
    public RenderLineJoin LineJoin { get; }
    public RenderBounds Bounds { get; }

    public RenderPoint(
        Vector2 point,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin)
    {
        Point = point;
        Color = color;
        Thickness = thickness;
        LineCap = lineCap;
        LineJoin = lineJoin;
        Bounds = RenderBounds.Empty.Expand(point);
    }
}

/// <summary>
/// Represents a text label with approximate layout metrics.
/// </summary>
public sealed class RenderText : IRenderPrimitive
{
    public string Text { get; }
    public Vector2 Anchor { get; }
    public Vector2 Offset { get; }
    public float LayoutWidth { get; }
    public float LayoutHeight { get; }
    public float FontSize { get; }
    public float WidthFactor { get; }
    public float Rotation { get; }
    /// <summary>
    /// Gets the oblique (shear) angle in radians.
    /// </summary>
    public float ObliqueAngle { get; }
    /// <summary>
    /// Gets a value indicating whether the font should be rendered bold.
    /// </summary>
    public bool IsBold { get; }
    /// <summary>
    /// Gets a value indicating whether the font should be rendered italic.
    /// </summary>
    public bool IsItalic { get; }
    /// <summary>
    /// Gets a value indicating whether the text should be mirrored in X.
    /// </summary>
    public bool MirrorX { get; }
    /// <summary>
    /// Gets a value indicating whether the text should be mirrored in Y.
    /// </summary>
    public bool MirrorY { get; }
    public string? FontFamily { get; }
    public RenderColor Color { get; }
    public float Thickness { get; }
    public RenderBounds Bounds { get; }

    public RenderText(
        string text,
        Vector2 anchor,
        Vector2 offset,
        float layoutWidth,
        float layoutHeight,
        float fontSize,
        float widthFactor,
        float rotation,
        float obliqueAngle,
        bool isBold,
        bool isItalic,
        bool mirrorX,
        bool mirrorY,
        RenderColor color,
        string? fontFamily)
    {
        Text = text;
        Anchor = anchor;
        Offset = offset;
        LayoutWidth = layoutWidth;
        LayoutHeight = layoutHeight;
        FontSize = fontSize;
        WidthFactor = widthFactor;
        Rotation = rotation;
        ObliqueAngle = obliqueAngle;
        IsBold = isBold;
        IsItalic = isItalic;
        MirrorX = mirrorX;
        MirrorY = mirrorY;
        Color = color;
        FontFamily = fontFamily;
        Thickness = 0f;
        Bounds = ComputeBounds(anchor, offset, layoutWidth, layoutHeight, widthFactor, rotation, obliqueAngle, mirrorX, mirrorY);
    }

    private static RenderBounds ComputeBounds(
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
        var bounds = RenderBounds.Empty;

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
            bounds = bounds.Expand(anchor + rotated);
        }

        return bounds;
    }
}
