using System;
using System.Collections.Generic;
using System.Numerics;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using ACadSharp.XData;
using CSMath;

namespace ACadInspector.Rendering;

/// <summary>
/// Renders single-line text entities using the configured text shaper.
/// </summary>
public sealed class TextEntityRenderHandler : IRenderEntityHandler
{
    private const float Epsilon = 0.0001f;
    private const string TextBackgroundAppId = "ACAD_INSPECTOR_TEXT_BG";

    public bool CanHandle(Entity entity) => entity is TextEntity;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var text = (TextEntity)entity;
        var layout = context.TextShaper.Shape(text, context.Settings);
        if (string.IsNullOrWhiteSpace(layout.Text))
        {
            return;
        }

        var builder = context.GetLayerBuilder(text);
        var color = context.ResolveEntityColor(text);
        var effectiveHeight = RenderTextUtils.ResolveTextHeight(text);

        var isAligned = text.HorizontalAlignment == TextHorizontalAlignment.Aligned;
        var isFit = text.HorizontalAlignment == TextHorizontalAlignment.Fit;
        var isAlignedOrFit = isAligned || isFit;
        var (anchor, rotation, scale, alignLength) = isAlignedOrFit
            ? ResolveAlignedTransform(transform, text)
            : ResolveDefaultTransform(transform, text);
        var annotationScale = RenderTextUtils.ResolveAnnotationScale(IsAnnotative(text), context.Settings);
        scale *= annotationScale;
        var widthFactor = ResolveWidthFactor(text.Style, text.WidthFactor);
        var obliqueAngle = ResolveObliqueAngle(text.Style, text.ObliqueAngle);
        var (isBold, isItalic) = ResolveFontFlags(text.Style);
        var layoutWidth = layout.Width * scale;
        var layoutHeight = layout.Height * scale;
        var fontSize = effectiveHeight * scale;
        var offset = isAlignedOrFit
            ? new Vector2(0f, -layoutHeight)
            : ResolveAlignmentOffset(text.HorizontalAlignment, text.VerticalAlignment, layoutWidth, layoutHeight);
        var fontFamily = ResolveFontFamily(text.Style);
        var (mirrorX, mirrorY) = ResolveMirrorFlags(text);
        if (!context.Settings.MirrorText)
        {
            mirrorX = false;
            mirrorY = false;
        }

        if (isAlignedOrFit)
        {
            var baseWidth = layoutWidth * widthFactor;
            if (alignLength > Epsilon && baseWidth > Epsilon)
            {
                var fitScale = alignLength / baseWidth;
                if (isAligned)
                {
                    scale *= fitScale;
                    layoutWidth *= fitScale;
                    layoutHeight *= fitScale;
                    fontSize *= fitScale;
                    offset = new Vector2(0f, -layoutHeight);
                }
                else
                {
                    widthFactor *= fitScale;
                }
            }
        }

        if (context.Settings.QuickTextMode)
        {
            var lineCap = context.ResolveLineCap(text);
            var lineJoin = context.ResolveLineJoin(text);
            var thickness = context.ResolveLineWeight(text);
            var quad = RenderTextUtils.BuildTextQuad(
                anchor,
                offset,
                layoutWidth,
                layoutHeight,
                widthFactor,
                rotation,
                obliqueAngle,
                mirrorX,
                mirrorY);
            builder.Add(new RenderPolyline(quad, isClosed: true, color, thickness, lineCap, lineJoin));
            return;
        }

        if (TryResolveBackground(text, context.Settings, color, offset, layoutWidth, layoutHeight, out var background))
        {
            var quad = RenderTextUtils.BuildTextQuad(
                anchor,
                background.Offset,
                background.Width,
                background.Height,
                widthFactor,
                rotation,
                obliqueAngle,
                mirrorX,
                mirrorY);

            if (background.DrawFill)
            {
                builder.Add(new RenderFill(quad, background.FillColor));
            }

            if (background.DrawFrame)
            {
                var lineCap = context.ResolveLineCap(text);
                var lineJoin = context.ResolveLineJoin(text);
                var thickness = context.ResolveLineWeight(text);
                builder.Add(new RenderPolyline(quad, isClosed: true, background.FrameColor, thickness, lineCap, lineJoin));
            }
        }

