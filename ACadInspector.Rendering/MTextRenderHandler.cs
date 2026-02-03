using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;

namespace ACadInspector.Rendering;

/// <summary>
/// Renders multi-line text entities using the configured text shaper.
/// </summary>
public sealed class MTextRenderHandler : IRenderEntityHandler
{
    private const float Epsilon = 0.0001f;

    public bool CanHandle(Entity entity) => entity is MText;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var text = (MText)entity;
        var builder = context.GetLayerBuilder(text);
        var color = context.ResolveEntityColor(text);

        var (anchor, rotation, scale) = ResolveTransform(transform, text.InsertPoint, (float)text.Rotation);
        var annotationScale = RenderTextUtils.ResolveAnnotationScale(text.IsAnnotative, context.Settings);
        scale *= annotationScale;
        var widthFactor = ResolveWidthFactor(text.Style);
        var obliqueAngle = ResolveObliqueAngle(text.Style);
        var (isBold, isItalic) = ResolveFontFlags(text.Style);
        var (mirrorX, mirrorY) = ResolveMirrorFlags(text.Style);
        if (!context.Settings.MirrorText)
        {
            mirrorX = false;
            mirrorY = false;
        }
        var fontFamily = ResolveFontFamily(text.Style);

        var parsed = ParseFormattedText(text.Value);
        if (parsed.Lines.Count == 0)
        {
            return;
        }

        var lineLayouts = BuildLineLayouts(parsed.Lines, text, context, scale, widthFactor, obliqueAngle, isBold, isItalic, fontFamily, color);
        if (lineLayouts.Count == 0)
        {
            return;
        }

        var blockWidth = 0f;
        var blockHeight = 0f;
        foreach (var line in lineLayouts)
        {
            blockWidth = MathF.Max(blockWidth, line.Width);
            blockHeight += line.Height;
        }

        var baseOffset = ResolveAttachmentOffset(text.AttachmentPoint, blockWidth, blockHeight);
        var horizontalAdjustMode = ResolveHorizontalAdjustment(text.AttachmentPoint);

        if (context.Settings.QuickTextMode)
        {
            var lineCap = context.ResolveLineCap(text);
            var lineJoin = context.ResolveLineJoin(text);
            var thickness = context.ResolveLineWeight(text);
            var quad = RenderTextUtils.BuildTextQuad(
                anchor,
                baseOffset,
                blockWidth,
                blockHeight,
                widthFactor: 1f,
                rotation,
                obliqueAngle,
                mirrorX,
                mirrorY);
            builder.Add(new RenderPolyline(quad, isClosed: true, color, thickness, lineCap, lineJoin));
            return;
        }

        if (TryResolveBackground(text, context.Settings, color, baseOffset, blockWidth, blockHeight, out var background))
        {
            var lineCap = context.ResolveLineCap(text);
            var lineJoin = context.ResolveLineJoin(text);
            var thickness = context.ResolveLineWeight(text);
            var quad = RenderTextUtils.BuildTextQuad(
                anchor,
                background.Offset,
                background.Width,
                background.Height,
                widthFactor: 1f,
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
                builder.Add(new RenderPolyline(quad, isClosed: true, background.FrameColor, thickness, lineCap, lineJoin));
            }
        }

