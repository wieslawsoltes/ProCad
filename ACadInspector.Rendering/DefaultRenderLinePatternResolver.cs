using System;
using System.Collections.Generic;
using System.Numerics;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Header;
using ACadSharp.Tables;

namespace ACadInspector.Rendering;

/// <summary>
/// Resolves simple dash-gap patterns from AutoCAD line types.
/// </summary>
public sealed class DefaultRenderLinePatternResolver : IRenderLinePatternResolver
{
    private const float MinSegmentLength = 0.0001f;
    private readonly IRenderTextShaper _textShaper;

    public DefaultRenderLinePatternResolver()
        : this(new DefaultRenderTextShaper())
    {
    }

    public DefaultRenderLinePatternResolver(IRenderTextShaper textShaper)
    {
        _textShaper = textShaper ?? new DefaultRenderTextShaper();
    }

    public RenderLinePattern ResolveLinePattern(Entity entity, CadDocument document, CadRenderSceneSettings settings)
    {
        if (!settings.EnableDashPatternRendering)
        {
            return RenderLinePattern.Continuous;
        }

        var lineType = entity.GetActiveLineType();
        if (lineType is null || !lineType.IsComplex)
        {
            return RenderLinePattern.Continuous;
        }

        var scale = (float)(document.Header?.LineTypeScale ?? 1.0);
        scale *= ResolveSpaceScale(document, settings);
        scale *= (float)entity.LineTypeScale;
        if (scale <= 0)
        {
            scale = 1f;
        }

        var dotLength = MathF.Max(settings.MinLineWeightMm, settings.LineTypeDotLengthMm) / settings.MillimetersPerUnit;
        dotLength *= scale;

        var segments = new List<RenderLinePatternSegment>();
        foreach (var segment in lineType.Segments)
        {
            float length;

            if (segment.IsPoint)
            {
                length = dotLength;
            }
            else
            {
                length = (float)Math.Abs(segment.Length) * scale;
            }

            if (segment.IsText)
            {
                if (length <= MinSegmentLength)
                {
                    length = MinSegmentLength;
                }
                var textSegment = BuildTextSegment(segment, length, scale, settings, document);
                segments.Add(textSegment);
                continue;
            }

            if (segment.IsShape)
            {
                if (length <= MinSegmentLength)
                {
                    length = MinSegmentLength;
                }
                var shapeSegment = BuildShapeSegment(segment, length, scale, document);
                segments.Add(shapeSegment);
                continue;
            }

            if (length <= MinSegmentLength)
            {
                continue;
            }

            var isDraw = !segment.IsSpace;
            segments.Add(new RenderLinePatternSegment(length, isDraw));
        }

        if (segments.Count == 0)
        {
            return RenderLinePattern.Continuous;
        }

        return new RenderLinePattern(segments.ToArray());
    }

    public RenderLinePattern ResolveLinePattern(
        LineType lineType,
        double entityLineTypeScale,
        CadDocument document,
        CadRenderSceneSettings settings)
    {
        if (!settings.EnableDashPatternRendering)
        {
            return RenderLinePattern.Continuous;
        }

        if (lineType is null || !lineType.IsComplex)
        {
            return RenderLinePattern.Continuous;
        }

        var scale = (float)(document.Header?.LineTypeScale ?? 1.0);
        scale *= ResolveSpaceScale(document, settings);
        scale *= (float)entityLineTypeScale;
        if (scale <= 0)
        {
            scale = 1f;
        }

        var dotLength = MathF.Max(settings.MinLineWeightMm, settings.LineTypeDotLengthMm) / settings.MillimetersPerUnit;
        dotLength *= scale;

        var segments = new List<RenderLinePatternSegment>();
        foreach (var segment in lineType.Segments)
        {
            float length;

            if (segment.IsPoint)
            {
                length = dotLength;
            }
            else
            {
                length = (float)Math.Abs(segment.Length) * scale;
            }

            if (segment.IsText)
            {
                if (length <= MinSegmentLength)
                {
                    length = MinSegmentLength;
                }
                var textSegment = BuildTextSegment(segment, length, scale, settings, document);
                segments.Add(textSegment);
                continue;
            }

            if (segment.IsShape)
            {
                if (length <= MinSegmentLength)
                {
                    length = MinSegmentLength;
                }
                var shapeSegment = BuildShapeSegment(segment, length, scale, document);
                segments.Add(shapeSegment);
                continue;
            }

            if (length <= MinSegmentLength)
            {
                continue;
            }

            var isDraw = !segment.IsSpace;
            segments.Add(new RenderLinePatternSegment(length, isDraw));
        }

        if (segments.Count == 0)
        {
            return RenderLinePattern.Continuous;
        }

        return new RenderLinePattern(segments.ToArray());
    }