        builder.Add(new RenderText(
            layout.Text,
            anchor,
            offset,
            layoutWidth,
            layoutHeight,
            fontSize,
            widthFactor,
            rotation,
            obliqueAngle,
            isBold,
            isItalic,
            mirrorX,
            mirrorY,
            color,
            fontFamily));
    }

    private static XYZ ResolveAnchorPoint(TextEntity text)
    {
        if (text.HorizontalAlignment != TextHorizontalAlignment.Left ||
            text.VerticalAlignment != TextVerticalAlignmentType.Baseline)
        {
            return text.AlignmentPoint;
        }

        return text.InsertPoint;
    }

    private static Vector2 ResolveAlignmentOffset(
        TextHorizontalAlignment horizontal,
        TextVerticalAlignmentType vertical,
        float width,
        float height)
    {
        var offsetX = horizontal switch
        {
            TextHorizontalAlignment.Center => -width * 0.5f,
            TextHorizontalAlignment.Right => -width,
            TextHorizontalAlignment.Middle => -width * 0.5f,
            _ => 0f
        };

        var offsetY = vertical switch
        {
            TextVerticalAlignmentType.Top => 0f,
            TextVerticalAlignmentType.Middle => -height * 0.5f,
            TextVerticalAlignmentType.Bottom => -height,
            _ => -height
        };

        return new Vector2(offsetX, offsetY);
    }

    private static (Vector2 Anchor, float Rotation, float Scale, float AlignLength) ResolveAlignedTransform(Transform transform, TextEntity text)
    {
        var startPoint = text.InsertPoint;
        var endPoint = text.AlignmentPoint;
        var localDelta = new Vector2((float)(endPoint.X - startPoint.X), (float)(endPoint.Y - startPoint.Y));
        var localLength = localDelta.Length();
        if (localLength <= Epsilon)
        {
            var fallback = ResolveDefaultTransform(transform, text);
            return (fallback.Anchor, fallback.Rotation, fallback.Scale, 0f);
        }

        var worldStart = RenderTransformUtils.Apply(transform, startPoint);
        var worldEnd = RenderTransformUtils.Apply(transform, endPoint);
        var worldDelta = worldEnd - worldStart;
        var worldLength = worldDelta.Length();
        if (worldLength <= Epsilon)
        {
            var fallback = ResolveDefaultTransform(transform, text);
            return (fallback.Anchor, fallback.Rotation, fallback.Scale, 0f);
        }

        var rotation = MathF.Atan2(worldDelta.Y, worldDelta.X);
        var scale = worldLength / localLength;
        return (worldStart, rotation, scale, worldLength);
    }

    private static (Vector2 Anchor, float Rotation, float Scale, float AlignLength) ResolveDefaultTransform(Transform transform, TextEntity text)
    {
        var anchorPoint = ResolveAnchorPoint(text);
        var rotation = (float)text.Rotation;
        var (anchor, worldRotation, scale) = ResolveTransform(transform, anchorPoint, rotation);
        return (anchor, worldRotation, scale, 0f);
    }

    private static (Vector2 Anchor, float Rotation, float Scale) ResolveTransform(Transform transform, XYZ anchorPoint, float rotation)
    {
        if (RenderTransformUtils.IsIdentity(transform))
        {
            return (RenderTransformUtils.ToVector2(anchorPoint), rotation, 1f);
        }

        var worldAnchor = transform.ApplyTransform(anchorPoint);
        var direction = new XYZ(Math.Cos(rotation), Math.Sin(rotation), 0);
        var worldDir = transform.ApplyTransform(anchorPoint + direction);
        var delta = new Vector2((float)(worldDir.X - worldAnchor.X), (float)(worldDir.Y - worldAnchor.Y));
        var scale = delta.Length();
        if (scale <= 0f)
        {
            scale = 1f;
        }

        var worldRotation = MathF.Atan2(delta.Y, delta.X);
        return (new Vector2((float)worldAnchor.X, (float)worldAnchor.Y), worldRotation, scale);
    }

    private static float ResolveWidthFactor(TextStyle style, double entityWidthFactor)
    {
        var width = style?.Width ?? 1.0;
        var factor = (float)(entityWidthFactor * width);
        return factor <= 0f ? 1f : factor;
    }

    private static float ResolveObliqueAngle(TextStyle style, double entityObliqueAngle)
    {
        var oblique = entityObliqueAngle;
        if (Math.Abs(oblique) < 0.0001 && style is not null)
        {
            oblique = style.ObliqueAngle;
        }

        return (float)oblique;
    }

    private static (bool IsBold, bool IsItalic) ResolveFontFlags(TextStyle style)
    {
        if (style is null)
        {
            return (false, false);
        }

        var flags = style.TrueType;
        return (flags.HasFlag(FontFlags.Bold), flags.HasFlag(FontFlags.Italic));
    }

    private static (bool MirrorX, bool MirrorY) ResolveMirrorFlags(TextEntity text)
    {
        var mirror = text.Mirror;
        if (text.Style is not null)
        {
            mirror |= text.Style.MirrorFlag;
        }

        var mirrorX = mirror.HasFlag(TextMirrorFlag.Backward);
        var mirrorY = mirror.HasFlag(TextMirrorFlag.UpsideDown);
        return (mirrorX, mirrorY);
    }

    private static string? ResolveFontFamily(TextStyle style)
    {
        if (style is null || string.IsNullOrWhiteSpace(style.Filename))
        {
            return null;
        }

        return System.IO.Path.GetFileNameWithoutExtension(style.Filename);
    }

    private static bool TryResolveBackground(
        TextEntity text,
        CadRenderSceneSettings settings,
        RenderColor textColor,
        Vector2 offset,
        float width,
        float height,
        out TextBackground background)
    {
        background = default;
        if (!text.ExtendedData.TryGet(TextBackgroundAppId, out var data))
        {
            return false;
        }

        var drawFill = false;
        var drawFrame = false;
        var scale = 1f;
        var fillColor = settings.Background;

        foreach (var record in data.Records)
        {
            switch (record)
            {
                case ExtendedDataInteger16 s16:
                    drawFill |= (s16.Value & 1) != 0;
                    drawFrame |= (s16.Value & 2) != 0;
                    break;
                case ExtendedDataInteger32 s32:
                    fillColor = ParseColor(s32.Value, textColor.A);
                    break;
                case ExtendedDataReal real:
                    scale = (float)real.Value;
                    break;
            }
        }

        if (!drawFill && !drawFrame)
        {
            return false;
        }

        scale = Math.Max(scale, 1f);
        var scaledWidth = width * scale;
        var scaledHeight = height * scale;
        var padX = (scaledWidth - width) * 0.5f;
        var padY = (scaledHeight - height) * 0.5f;
        var backgroundOffset = new Vector2(offset.X - padX, offset.Y - padY);

        background = new TextBackground(
            backgroundOffset,
            scaledWidth,
            scaledHeight,
            fillColor,
            textColor,
            drawFill,
            drawFrame);
        return true;
    }

    private static RenderColor ParseColor(int value, byte alpha)
    {
        ACadSharp.Color color;
        if (value <= 255 && value > 0)
        {
            color = new ACadSharp.Color((short)value);
        }
        else
        {
            var trueColor = (uint)Math.Clamp(value, 1, 0xFFFFFF);
            color = ACadSharp.Color.FromTrueColor(trueColor);
        }

        return new RenderColor(color.R, color.G, color.B, alpha);
    }

    private static bool IsAnnotative(TextEntity text)
    {
        if (text is AttributeBase attribute && attribute.MText is { IsAnnotative: true })
        {
            return true;
        }

        return false;
    }

    private readonly struct TextBackground
    {
        public Vector2 Offset { get; }
        public float Width { get; }
        public float Height { get; }
        public RenderColor FillColor { get; }
        public RenderColor FrameColor { get; }
        public bool DrawFill { get; }
        public bool DrawFrame { get; }

        public TextBackground(
            Vector2 offset,
            float width,
            float height,
            RenderColor fillColor,
            RenderColor frameColor,
            bool drawFill,
            bool drawFrame)
        {
            Offset = offset;
            Width = width;
            Height = height;
            FillColor = fillColor;
            FrameColor = frameColor;
            DrawFill = drawFill;
            DrawFrame = drawFrame;
        }
    }
}
