using System;
using System.Collections.Generic;
using System.Numerics;

namespace ACadInspector.Rendering;

public readonly struct RenderLinePattern
{
    private readonly RenderLinePatternSegment[] _segments;

    /// <summary>
    /// Represents a continuous (unpatterned) stroke.
    /// </summary>
    public static RenderLinePattern Continuous => new(Array.Empty<RenderLinePatternSegment>());

    /// <summary>
    /// Gets the pattern segments in draw/space order.
    /// </summary>
    public RenderLinePatternSegment[] Segments => _segments;

    /// <summary>
    /// Gets a value indicating whether the pattern is continuous.
    /// </summary>
    public bool IsContinuous => _segments.Length == 0;

    /// <summary>
    /// Gets a value indicating whether the pattern includes complex text/shape segments.
    /// </summary>
    public bool HasDecorations
    {
        get
        {
            foreach (var segment in _segments)
            {
                if (segment.IsText || segment.IsShape)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Attempts to extract a simple dash pattern (no text/shape segments).
    /// </summary>
    public bool TryGetDashPattern(out float[] intervals, out float phase)
    {
        intervals = Array.Empty<float>();
        phase = 0f;

        if (_segments.Length == 0)
        {
            return false;
        }

        if (HasDecorations)
        {
            return false;
        }

        var merged = new List<SegmentEntry>(_segments.Length);
        foreach (var segment in _segments)
        {
            if (segment.Length <= 0f)
            {
                continue;
            }

            var isDraw = segment.IsDraw;
            if (merged.Count > 0 && merged[^1].IsDraw == isDraw)
            {
                merged[^1] = merged[^1] with { Length = merged[^1].Length + segment.Length };
                continue;
            }

            merged.Add(new SegmentEntry(isDraw, segment.Length));
        }

        if (merged.Count == 0)
        {
            return false;
        }

        if (merged.Count > 1 && merged[0].IsDraw == merged[^1].IsDraw)
        {
            merged[0] = merged[0] with { Length = merged[0].Length + merged[^1].Length };
            merged.RemoveAt(merged.Count - 1);
        }

        if (merged.Count == 1 && merged[0].IsDraw)
        {
            return false;
        }

        var startIndex = 0;
        if (!merged[0].IsDraw)
        {
            phase = merged[0].Length;
            startIndex = 1;
        }

        if (startIndex >= merged.Count)
        {
            return false;
        }

        var list = new List<float>(merged.Count);
        var expectDraw = true;
        for (var i = startIndex; i < merged.Count; i++)
        {
            var entry = merged[i];
            if (entry.Length <= 0f)
            {
                continue;
            }

            if (entry.IsDraw != expectDraw)
            {
                expectDraw = entry.IsDraw;
            }

            list.Add(entry.Length);
            expectDraw = !expectDraw;
        }

        if (list.Count < 2)
        {
            return false;
        }

        if (list.Count % 2 != 0)
        {
            list.Add(list[^1]);
        }

        intervals = list.ToArray();
        return true;
    }

    /// <summary>
    /// Creates a new line pattern from segments.
    /// </summary>
    public RenderLinePattern(RenderLinePatternSegment[] segments)
    {
        _segments = segments ?? Array.Empty<RenderLinePatternSegment>();
    }

    private readonly record struct SegmentEntry(bool IsDraw, float Length);
}

public readonly struct RenderLinePatternSegment
{
    private readonly RenderLinePatternSegmentKind _kind;
    private readonly Vector2 _offset;
    private readonly float _rotation;
    private readonly bool _rotationIsAbsolute;
    private readonly float _scale;
    private readonly string? _text;
    private readonly float _layoutWidth;
    private readonly float _layoutHeight;
    private readonly float _fontSize;
    private readonly float _widthFactor;
    private readonly float _obliqueAngle;
    private readonly bool _isBold;
    private readonly bool _isItalic;
    private readonly bool _mirrorX;
    private readonly bool _mirrorY;
    private readonly string? _fontFamily;
    private readonly string? _shapeFile;
    private readonly short _shapeNumber;

    /// <summary>
    /// Gets the segment length in drawing units.
    /// </summary>
    public float Length { get; }
    /// <summary>
    /// Gets a value indicating whether the segment is drawn.
    /// </summary>
    public bool IsDraw => _kind == RenderLinePatternSegmentKind.Draw;
    /// <summary>
    /// Gets a value indicating whether the segment is a space.
    /// </summary>
    public bool IsSpace => _kind == RenderLinePatternSegmentKind.Space;
    /// <summary>
    /// Gets a value indicating whether the segment represents linetype text.
    /// </summary>
    public bool IsText => _kind == RenderLinePatternSegmentKind.Text;
    /// <summary>
    /// Gets a value indicating whether the segment represents a linetype shape.
    /// </summary>
    public bool IsShape => _kind == RenderLinePatternSegmentKind.Shape;
    /// <summary>
    /// Gets the local offset of a complex segment.
    /// </summary>
    public Vector2 Offset => _offset;
    /// <summary>
    /// Gets the rotation of a complex segment in radians.
    /// </summary>
    public float Rotation => _rotation;
    /// <summary>
    /// Gets a value indicating whether the rotation is absolute.
    /// </summary>
    public bool RotationIsAbsolute => _rotationIsAbsolute;
    /// <summary>
    /// Gets the scale of a complex segment.
    /// </summary>
    public float Scale => _scale;
    /// <summary>
    /// Gets the text for a linetype text segment.
    /// </summary>
    public string? Text => _text;
    /// <summary>
    /// Gets the text layout width for a linetype text segment.
    /// </summary>
    public float LayoutWidth => _layoutWidth;
    /// <summary>
    /// Gets the text layout height for a linetype text segment.
    /// </summary>
    public float LayoutHeight => _layoutHeight;
    /// <summary>
    /// Gets the font size for a linetype text segment.
    /// </summary>
    public float FontSize => _fontSize;
    /// <summary>
    /// Gets the width factor for a linetype text segment.
    /// </summary>
    public float WidthFactor => _widthFactor;
    /// <summary>
    /// Gets the oblique angle for a linetype text segment.
    /// </summary>
    public float ObliqueAngle => _obliqueAngle;
    /// <summary>
    /// Gets a value indicating whether the linetype text should be bold.
    /// </summary>
    public bool IsBold => _isBold;
    /// <summary>
    /// Gets a value indicating whether the linetype text should be italic.
    /// </summary>
    public bool IsItalic => _isItalic;
    /// <summary>
    /// Gets a value indicating whether the linetype text is mirrored in X.
    /// </summary>
    public bool MirrorX => _mirrorX;
    /// <summary>
    /// Gets a value indicating whether the linetype text is mirrored in Y.
    /// </summary>
    public bool MirrorY => _mirrorY;
    /// <summary>
    /// Gets the font family for a linetype text segment.
    /// </summary>
    public string? FontFamily => _fontFamily;
    /// <summary>
    /// Gets the shape number for a linetype shape segment.
    /// </summary>
    public short ShapeNumber => _shapeNumber;
    /// <summary>
    /// Gets the shape file for a linetype shape segment.
    /// </summary>
    public string? ShapeFile => _shapeFile;

    /// <summary>
    /// Creates a new line pattern segment.
    /// </summary>
    public RenderLinePatternSegment(float length, bool isDraw)
    {
        Length = length;
        _kind = isDraw ? RenderLinePatternSegmentKind.Draw : RenderLinePatternSegmentKind.Space;
        _offset = default;
        _rotation = 0f;
        _rotationIsAbsolute = false;
        _scale = 1f;
        _text = null;
        _layoutWidth = 0f;
        _layoutHeight = 0f;
        _fontSize = 0f;
        _widthFactor = 1f;
        _obliqueAngle = 0f;
        _isBold = false;
        _isItalic = false;
        _mirrorX = false;
        _mirrorY = false;
        _fontFamily = null;
        _shapeFile = null;
        _shapeNumber = 0;
    }

    private RenderLinePatternSegment(
        RenderLinePatternSegmentKind kind,
        float length,
        Vector2 offset,
        float rotation,
        bool rotationIsAbsolute,
        float scale,
        string? text,
        float layoutWidth,
        float layoutHeight,
        float fontSize,
        float widthFactor,
        float obliqueAngle,
        bool isBold,
        bool isItalic,
        bool mirrorX,
        bool mirrorY,
        string? fontFamily,
        string? shapeFile,
        short shapeNumber)
    {
        Length = length;
        _kind = kind;
        _offset = offset;
        _rotation = rotation;
        _rotationIsAbsolute = rotationIsAbsolute;
        _scale = scale;
        _text = text;
        _layoutWidth = layoutWidth;
        _layoutHeight = layoutHeight;
        _fontSize = fontSize;
        _widthFactor = widthFactor;
        _obliqueAngle = obliqueAngle;
        _isBold = isBold;
        _isItalic = isItalic;
        _mirrorX = mirrorX;
        _mirrorY = mirrorY;
        _fontFamily = fontFamily;
        _shapeFile = shapeFile;
        _shapeNumber = shapeNumber;
    }

    /// <summary>
    /// Creates a linetype text segment.
    /// </summary>
    public static RenderLinePatternSegment CreateText(
        float length,
        Vector2 offset,
        float rotation,
        bool rotationIsAbsolute,
        float scale,
        string text,
        float layoutWidth,
        float layoutHeight,
        float fontSize,
        float widthFactor,
        float obliqueAngle,
        bool isBold,
        bool isItalic,
        bool mirrorX,
        bool mirrorY,
        string? fontFamily)
    {
        return new RenderLinePatternSegment(
            RenderLinePatternSegmentKind.Text,
            length,
            offset,
            rotation,
            rotationIsAbsolute,
            scale,
            text,
            layoutWidth,
            layoutHeight,
            fontSize,
            widthFactor,
            obliqueAngle,
            isBold,
            isItalic,
            mirrorX,
            mirrorY,
            fontFamily,
            null,
            0);
    }

    /// <summary>
    /// Creates a linetype shape segment.
    /// </summary>
    public static RenderLinePatternSegment CreateShape(
        float length,
        Vector2 offset,
        float rotation,
        bool rotationIsAbsolute,
        float scale,
        string? shapeFile,
        short shapeNumber)
    {
        return new RenderLinePatternSegment(
            RenderLinePatternSegmentKind.Shape,
            length,
            offset,
            rotation,
            rotationIsAbsolute,
            scale,
            null,
            0f,
            0f,
            0f,
            1f,
            0f,
            false,
            false,
            false,
            false,
            null,
            shapeFile,
            shapeNumber);
    }
}

public enum RenderLinePatternSegmentKind
{
    Draw = 0,
    Space = 1,
    Text = 2,
    Shape = 3
}