    private RenderLinePatternSegment BuildTextSegment(
        LineType.Segment segment,
        float length,
        float scale,
        CadRenderSceneSettings settings,
        CadDocument document)
    {
        var style = segment.Style ?? document.TextStyles[TextStyle.DefaultName];
        var baseHeight = style?.Height > 0 ? style.Height : 1.0;
        var fontSize = (float)(baseHeight * segment.Scale * scale);
        var textValue = string.IsNullOrEmpty(segment.Text) ? string.Empty : segment.Text;
        var widthFactor = (float)(style?.Width ?? 1.0);
        if (widthFactor <= 0f)
        {
            widthFactor = 1f;
        }

        var layout = ShapeLineTypeText(textValue, fontSize, widthFactor, style, settings);
        var layoutWidth = layout.Width;
        var layoutHeight = layout.Height > 0f ? layout.Height : fontSize;

        var offset = new Vector2(
            (float)segment.Offset.X * (float)segment.Scale * scale,
            (float)segment.Offset.Y * (float)segment.Scale * scale);
        var rotation = (float)segment.Rotation;
        var rotationIsAbsolute = segment.Flags.HasFlag(LineTypeShapeFlags.RotationIsAbsolute);
        var obliqueAngle = (float)(style?.ObliqueAngle ?? 0.0);
        var flags = style?.TrueType ?? FontFlags.Regular;
        var isBold = flags.HasFlag(FontFlags.Bold);
        var isItalic = flags.HasFlag(FontFlags.Italic);
        var mirror = style?.MirrorFlag ?? TextMirrorFlag.None;
        var mirrorX = mirror.HasFlag(TextMirrorFlag.Backward);
        var mirrorY = mirror.HasFlag(TextMirrorFlag.UpsideDown);
        var fontFamily = ResolveFontFamily(style);

        var effectiveLength = length;
        if (effectiveLength <= MinSegmentLength && layoutWidth > 0f)
        {
            effectiveLength = MathF.Max(layoutWidth, MinSegmentLength);
        }

        return RenderLinePatternSegment.CreateText(
            effectiveLength,
            offset,
            rotation,
            rotationIsAbsolute,
            (float)segment.Scale,
            textValue,
            layoutWidth,
            layoutHeight,
            fontSize,
            widthFactor,
            obliqueAngle,
            isBold,
            isItalic,
            mirrorX,
            mirrorY,
            fontFamily);
    }

    private RenderTextLayout ShapeLineTypeText(
        string text,
        float fontSize,
        float widthFactor,
        TextStyle? style,
        CadRenderSceneSettings settings)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new RenderTextLayout(string.Empty, 0f, 0f);
        }

        var height = fontSize > 0f ? fontSize : 1f;
        var resolvedStyle = style ?? TextStyle.Default;
        var entity = new TextEntity
        {
            Value = text,
            Height = height,
            WidthFactor = widthFactor,
            Style = resolvedStyle
        };

        return _textShaper.Shape(entity, settings);
    }

    private static RenderLinePatternSegment BuildShapeSegment(
        LineType.Segment segment,
        float length,
        float scale,
        CadDocument document)
    {
        var style = segment.Style ?? document.TextStyles[TextStyle.DefaultName];
        var shapeFile = string.IsNullOrWhiteSpace(style?.Filename) ? null : style.Filename;
        var offset = new Vector2(
            (float)segment.Offset.X * (float)segment.Scale * scale,
            (float)segment.Offset.Y * (float)segment.Scale * scale);
        var rotation = (float)segment.Rotation;
        var rotationIsAbsolute = segment.Flags.HasFlag(LineTypeShapeFlags.RotationIsAbsolute);
        var shapeScale = (float)segment.Scale * scale;
        var effectiveLength = length <= MinSegmentLength
            ? MathF.Max(shapeScale, MinSegmentLength)
            : length;
        return RenderLinePatternSegment.CreateShape(
            effectiveLength,
            offset,
            rotation,
            rotationIsAbsolute,
            shapeScale,
            shapeFile,
            segment.ShapeNumber);
    }

    private static float ResolveSpaceScale(CadDocument document, CadRenderSceneSettings settings)
    {
        if (settings.ViewportScale <= 0f)
        {
            return 1f;
        }

        if (settings.IsPaperSpace)
        {
            var scaling = settings.PaperSpaceLineTypeScalingOverride
                ?? document.Header?.PaperSpaceLineTypeScaling
                ?? SpaceLineTypeScaling.Normal;
            return scaling == SpaceLineTypeScaling.Viewport ? settings.ViewportScale : 1f;
        }

        return settings.ModelSpaceLineTypeScaling ? settings.ViewportScale : 1f;
    }

    private static string? ResolveFontFamily(TextStyle? style)
    {
        if (style is null || string.IsNullOrWhiteSpace(style.Filename))
        {
            return null;
        }

        return System.IO.Path.GetFileNameWithoutExtension(style.Filename);
    }
}