        var y = 0f;
        foreach (var line in lineLayouts)
        {
            var lineAdjust = horizontalAdjustMode switch
            {
                HorizontalAdjustment.Center => (blockWidth - line.Width) * 0.5f,
                HorizontalAdjustment.Right => blockWidth - line.Width,
                _ => 0f
            };

            var x = 0f;
            foreach (var run in line.Runs)
            {
                if (string.IsNullOrWhiteSpace(run.Layout.Text))
                {
                    x += run.Advance;
                    continue;
                }

                var offset = new Vector2(baseOffset.X + lineAdjust + x, baseOffset.Y - y);
                builder.Add(new RenderText(
                    run.Layout.Text,
                    anchor,
                    offset,
                    run.Layout.Width * scale,
                    run.Layout.Height * scale,
                    run.FontSize * scale,
                    run.WidthFactor,
                    rotation,
                    run.ObliqueAngle,
                    run.IsBold,
                    run.IsItalic,
                    mirrorX,
                    mirrorY,
                    run.Color,
                    run.FontFamily));

                x += run.Advance;
            }

            y += line.Height;
        }
    }

    private static Vector2 ResolveAttachmentOffset(AttachmentPointType attachment, float width, float height)
    {
        var horizontal = attachment switch
        {
            AttachmentPointType.TopCenter => -width * 0.5f,
            AttachmentPointType.MiddleCenter => -width * 0.5f,
            AttachmentPointType.BottomCenter => -width * 0.5f,
            AttachmentPointType.TopRight => -width,
            AttachmentPointType.MiddleRight => -width,
            AttachmentPointType.BottomRight => -width,
            _ => 0f
        };

        var vertical = attachment switch
        {
            AttachmentPointType.TopLeft => 0f,
            AttachmentPointType.TopCenter => 0f,
            AttachmentPointType.TopRight => 0f,
            AttachmentPointType.MiddleLeft => -height * 0.5f,
            AttachmentPointType.MiddleCenter => -height * 0.5f,
            AttachmentPointType.MiddleRight => -height * 0.5f,
            _ => -height
        };

        return new Vector2(horizontal, vertical);
    }

    private static HorizontalAdjustment ResolveHorizontalAdjustment(AttachmentPointType attachment)
    {
        return attachment switch
        {
            AttachmentPointType.TopCenter => HorizontalAdjustment.Center,
            AttachmentPointType.MiddleCenter => HorizontalAdjustment.Center,
            AttachmentPointType.BottomCenter => HorizontalAdjustment.Center,
            AttachmentPointType.TopRight => HorizontalAdjustment.Right,
            AttachmentPointType.MiddleRight => HorizontalAdjustment.Right,
            AttachmentPointType.BottomRight => HorizontalAdjustment.Right,
            _ => HorizontalAdjustment.Left
        };
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

    private static float ResolveWidthFactor(TextStyle style)
    {
        var width = style?.Width ?? 1.0;
        var factor = (float)width;
        return factor <= 0f ? 1f : factor;
    }

    private static float ResolveObliqueAngle(TextStyle style)
    {
        if (style is null)
        {
            return 0f;
        }

        return (float)style.ObliqueAngle;
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

    private static (bool MirrorX, bool MirrorY) ResolveMirrorFlags(TextStyle style)
    {
        if (style is null)
        {
            return (false, false);
        }

        var mirrorX = style.MirrorFlag.HasFlag(TextMirrorFlag.Backward);
        var mirrorY = style.MirrorFlag.HasFlag(TextMirrorFlag.UpsideDown);
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
        MText text,
        CadRenderSceneSettings settings,
        RenderColor textColor,
        Vector2 baseOffset,
        float blockWidth,
        float blockHeight,
        out TextBackground background)
    {
        background = default;
        var flags = text.BackgroundFillFlags;
        var drawFill = flags.HasFlag(BackgroundFillFlags.UseBackgroundFillColor)
            || flags.HasFlag(BackgroundFillFlags.UseDrawingWindowColor);
        var drawFrame = flags.HasFlag(BackgroundFillFlags.TextFrame);
        if (!drawFill && !drawFrame)
        {
            return false;
        }

        var fillColor = textColor;
        if (flags.HasFlag(BackgroundFillFlags.UseBackgroundFillColor))
        {
            fillColor = ToRenderColor(text.BackgroundColor);
        }
        else if (flags.HasFlag(BackgroundFillFlags.UseDrawingWindowColor))
        {
            fillColor = settings.Background;
        }

        var alpha = ResolveTransparencyAlpha(text.BackgroundTransparency);
        if (alpha < 255)
        {
            fillColor = new RenderColor(fillColor.R, fillColor.G, fillColor.B, alpha);
        }

        var scale = Math.Max((float)text.BackgroundScale, 1f);
        var scaledWidth = blockWidth * scale;
        var scaledHeight = blockHeight * scale;
        var padX = (scaledWidth - blockWidth) * 0.5f;
        var padY = (scaledHeight - blockHeight) * 0.5f;
        var offset = new Vector2(baseOffset.X - padX, baseOffset.Y - padY);

        background = new TextBackground(
            offset,
            scaledWidth,
            scaledHeight,
            fillColor,
            textColor,
            drawFill,
            drawFrame);
        return true;
    }

    private static RenderColor ToRenderColor(ACadSharp.Color color)
    {
        return new RenderColor(color.R, color.G, color.B, 255);
    }

    private static byte ResolveTransparencyAlpha(Transparency transparency)
    {
        if (transparency.IsByLayer || transparency.IsByBlock)
        {
            return 255;
        }

        var value = Math.Clamp(transparency.Value, (short)0, (short)90);
        var alpha = (int)(255 * (100 - value) / 100.0);
        return (byte)Math.Clamp(alpha, 0, 255);
    }

    private static MTextLayout ParseFormattedText(string value)
    {
        var lines = new List<MTextLine> { new() };
        var buffer = new StringBuilder();
        var state = MTextFormatState.Default;
        var stack = new Stack<MTextFormatState>();

        void Flush()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            lines[^1].Runs.Add(new MTextRun(buffer.ToString(), state));
            buffer.Clear();
        }

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (current == '\\' && i + 1 < value.Length)
            {
                var code = value[i + 1];
                switch (code)
                {
                    case '\\':
                    case '{':
                    case '}':
                        buffer.Append(code);
                        i++;
                        continue;
                    case 'P':
                    case 'n':
                    case 'N':
                        Flush();
                        lines.Add(new MTextLine());
                        i++;
                        continue;
                    case '~':
                        buffer.Append(' ');
                        i++;
                        continue;
                    case 'S':
                        Flush();
                        var stacked = ReadDelimitedValue(value, i + 2, out var stackEnd);
                        if (!string.IsNullOrEmpty(stacked))
                        {
                            buffer.Append(ParseStackedText(stacked));
                        }
                        i = stackEnd - 1;
                        continue;
                    case 'C':
                    case 'c':
                        Flush();
                        var colorValue = ReadDelimitedValue(value, i + 2, out var colorEnd);
                        state = state.WithColor(ParseColor(code, colorValue));
                        i = colorEnd - 1;
                        continue;
                    case 'F':
                    case 'f':
                        Flush();
                        var fontValue = ReadDelimitedValue(value, i + 2, out var fontEnd);
                        state = state.WithFont(ParseFont(fontValue));
                        i = fontEnd - 1;
                        continue;
                    case 'H':
                    case 'h':
                        Flush();
                        var heightValue = ReadDelimitedValue(value, i + 2, out var heightEnd);
                        state = state.WithHeight(ParseHeight(heightValue));
                        i = heightEnd - 1;
                        continue;
                    case 'W':
                    case 'w':
                        Flush();
                        var widthValue = ReadDelimitedValue(value, i + 2, out var widthEnd);
                        state = state.WithWidthFactor(ParseFloat(widthValue));
                        i = widthEnd - 1;
                        continue;
                    case 'Q':
                    case 'q':
                        Flush();
                        var obliqueValue = ReadDelimitedValue(value, i + 2, out var obliqueEnd);
                        state = state.WithOblique(ParseOblique(obliqueValue));
                        i = obliqueEnd - 1;
                        continue;
                    case 'A':
                    case 'p':
                        ReadDelimitedValue(value, i + 2, out var ignoreEnd);
                        i = ignoreEnd - 1;
                        continue;
                    default:
                        i++;
                        continue;
                }
            }

            if (current == '{')
            {
                Flush();
                stack.Push(state);
                continue;
            }

            if (current == '}')
            {
                Flush();
                if (stack.Count > 0)
                {
                    state = stack.Pop();
                }
                continue;
            }

            buffer.Append(current);
        }

        Flush();
        return new MTextLayout(lines);
    }

    private static string ReadDelimitedValue(string text, int start, out int end)
    {
        var index = text.IndexOf(';', start);
        if (index < 0)
        {
            end = text.Length;
            return text.Substring(start);
        }

        end = index + 1;
        return text.Substring(start, index - start);
    }

    private static RenderColor? ParseColor(char code, string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
        {
            return null;
        }

        if (raw <= 0 || raw == 256)
        {
            return null;
        }

        ACadSharp.Color color;
        if (code == 'C')
        {
            color = new ACadSharp.Color((short)Math.Clamp(raw, 1, 255));
        }
        else
        {
            var trueColor = (uint)Math.Clamp(raw, 1, 0xFFFFFF);
            color = ACadSharp.Color.FromTrueColor(trueColor);
        }

        return new RenderColor(color.R, color.G, color.B, 255);
    }

    private static MTextFont ParseFont(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        var parts = value.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var fontName = parts.Length > 0 ? parts[0] : null;
        if (!string.IsNullOrEmpty(fontName) && fontName[0] == '\\')
        {
            fontName = fontName.Substring(1);
        }
        bool? isBold = null;
        bool? isItalic = null;

        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length < 2)
            {
                continue;
            }

            var flag = char.ToLowerInvariant(part[0]);
            if (flag == 'b')
            {
                isBold = part[1] == '1';
            }
            else if (flag == 'i')
            {
                isItalic = part[1] == '1';
            }
        }

        return new MTextFont(fontName, isBold, isItalic);
    }

    private static MTextHeight ParseHeight(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        var isRelative = value.EndsWith("x", StringComparison.OrdinalIgnoreCase);
        var numeric = isRelative ? value.Substring(0, value.Length - 1) : value;
        var parsed = ParseFloat(numeric);
        if (parsed <= 0f)
        {
            return default;
        }

        return isRelative ? new MTextHeight(scale: parsed, absolute: null) : new MTextHeight(scale: null, absolute: parsed);
    }

    private static float? ParseOblique(string value)
    {
        var parsed = ParseFloat(value);
        if (parsed is null)
        {
            return null;
        }

        return (float)(parsed.Value * MathHelper.DegToRadFactor);
    }

    private static float? ParseFloat(string value)
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string ParseStackedText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace('#', '/').Replace('^', '/');
    }

    private static IReadOnlyList<MTextLineLayout> BuildLineLayouts(
        IReadOnlyList<MTextLine> lines,
        MText text,
        RenderBuildContext context,
        float scale,
        float baseWidthFactor,
        float baseObliqueAngle,
        bool baseBold,
        bool baseItalic,
        string? baseFontFamily,
        RenderColor baseColor)
    {
        var layouts = new List<MTextLineLayout>(lines.Count);
        var baseHeight = (float)text.Height;
        var lineSpacing = Math.Clamp((float)text.LineSpacing, 0.25f, 4f);
        var baseLineHeight = baseHeight * lineSpacing * scale;

        foreach (var line in lines)
        {
            if (line.Runs.Count == 0)
            {
                layouts.Add(new MTextLineLayout(new List<MTextRunLayout>(), 0f, baseLineHeight));
                continue;
            }

            var runLayouts = new List<MTextRunLayout>(line.Runs.Count);
            var lineWidth = 0f;
            var maxRunHeight = 0f;

            foreach (var run in line.Runs)
            {
                var resolved = ResolveRunStyle(text, run.State, baseWidthFactor, baseObliqueAngle, baseBold, baseItalic, baseFontFamily, baseColor);
                var runHeight = resolved.Height;
                var style = CreateRunStyle(text.Style, resolved);
                var layout = MeasureRunLayout(run.Text, runHeight, style, context);
                if (string.IsNullOrEmpty(layout.Text))
                {
                    continue;
                }

                var runWidth = layout.Width * scale * resolved.WidthFactor;
                var runHeightWorld = layout.Height * scale;
                lineWidth += runWidth;
                maxRunHeight = MathF.Max(maxRunHeight, runHeightWorld);

                runLayouts.Add(new MTextRunLayout(
                    layout,
                    resolved.Color,
                    resolved.FontFamily,
                    resolved.WidthFactor,
                    resolved.ObliqueAngle,
                    resolved.IsBold,
                    resolved.IsItalic,
                    runHeight,
                    runWidth));
            }

            var lineHeight = text.LineSpacingStyle == LineSpacingStyleType.Exact
                ? baseLineHeight
                : MathF.Max(baseLineHeight, maxRunHeight);

            layouts.Add(new MTextLineLayout(runLayouts, lineWidth, lineHeight));
        }

        return layouts;
    }

    private static MTextResolvedStyle ResolveRunStyle(
        MText text,
        MTextFormatState state,
        float baseWidthFactor,
        float baseObliqueAngle,
        bool baseBold,
        bool baseItalic,
        string? baseFontFamily,
        RenderColor baseColor)
    {
        var height = (float)text.Height;
        if (state.Height.Absolute.HasValue)
        {
            height = state.Height.Absolute.Value;
        }
        else if (state.Height.Scale.HasValue)
        {
            height *= state.Height.Scale.Value;
        }

        if (height <= Epsilon)
        {
            height = (float)text.Height;
        }

        var widthFactor = baseWidthFactor;
        if (state.WidthFactor.HasValue && state.WidthFactor.Value > 0f)
        {
            widthFactor *= state.WidthFactor.Value;
        }

        var oblique = state.ObliqueAngle ?? baseObliqueAngle;
        var isBold = state.IsBold ?? baseBold;
        var isItalic = state.IsItalic ?? baseItalic;
        var fontFamily = !string.IsNullOrWhiteSpace(state.FontFamily) ? state.FontFamily : baseFontFamily;
        var color = state.Color ?? baseColor;
        if (state.Color.HasValue)
        {
            color = new RenderColor(state.Color.Value.R, state.Color.Value.G, state.Color.Value.B, baseColor.A);
        }

        return new MTextResolvedStyle(height, widthFactor, oblique, isBold, isItalic, fontFamily, color);
    }

    private static TextEntity CreateMeasureEntity(string text, float height, TextStyle style)
    {
        return new TextEntity
        {
            Value = text,
            Height = Math.Max(height, MathHelper.Epsilon),
            Style = style
        };
    }

    private static RenderTextLayout MeasureRunLayout(
        string text,
        float height,
        TextStyle style,
        RenderBuildContext context)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new RenderTextLayout(string.Empty, 0f, 0f);
        }

        const int maxLength = 256;
        if (text.Length <= maxLength)
        {
            var entity = CreateMeasureEntity(text, height, style);
            return context.TextShaper.Shape(entity, context.Settings);
        }

        var width = 0f;
        var maxHeight = 0f;
        foreach (var chunk in SplitText(text, maxLength))
        {
            var entity = CreateMeasureEntity(chunk, height, style);
            var layout = context.TextShaper.Shape(entity, context.Settings);
            width += layout.Width;
            maxHeight = MathF.Max(maxHeight, layout.Height);
        }

        return new RenderTextLayout(text, width, maxHeight);
    }

    private static IEnumerable<string> SplitText(string text, int maxLength)
    {
        for (var i = 0; i < text.Length; i += maxLength)
        {
            var length = Math.Min(maxLength, text.Length - i);
            yield return text.Substring(i, length);
        }
    }

    private static TextStyle CreateRunStyle(TextStyle baseStyle, MTextResolvedStyle style)
    {
        var runStyle = new TextStyle(baseStyle?.Name ?? TextStyle.DefaultName)
        {
            Filename = baseStyle?.Filename ?? string.Empty,
            Width = baseStyle?.Width ?? 1.0,
            Height = baseStyle?.Height ?? 0.0,
            ObliqueAngle = baseStyle?.ObliqueAngle ?? 0.0,
            MirrorFlag = baseStyle?.MirrorFlag ?? TextMirrorFlag.None,
            TrueType = baseStyle?.TrueType ?? FontFlags.Regular
        };

        if (!string.IsNullOrWhiteSpace(style.FontFamily))
        {
            runStyle.Filename = style.FontFamily;
        }

        runStyle.ObliqueAngle = style.ObliqueAngle;
        runStyle.TrueType = FontFlags.Regular;
        if (style.IsBold)
        {
            runStyle.TrueType |= FontFlags.Bold;
        }

        if (style.IsItalic)
        {
            runStyle.TrueType |= FontFlags.Italic;
        }

        return runStyle;
    }

    private enum HorizontalAdjustment
    {
        Left = 0,
        Center = 1,
        Right = 2
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

    private readonly struct MTextLayout
    {
        public IReadOnlyList<MTextLine> Lines { get; }

        public MTextLayout(IReadOnlyList<MTextLine> lines)
        {
            Lines = lines;
        }
    }

    private sealed class MTextLine
    {
        public List<MTextRun> Runs { get; } = new();
    }

    private readonly struct MTextRun
    {
        public string Text { get; }
        public MTextFormatState State { get; }

        public MTextRun(string text, MTextFormatState state)
        {
            Text = text;
            State = state;
        }
    }

    private readonly struct MTextLineLayout
    {
        public IReadOnlyList<MTextRunLayout> Runs { get; }
        public float Width { get; }
        public float Height { get; }

        public MTextLineLayout(IReadOnlyList<MTextRunLayout> runs, float width, float height)
        {
            Runs = runs;
            Width = width;
            Height = height;
        }
    }

    private readonly struct MTextRunLayout
    {
        public RenderTextLayout Layout { get; }
        public RenderColor Color { get; }
        public string? FontFamily { get; }
        public float WidthFactor { get; }
        public float ObliqueAngle { get; }
        public bool IsBold { get; }
        public bool IsItalic { get; }
        public float FontSize { get; }
        public float Advance { get; }

        public MTextRunLayout(
            RenderTextLayout layout,
            RenderColor color,
            string? fontFamily,
            float widthFactor,
            float obliqueAngle,
            bool isBold,
            bool isItalic,
            float fontSize,
            float advance)
        {
            Layout = layout;
            Color = color;
            FontFamily = fontFamily;
            WidthFactor = widthFactor;
            ObliqueAngle = obliqueAngle;
            IsBold = isBold;
            IsItalic = isItalic;
            FontSize = fontSize;
            Advance = advance;
        }
    }

    private readonly struct MTextResolvedStyle
    {
        public float Height { get; }
        public float WidthFactor { get; }
        public float ObliqueAngle { get; }
        public bool IsBold { get; }
        public bool IsItalic { get; }
        public string? FontFamily { get; }
        public RenderColor Color { get; }

        public MTextResolvedStyle(
            float height,
            float widthFactor,
            float obliqueAngle,
            bool isBold,
            bool isItalic,
            string? fontFamily,
            RenderColor color)
        {
            Height = height;
            WidthFactor = widthFactor;
            ObliqueAngle = obliqueAngle;
            IsBold = isBold;
            IsItalic = isItalic;
            FontFamily = fontFamily;
            Color = color;
        }
    }

    private readonly struct MTextFont
    {
        public string? Name { get; }
        public bool? IsBold { get; }
        public bool? IsItalic { get; }

        public MTextFont(string? name, bool? isBold, bool? isItalic)
        {
            Name = name;
            IsBold = isBold;
            IsItalic = isItalic;
        }
    }

    private readonly struct MTextHeight
    {
        public float? Scale { get; }
        public float? Absolute { get; }

        public MTextHeight(float? scale, float? absolute)
        {
            Scale = scale;
            Absolute = absolute;
        }
    }

    private readonly struct MTextFormatState
    {
        public static MTextFormatState Default => new(null, null, null, null, null, null, null);

        public RenderColor? Color { get; }
        public string? FontFamily { get; }
        public bool? IsBold { get; }
        public bool? IsItalic { get; }
        public MTextHeight Height { get; }
        public float? WidthFactor { get; }
        public float? ObliqueAngle { get; }

        public MTextFormatState(
            RenderColor? color,
            string? fontFamily,
            bool? isBold,
            bool? isItalic,
            MTextHeight? height,
            float? widthFactor,
            float? obliqueAngle)
        {
            Color = color;
            FontFamily = fontFamily;
            IsBold = isBold;
            IsItalic = isItalic;
            Height = height ?? default;
            WidthFactor = widthFactor;
            ObliqueAngle = obliqueAngle;
        }

        public MTextFormatState WithColor(RenderColor? color)
        {
            return new MTextFormatState(color, FontFamily, IsBold, IsItalic, Height, WidthFactor, ObliqueAngle);
        }

        public MTextFormatState WithFont(MTextFont font)
        {
            var name = !string.IsNullOrWhiteSpace(font.Name) ? font.Name : FontFamily;
            var bold = font.IsBold ?? IsBold;
            var italic = font.IsItalic ?? IsItalic;
            return new MTextFormatState(Color, name, bold, italic, Height, WidthFactor, ObliqueAngle);
        }

        public MTextFormatState WithHeight(MTextHeight height)
        {
            return new MTextFormatState(Color, FontFamily, IsBold, IsItalic, height, WidthFactor, ObliqueAngle);
        }

        public MTextFormatState WithWidthFactor(float? widthFactor)
        {
            return new MTextFormatState(Color, FontFamily, IsBold, IsItalic, Height, widthFactor, ObliqueAngle);
        }

        public MTextFormatState WithOblique(float? obliqueAngle)
        {
            return new MTextFormatState(Color, FontFamily, IsBold, IsItalic, Height, WidthFactor, obliqueAngle);
        }
    }
}
