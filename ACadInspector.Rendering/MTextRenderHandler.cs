using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using ACadSharp.Text;
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

        var parsed = ParseFormattedText(text.Value, context.Settings);
        if (parsed.Lines.Count == 0)
        {
            return;
        }

        var lineLayouts = BuildLineLayouts(parsed.Lines, text, context, scale, widthFactor, obliqueAngle, isBold, isItalic, fontFamily, color);
        if (lineLayouts.Count == 0)
        {
            return;
        }

        var columns = BuildColumns(lineLayouts, text, scale, out var columnGutter);
        if (columns.Count == 0)
        {
            return;
        }

        var blockWidth = ComputeBlockWidth(columns, columnGutter);
        var blockHeight = ComputeBlockHeight(columns);
        var baseOffset = ResolveAttachmentOffset(text.AttachmentPoint, blockWidth, blockHeight);

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

        var columnOffset = 0f;
        var textLineCap = context.ResolveLineCap(text);
        var textLineJoin = context.ResolveLineJoin(text);
        var textLineWeight = context.ResolveLineWeight(text);
        foreach (var column in columns)
        {
            var y = 0f;
            foreach (var line in column.Lines)
            {
                var lineAdjust = line.Alignment switch
                {
                    MTextAlignment.Center => (column.Width - line.Width) * 0.5f,
                    MTextAlignment.Right => column.Width - line.Width,
                    _ => 0f
                };

                var extraSpace = line.Alignment == MTextAlignment.Justify && line.SpaceCount > 0
                    ? (column.Width - line.Width) / line.SpaceCount
                    : 0f;

                var x = 0f;
                foreach (var run in line.Runs)
                {
                    var offset = new Vector2(baseOffset.X + columnOffset + lineAdjust + x, baseOffset.Y - y);
                    var hasStack = run.StackedLayout.HasValue;

                    if (run.BackgroundColor.HasValue && run.Advance > 0f && (hasStack || !string.IsNullOrWhiteSpace(run.Layout.Text)))
                    {
                        var padding = run.BackgroundPadding * scale;
                        var backgroundOffset = new Vector2(offset.X - padding, offset.Y - padding);
                        var backgroundWidth = run.Advance + padding * 2f;
                        var stackedLayout = run.StackedLayout;
                        var contentHeight = stackedLayout.HasValue
                            ? stackedLayout.Value.Height * scale
                            : run.Layout.Height * scale;
                        var backgroundHeight = contentHeight + padding * 2f;
                        var quad = RenderTextUtils.BuildTextQuad(
                            anchor,
                            backgroundOffset,
                            backgroundWidth,
                            backgroundHeight,
                            widthFactor: 1f,
                            rotation,
                            run.ObliqueAngle,
                            mirrorX,
                            mirrorY);
                        builder.Add(new RenderFill(quad, run.BackgroundColor.Value));
                    }

                    if (hasStack)
                    {
                        AppendStackedRun(
                            builder,
                            context,
                            text,
                            run,
                            line,
                            anchor,
                            offset,
                            scale,
                            rotation,
                            mirrorX,
                            mirrorY,
                            textLineCap,
                            textLineJoin,
                            textLineWeight);
                    }
                    else if (!string.IsNullOrWhiteSpace(run.Layout.Text))
                    {
                        var runStyle = ResolveRunTextStyle(text.Style, run);
                        var runHeight = run.FontSize * scale;
                        var trackedWidth = ApplyTrackingWidth(run.Layout.Width, run.Layout.Text.Length, run.TrackingFactor) * scale;
                        if (!ShxTextGeometryBuilder.TryAddText(
                                builder,
                                run.Layout.Text,
                                runStyle,
                                context,
                                anchor,
                                offset,
                                runHeight,
                                run.WidthFactor,
                                rotation,
                                run.ObliqueAngle,
                                mirrorX,
                                mirrorY,
                                run.Color))
                        {
                            builder.Add(new RenderText(
                                run.Layout.Text,
                                anchor,
                                offset,
                                trackedWidth,
                                run.Layout.Height * scale,
                                runHeight,
                                run.WidthFactor,
                                rotation,
                                run.ObliqueAngle,
                                run.IsBold,
                                run.IsItalic,
                                mirrorX,
                                mirrorY,
                                run.Color,
                                run.FontFamily,
                                run.Decorations,
                                run.TrackingFactor));
                        }
                    }

                    x += run.Advance + extraSpace * run.SpaceCount;
                }

                y += line.Height + line.ExtraSpacingAfter;
            }

            columnOffset += column.Width + columnGutter;
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

    private static IReadOnlyList<MTextColumnLayout> BuildColumns(
        IReadOnlyList<MTextLineLayout> lines,
        MText text,
        float scale,
        out float columnGutter)
    {
        columnGutter = 0f;
        if (lines.Count == 0)
        {
            return Array.Empty<MTextColumnLayout>();
        }

        var column = text.Column;
        if (column is null || column.ColumnType == ColumnType.NoColumns)
        {
            var width = MaxLineWidth(lines);
            var height = SumLineHeight(lines);
            return new[] { new MTextColumnLayout(lines, width, height) };
        }

        var columnWidth = column.ColumnWidth > 0 ? (float)column.ColumnWidth * scale : 0f;
        columnGutter = MathF.Max(0f, (float)column.ColumnGutter * scale);
        var columnCount = column.ColumnCount > 0 ? column.ColumnCount : 1;
        var useHeights = column.ColumnHeights.Count >= columnCount;
        var columns = new List<MTextColumnLayout>(columnCount);

        var currentLines = new List<MTextLineLayout>();
        var currentHeight = 0f;
        var columnIndex = 0;
        var maxHeight = useHeights ? (float)column.ColumnHeights[columnIndex] * scale : float.PositiveInfinity;

        foreach (var line in lines)
        {
            var lineHeight = line.Height + line.ExtraSpacingAfter;
            if (currentLines.Count > 0 && currentHeight + lineHeight > maxHeight && columnIndex + 1 < columnCount)
            {
                var width = columnWidth > 0f ? columnWidth : MaxLineWidth(currentLines);
                columns.Add(new MTextColumnLayout(currentLines.ToArray(), width, currentHeight));
                columnIndex++;
                currentLines = new List<MTextLineLayout>();
                currentHeight = 0f;
                maxHeight = useHeights ? (float)column.ColumnHeights[columnIndex] * scale : float.PositiveInfinity;
            }

            currentLines.Add(line);
            currentHeight += lineHeight;
        }

        if (currentLines.Count > 0)
        {
            var width = columnWidth > 0f ? columnWidth : MaxLineWidth(currentLines);
            columns.Add(new MTextColumnLayout(currentLines.ToArray(), width, currentHeight));
        }

        if (column.ColumnFlowReversed || text.DrawingDirection == DrawingDirectionType.RightToLeft)
        {
            columns.Reverse();
        }

        return columns;
    }

    private static float MaxLineWidth(IReadOnlyList<MTextLineLayout> lines)
    {
        var width = 0f;
        foreach (var line in lines)
        {
            width = MathF.Max(width, line.Width);
        }

        return width;
    }

    private static float SumLineHeight(IReadOnlyList<MTextLineLayout> lines)
    {
        var height = 0f;
        foreach (var line in lines)
        {
            height += line.Height + line.ExtraSpacingAfter;
        }

        return height;
    }

    private static float ComputeBlockWidth(IReadOnlyList<MTextColumnLayout> columns, float gutter)
    {
        if (columns.Count == 0)
        {
            return 0f;
        }

        var width = 0f;
        for (var i = 0; i < columns.Count; i++)
        {
            width += columns[i].Width;
            if (i < columns.Count - 1)
            {
                width += gutter;
            }
        }

        return width;
    }

    private static float ComputeBlockHeight(IReadOnlyList<MTextColumnLayout> columns)
    {
        var height = 0f;
        foreach (var column in columns)
        {
            height = MathF.Max(height, column.Height);
        }

        return height;
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
            fillColor = ToRenderColor(text.BackgroundColor, settings);
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

    private static RenderColor ToRenderColor(ACadSharp.Color color, CadRenderSceneSettings settings)
    {
        return RenderStyleUtils.ResolveColor(color, settings, 255);
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

    private static MTextLayout ParseFormattedText(string value, CadRenderSceneSettings settings)
    {
        var lines = new List<MTextLine> { new MTextLine(null) };
        var buffer = new StringBuilder();
        var state = MTextFormatState.Default.WithStackAlignment(ResolveStackAlignment(settings.StackedTextAlignment));
        var stack = new Stack<MTextFormatState>();

        void Flush()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            lines[^1].Runs.Add(new MTextRun(buffer.ToString(), state, isTab: false));
            buffer.Clear();
        }

        void NewLine(MTextLineBreakKind breakKind)
        {
            Flush();
            lines[^1].BreakKind = breakKind;
            lines.Add(new MTextLine(state.Alignment));
        }

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (current == '%' && i + 1 < value.Length && value[i + 1] == '<')
            {
                Flush();
                var field = ReadFieldValue(value, i + 2, out var fieldEnd);
                if (!string.IsNullOrEmpty(field))
                {
                    buffer.Append(field);
                }

                i = fieldEnd - 1;
                continue;
            }

            if (current == '%' && i + 2 < value.Length && value[i + 1] == '%')
            {
                if (TryParsePercentCode(value[i + 2], ref state, buffer))
                {
                    i += 2;
                    continue;
                }
            }

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
                        NewLine(MTextLineBreakKind.Paragraph);
                        i++;
                        continue;
                    case 'n':
                    case 'N':
                        NewLine(MTextLineBreakKind.Line);
                        i++;
                        continue;
                    case '~':
                        buffer.Append(' ');
                        i++;
                        continue;
                    case 't':
                        Flush();
                        lines[^1].Runs.Add(new MTextRun("\t", state, isTab: true));
                        i++;
                        continue;
                    case 'T':
                        Flush();
                        var trackingValue = ReadDelimitedValue(value, i + 2, out var trackingEnd);
                        state = state.WithTracking(ParseTracking(trackingValue));
                        i = trackingEnd - 1;
                        continue;
                    case 'S':
                        Flush();
                        var stackedValue = ReadDelimitedValue(value, i + 2, out var stackEnd);
                        if (TryParseStacked(stackedValue, out var stacked))
                        {
                            lines[^1].Runs.Add(new MTextRun(stacked, state));
                        }
                        else if (!string.IsNullOrEmpty(stackedValue))
                        {
                            buffer.Append(stackedValue);
                        }
                        i = stackEnd - 1;
                        continue;
                    case 'U':
                        if (TryParseUnicode(value, i + 2, out var unicode, out var unicodeEnd))
                        {
                            buffer.Append(unicode);
                            i = unicodeEnd - 1;
                            continue;
                        }
                        break;
                    case 'L':
                        Flush();
                        state = state.EnableDecoration(RenderTextDecoration.Underline);
                        i++;
                        continue;
                    case 'l':
                        Flush();
                        state = state.DisableDecoration(RenderTextDecoration.Underline);
                        i++;
                        continue;
                    case 'O':
                        Flush();
                        state = state.EnableDecoration(RenderTextDecoration.Overline);
                        i++;
                        continue;
                    case 'o':
                        Flush();
                        state = state.DisableDecoration(RenderTextDecoration.Overline);
                        i++;
                        continue;
                    case 'K':
                        Flush();
                        state = state.EnableDecoration(RenderTextDecoration.StrikeThrough);
                        i++;
                        continue;
                    case 'k':
                        Flush();
                        state = state.DisableDecoration(RenderTextDecoration.StrikeThrough);
                        i++;
                        continue;
                    case 'C':
                    case 'c':
                        Flush();
                        var colorValue = ReadDelimitedValue(value, i + 2, out var colorEnd);
                        state = state.WithColor(ParseColor(code, colorValue, settings));
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
                    case 'a':
                        Flush();
                        var alignValue = ReadDelimitedValue(value, i + 2, out var alignEnd);
                        state = state.WithStackAlignment(ParseStackAlignment(alignValue));
                        i = alignEnd - 1;
                        continue;
                    case 'p':
                        Flush();
                        var paragraphValue = ReadDelimitedValue(value, i + 2, out var paragraphEnd);
                        _ = paragraphValue;
                        i = paragraphEnd - 1;
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

    private static string ReadFieldValue(string text, int start, out int end)
    {
        var index = text.IndexOf(">%", start, StringComparison.Ordinal);
        if (index < 0)
        {
            end = text.Length;
            return text.Substring(start);
        }

        end = index + 2;
        var content = text.Substring(start, index - start);
        return TextProcessor.Parse(content, out _);
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

    private static RenderColor? ParseColor(char code, string value, CadRenderSceneSettings settings)
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

        return RenderStyleUtils.ResolveColor(color, settings, 255);
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

    private static bool TryParseStacked(string value, out MTextStacked stacked)
    {
        stacked = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separatorIndex = -1;
        var separator = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (current is '/' or '#' or '^')
            {
                separatorIndex = i;
                separator = current;
                break;
            }
        }

        if (separatorIndex < 0)
        {
            return false;
        }

        var upper = DecodeStackedPart(value.Substring(0, separatorIndex));
        var lower = DecodeStackedPart(value.Substring(separatorIndex + 1));
        var kind = separator switch
        {
            '/' => MTextStackedKind.Horizontal,
            '#' => MTextStackedKind.Diagonal,
            '^' => MTextStackedKind.Tolerance,
            _ => MTextStackedKind.Horizontal
        };

        stacked = new MTextStacked(upper, lower, kind);
        return true;
    }

    private static string DecodeStackedPart(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (current == '\\' && i + 1 < value.Length)
            {
                var next = value[i + 1];
                if (next is '\\' or '{' or '}')
                {
                    buffer.Append(next);
                    i++;
                    continue;
                }

                if (next == '~')
                {
                    buffer.Append(' ');
                    i++;
                    continue;
                }
            }

            if (current == '%' && i + 2 < value.Length && value[i + 1] == '%')
            {
                var tempState = MTextFormatState.Default;
                if (TryParsePercentCode(value[i + 2], ref tempState, buffer))
                {
                    i += 2;
                    continue;
                }
            }

            buffer.Append(current);
        }

        return buffer.ToString();
    }

    private static bool TryParseUnicode(string text, int start, out string value, out int end)
    {
        value = string.Empty;
        end = start;
        if (start + 2 > text.Length || text[start] != '+')
        {
            return false;
        }

        var i = start + 1;
        var hexStart = i;
        while (i < text.Length && IsHexDigit(text[i]))
        {
            i++;
        }

        if (i == hexStart)
        {
            return false;
        }

        var hex = text.Substring(hexStart, i - hexStart);
        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint))
        {
            return false;
        }

        value = char.ConvertFromUtf32(codePoint);
        if (i < text.Length && text[i] == ';')
        {
            i++;
        }
        end = i;
        return true;
    }

    private static bool IsHexDigit(char value)
    {
        return (value >= '0' && value <= '9')
            || (value >= 'a' && value <= 'f')
            || (value >= 'A' && value <= 'F');
    }

    private static float ParseTracking(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 1f;
        }

        var trimmed = value.Trim();
        if (trimmed.EndsWith("x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1);
        }

        var parsed = ParseFloat(trimmed) ?? 1f;
        if (parsed <= 0f)
        {
            return 1f;
        }

        return Math.Clamp(parsed, 0.75f, 4f);
    }

    private static MTextStackAlignment? ParseStackAlignment(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        return parsed switch
        {
            0 => MTextStackAlignment.Bottom,
            1 => MTextStackAlignment.Center,
            2 => MTextStackAlignment.Top,
            _ => null
        };
    }

    private static MTextStackAlignment ResolveStackAlignment(short value)
    {
        return value switch
        {
            0 => MTextStackAlignment.Bottom,
            1 => MTextStackAlignment.Center,
            2 => MTextStackAlignment.Top,
            _ => MTextStackAlignment.Bottom
        };
    }

    private static bool TryParsePercentCode(char code, ref MTextFormatState state, StringBuilder buffer)
    {
        switch (char.ToLowerInvariant(code))
        {
            case 'd':
                buffer.Append('\u00B0');
                return true;
            case 'p':
                buffer.Append('\u00B1');
                return true;
            case 'c':
                buffer.Append('\u2300');
                return true;
            case 'u':
                state = state.ToggleDecoration(RenderTextDecoration.Underline);
                return true;
            case 'o':
                state = state.ToggleDecoration(RenderTextDecoration.Overline);
                return true;
            case '%':
                buffer.Append('%');
                return true;
            default:
                return false;
        }
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
        var paragraphSpacing = baseHeight * 0.5f * scale;

        foreach (var line in lines)
        {
            if (line.Runs.Count == 0)
            {
                var alignment = ResolveLineAlignment(line, text);
                var extra = line.BreakKind == MTextLineBreakKind.Paragraph ? paragraphSpacing : 0f;
                layouts.Add(new MTextLineLayout(new List<MTextRunLayout>(), 0f, baseLineHeight, extra, alignment, 0));
                continue;
            }

            var runLayouts = new List<MTextRunLayout>(line.Runs.Count);
            var lineWidth = 0f;
            var maxRunHeight = 0f;
            var spaceCount = 0;

            foreach (var run in line.Runs)
            {
                var resolved = ResolveRunStyle(text, run.State, baseWidthFactor, baseObliqueAngle, baseBold, baseItalic, baseFontFamily, baseColor);
                var runHeight = resolved.Height;
                var decorations = run.State.Decorations;
                var tracking = resolved.TrackingFactor;
                var style = CreateRunStyle(text.Style, resolved);

                if (run.IsTab)
                {
                    var tabWidth = ResolveTabWidth(runHeight, resolved.WidthFactor);
                    lineWidth += tabWidth * scale;
                    maxRunHeight = MathF.Max(maxRunHeight, runHeight * scale);
                    runLayouts.Add(new MTextRunLayout(
                        new RenderTextLayout(string.Empty, 0f, 0f),
                        resolved.Color,
                        resolved.FontFamily,
                        resolved.WidthFactor,
                        resolved.ObliqueAngle,
                        resolved.IsBold,
                        resolved.IsItalic,
                        runHeight,
                        tabWidth * scale,
                        0,
                        decorations,
                        tracking,
                        run.State.StackAlignment,
                        null,
                        null,
                        0f));
                    continue;
                }

                if (run.IsStacked)
                {
                    var stackedLayout = BuildStackedLayout(run.Stacked!.Value, runHeight, tracking, style, context);
                    var stackWidthWorld = stackedLayout.Width * scale * resolved.WidthFactor;
                    var stackHeightWorld = stackedLayout.Height * scale;
                    lineWidth += stackWidthWorld;
                    maxRunHeight = MathF.Max(maxRunHeight, stackHeightWorld);

                    var stackBackgroundColor = ResolveRunBackground(text, context.Settings);
                    var stackBackgroundPadding = runHeight * 0.15f;

                    runLayouts.Add(new MTextRunLayout(
                        new RenderTextLayout(string.Empty, 0f, 0f),
                        resolved.Color,
                        resolved.FontFamily,
                        resolved.WidthFactor,
                        resolved.ObliqueAngle,
                        resolved.IsBold,
                        resolved.IsItalic,
                        runHeight,
                        stackWidthWorld,
                        0,
                        decorations,
                        tracking,
                        run.State.StackAlignment,
                        stackedLayout,
                        stackBackgroundColor,
                        stackBackgroundPadding));
                    continue;
                }

                var layout = MeasureRunLayout(run.Text, runHeight, style, context);
                if (string.IsNullOrEmpty(layout.Text))
                {
                    continue;
                }

                var trackedWidth = ApplyTrackingWidth(layout.Width, run.Text.Length, tracking);
                var runWidth = trackedWidth * scale * resolved.WidthFactor;
                var runHeightWorld = layout.Height * scale;
                lineWidth += runWidth;
                maxRunHeight = MathF.Max(maxRunHeight, runHeightWorld);

                var runSpaces = CountSpaces(run.Text);
                spaceCount += runSpaces;

                var backgroundColor = ResolveRunBackground(text, context.Settings);
                var backgroundPadding = runHeight * 0.15f;

                runLayouts.Add(new MTextRunLayout(
                    layout,
                    resolved.Color,
                    resolved.FontFamily,
                    resolved.WidthFactor,
                    resolved.ObliqueAngle,
                    resolved.IsBold,
                    resolved.IsItalic,
                    runHeight,
                    runWidth,
                    runSpaces,
                    decorations,
                    tracking,
                    run.State.StackAlignment,
                    null,
                    backgroundColor,
                    backgroundPadding));
            }

            var lineHeight = text.LineSpacingStyle == LineSpacingStyleType.Exact
                ? baseLineHeight
                : MathF.Max(baseLineHeight, maxRunHeight);
            var extraSpacingAfter = line.BreakKind == MTextLineBreakKind.Paragraph ? paragraphSpacing : 0f;
            var alignmentValue = ResolveLineAlignment(line, text);

            layouts.Add(new MTextLineLayout(runLayouts, lineWidth, lineHeight, extraSpacingAfter, alignmentValue, spaceCount));
        }

        return layouts;
    }

    private static MTextStackedLayout BuildStackedLayout(
        MTextStacked stacked,
        float runHeight,
        float tracking,
        TextStyle style,
        RenderBuildContext context)
    {
        var stackScale = ResolveStackScale(context.Settings);
        var upperHeight = runHeight * stackScale;
        var lowerHeight = runHeight * stackScale;

        var upperLayout = MeasureRunLayout(stacked.Upper, upperHeight, style, context);
        var lowerLayout = MeasureRunLayout(stacked.Lower, lowerHeight, style, context);

        var upperAdvance = ApplyTrackingWidth(upperLayout.Width, stacked.Upper.Length, tracking);
        var lowerAdvance = ApplyTrackingWidth(lowerLayout.Width, stacked.Lower.Length, tracking);

        var gap = MathF.Max(runHeight * 0.15f, runHeight * 0.05f);
        if (string.IsNullOrEmpty(upperLayout.Text) || string.IsNullOrEmpty(lowerLayout.Text))
        {
            gap = MathF.Max(runHeight * 0.1f, runHeight * 0.03f);
        }

        var width = stacked.Kind == MTextStackedKind.Diagonal
            ? upperAdvance + lowerAdvance + gap
            : MathF.Max(upperAdvance, lowerAdvance);

        var height = upperLayout.Height + lowerLayout.Height + gap;
        if (string.IsNullOrEmpty(upperLayout.Text))
        {
            height = lowerLayout.Height;
        }
        else if (string.IsNullOrEmpty(lowerLayout.Text))
        {
            height = upperLayout.Height;
        }

        return new MTextStackedLayout(
            upperLayout,
            lowerLayout,
            upperAdvance,
            lowerAdvance,
            width,
            height,
            gap,
            stackScale,
            stacked.Kind);
    }

    private static float ResolveStackScale(CadRenderSceneSettings settings)
    {
        var percent = settings.StackedTextSizePercentage;
        if (percent <= 0)
        {
            return 0.7f;
        }

        return Math.Clamp(percent / 100f, 0.1f, 1f);
    }

    private static float ApplyTrackingWidth(float width, int length, float tracking)
    {
        if (length < 2 || tracking <= 0f || float.IsNaN(tracking) || float.IsInfinity(tracking))
        {
            return width;
        }

        return width * tracking;
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
        var tracking = state.TrackingFactor > 0f ? state.TrackingFactor : 1f;
        var color = state.Color ?? baseColor;
        if (state.Color.HasValue)
        {
            color = new RenderColor(state.Color.Value.R, state.Color.Value.G, state.Color.Value.B, baseColor.A);
        }

        return new MTextResolvedStyle(height, widthFactor, oblique, isBold, isItalic, fontFamily, tracking, color);
    }

    private static MTextAlignment ResolveLineAlignment(MTextLine line, MText text)
    {
        if (line.Alignment.HasValue)
        {
            return line.Alignment.Value;
        }

        return text.AttachmentPoint switch
        {
            AttachmentPointType.TopCenter => MTextAlignment.Center,
            AttachmentPointType.MiddleCenter => MTextAlignment.Center,
            AttachmentPointType.BottomCenter => MTextAlignment.Center,
            AttachmentPointType.TopRight => MTextAlignment.Right,
            AttachmentPointType.MiddleRight => MTextAlignment.Right,
            AttachmentPointType.BottomRight => MTextAlignment.Right,
            _ => MTextAlignment.Left
        };
    }

    private static float ResolveTabWidth(float height, float widthFactor)
    {
        var baseWidth = MathF.Max(height * widthFactor, 0.1f);
        return baseWidth * 4f;
    }

    private static int CountSpaces(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;
        foreach (var ch in text)
        {
            if (ch == ' ')
            {
                count++;
            }
        }

        return count;
    }

    private static RenderColor? ResolveRunBackground(MText text, CadRenderSceneSettings settings)
    {
        if (text.BackgroundFillFlags == BackgroundFillFlags.None)
        {
            return null;
        }

        RenderColor fillColor;
        if (text.BackgroundFillFlags.HasFlag(BackgroundFillFlags.UseBackgroundFillColor))
        {
            var color = text.BackgroundColor;
            if (color.IsByLayer || color.IsByBlock)
            {
                fillColor = settings.Background;
            }
            else
            {
                fillColor = ToRenderColor(color, settings);
            }
        }
        else if (text.BackgroundFillFlags.HasFlag(BackgroundFillFlags.UseDrawingWindowColor))
        {
            fillColor = settings.Background;
        }
        else
        {
            return null;
        }

        var alpha = ResolveTransparencyAlpha(text.BackgroundTransparency);
        return new RenderColor(fillColor.R, fillColor.G, fillColor.B, alpha);
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

    private static TextStyle ResolveRunTextStyle(TextStyle baseStyle, MTextRunLayout run)
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

        if (!string.IsNullOrWhiteSpace(run.FontFamily))
        {
            runStyle.Filename = run.FontFamily;
        }

        return runStyle;
    }

    private static void AppendStackedRun(
        RenderLayerBuilder builder,
        RenderBuildContext context,
        MText text,
        MTextRunLayout run,
        MTextLineLayout line,
        Vector2 anchor,
        Vector2 offset,
        float scale,
        float rotation,
        bool mirrorX,
        bool mirrorY,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        float lineWeight)
    {
        var stacked = run.StackedLayout!.Value;
        var stackHeight = stacked.Height * scale;
        var stackTop = offset.Y + ResolveStackAlignmentOffset(run.StackAlignment, line.Height, stackHeight);
        var stackWidth = run.Advance;

        var upperText = stacked.UpperLayout.Text;
        var lowerText = stacked.LowerLayout.Text;
        var hasUpper = !string.IsNullOrWhiteSpace(upperText);
        var hasLower = !string.IsNullOrWhiteSpace(lowerText);

        var upperWidth = stacked.UpperAdvance * scale * run.WidthFactor;
        var lowerWidth = stacked.LowerAdvance * scale * run.WidthFactor;
        var upperHeight = stacked.UpperLayout.Height * scale;
        var lowerHeight = stacked.LowerLayout.Height * scale;
        var gap = stacked.Gap * scale;

        var upperOffsetX = stacked.Kind == MTextStackedKind.Diagonal
            ? offset.X
            : offset.X + (stackWidth - upperWidth) * 0.5f;
        var lowerOffsetX = stacked.Kind == MTextStackedKind.Diagonal
            ? offset.X + stackWidth - lowerWidth
            : offset.X + (stackWidth - lowerWidth) * 0.5f;

        var upperOffset = new Vector2(upperOffsetX, stackTop);
        var lowerOffset = new Vector2(lowerOffsetX, stackTop + (hasUpper ? upperHeight + gap : 0f));

        var runStyle = ResolveRunTextStyle(text.Style, run);
        var fontSize = run.FontSize * stacked.Scale * scale;
        var decoration = RenderTextDecoration.None;

        if (hasUpper)
        {
            var trackedWidth = ApplyTrackingWidth(stacked.UpperLayout.Width, upperText.Length, run.TrackingFactor) * scale;
            if (!ShxTextGeometryBuilder.TryAddText(
                    builder,
                    upperText,
                    runStyle,
                    context,
                    anchor,
                    upperOffset,
                    fontSize,
                    run.WidthFactor,
                    rotation,
                    run.ObliqueAngle,
                    mirrorX,
                    mirrorY,
                    run.Color))
            {
                builder.Add(new RenderText(
                    upperText,
                    anchor,
                    upperOffset,
                    trackedWidth,
                    stacked.UpperLayout.Height * scale,
                    fontSize,
                    run.WidthFactor,
                    rotation,
                    run.ObliqueAngle,
                    run.IsBold,
                    run.IsItalic,
                    mirrorX,
                    mirrorY,
                    run.Color,
                    run.FontFamily,
                    decoration,
                    run.TrackingFactor));
            }
        }

        if (hasLower)
        {
            var trackedWidth = ApplyTrackingWidth(stacked.LowerLayout.Width, lowerText.Length, run.TrackingFactor) * scale;
            if (!ShxTextGeometryBuilder.TryAddText(
                    builder,
                    lowerText,
                    runStyle,
                    context,
                    anchor,
                    lowerOffset,
                    fontSize,
                    run.WidthFactor,
                    rotation,
                    run.ObliqueAngle,
                    mirrorX,
                    mirrorY,
                    run.Color))
            {
                builder.Add(new RenderText(
                    lowerText,
                    anchor,
                    lowerOffset,
                    trackedWidth,
                    stacked.LowerLayout.Height * scale,
                    fontSize,
                    run.WidthFactor,
                    rotation,
                    run.ObliqueAngle,
                    run.IsBold,
                    run.IsItalic,
                    mirrorX,
                    mirrorY,
                    run.Color,
                    run.FontFamily,
                    decoration,
                    run.TrackingFactor));
            }
        }

        var lineThickness = MathF.Max(run.FontSize * scale * 0.05f, lineWeight);

        if (hasUpper && hasLower)
        {
            if (stacked.Kind == MTextStackedKind.Horizontal)
            {
                var y = stackTop + upperHeight + gap * 0.5f;
                AddTextLine(builder, anchor, new Vector2(offset.X, y), new Vector2(offset.X + stackWidth, y), run, rotation, mirrorX, mirrorY, lineCap, lineJoin, lineThickness);
            }
            else if (stacked.Kind == MTextStackedKind.Diagonal)
            {
                var start = new Vector2(offset.X, stackTop + upperHeight + gap);
                var end = new Vector2(offset.X + stackWidth, stackTop);
                AddTextLine(builder, anchor, start, end, run, rotation, mirrorX, mirrorY, lineCap, lineJoin, lineThickness);
            }
        }

        if (run.Decorations != RenderTextDecoration.None)
        {
            var top = stackTop;
            var bottom = stackTop + stackHeight;
            var middle = stackTop + stackHeight * 0.5f;
            if (run.Decorations.HasFlag(RenderTextDecoration.Overline))
            {
                AddTextLine(builder, anchor, new Vector2(offset.X, top), new Vector2(offset.X + stackWidth, top), run, rotation, mirrorX, mirrorY, lineCap, lineJoin, lineThickness);
            }
            if (run.Decorations.HasFlag(RenderTextDecoration.Underline))
            {
                AddTextLine(builder, anchor, new Vector2(offset.X, bottom), new Vector2(offset.X + stackWidth, bottom), run, rotation, mirrorX, mirrorY, lineCap, lineJoin, lineThickness);
            }
            if (run.Decorations.HasFlag(RenderTextDecoration.StrikeThrough))
            {
                AddTextLine(builder, anchor, new Vector2(offset.X, middle), new Vector2(offset.X + stackWidth, middle), run, rotation, mirrorX, mirrorY, lineCap, lineJoin, lineThickness);
            }
        }
    }

    private static float ResolveStackAlignmentOffset(MTextStackAlignment alignment, float lineHeight, float stackHeight)
    {
        var delta = MathF.Max(0f, lineHeight - stackHeight);
        return alignment switch
        {
            MTextStackAlignment.Center => delta * 0.5f,
            MTextStackAlignment.Top => 0f,
            _ => delta
        };
    }

    private static void AddTextLine(
        RenderLayerBuilder builder,
        Vector2 anchor,
        Vector2 start,
        Vector2 end,
        MTextRunLayout run,
        float rotation,
        bool mirrorX,
        bool mirrorY,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        float thickness)
    {
        var worldStart = TransformTextPoint(anchor, start, run.WidthFactor, rotation, run.ObliqueAngle, mirrorX, mirrorY);
        var worldEnd = TransformTextPoint(anchor, end, run.WidthFactor, rotation, run.ObliqueAngle, mirrorX, mirrorY);
        builder.Add(new RenderLine(worldStart, worldEnd, run.Color, thickness, lineCap, lineJoin));
    }

    private static Vector2 TransformTextPoint(
        Vector2 anchor,
        Vector2 point,
        float widthFactor,
        float rotation,
        float obliqueAngle,
        bool mirrorX,
        bool mirrorY)
    {
        var scaleX = widthFactor * (mirrorX ? -1f : 1f);
        var scaleY = mirrorY ? 1f : -1f;
        var transformed = new Vector2(point.X * scaleX, point.Y * scaleY);

        var hasOblique = MathF.Abs(obliqueAngle) > 0.0001f;
        if (hasOblique)
        {
            var shear = MathF.Tan(obliqueAngle);
            transformed = new Vector2(transformed.X + transformed.Y * shear, transformed.Y);
        }

        if (MathF.Abs(rotation) > 0.0001f)
        {
            var sin = MathF.Sin(rotation);
            var cos = MathF.Cos(rotation);
            transformed = new Vector2(
                transformed.X * cos - transformed.Y * sin,
                transformed.X * sin + transformed.Y * cos);
        }

        return anchor + transformed;
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

    private enum MTextLineBreakKind
    {
        Line,
        Paragraph
    }

    private enum MTextStackAlignment
    {
        Bottom = 0,
        Center = 1,
        Top = 2
    }

    private enum MTextAlignment
    {
        Left,
        Center,
        Right,
        Justify
    }

    private enum MTextStackedKind
    {
        Horizontal,
        Diagonal,
        Tolerance
    }

    private sealed class MTextLine
    {
        public List<MTextRun> Runs { get; } = new();
        public MTextLineBreakKind BreakKind { get; set; } = MTextLineBreakKind.Line;
        public MTextAlignment? Alignment { get; }

        public MTextLine(MTextAlignment? alignment)
        {
            Alignment = alignment;
        }
    }

    private readonly struct MTextStacked
    {
        public string Upper { get; }
        public string Lower { get; }
        public MTextStackedKind Kind { get; }

        public MTextStacked(string upper, string lower, MTextStackedKind kind)
        {
            Upper = upper ?? string.Empty;
            Lower = lower ?? string.Empty;
            Kind = kind;
        }
    }

    private readonly struct MTextRun
    {
        public string Text { get; }
        public MTextStacked? Stacked { get; }
        public MTextFormatState State { get; }
        public bool IsTab { get; }
        public bool IsStacked => Stacked.HasValue;

        public MTextRun(string text, MTextFormatState state, bool isTab)
        {
            Text = text;
            Stacked = null;
            State = state;
            IsTab = isTab;
        }

        public MTextRun(MTextStacked stacked, MTextFormatState state)
        {
            Text = string.Empty;
            Stacked = stacked;
            State = state;
            IsTab = false;
        }
    }

    private readonly struct MTextLineLayout
    {
        public IReadOnlyList<MTextRunLayout> Runs { get; }
        public float Width { get; }
        public float Height { get; }
        public float ExtraSpacingAfter { get; }
        public MTextAlignment Alignment { get; }
        public int SpaceCount { get; }

        public MTextLineLayout(
            IReadOnlyList<MTextRunLayout> runs,
            float width,
            float height,
            float extraSpacingAfter,
            MTextAlignment alignment,
            int spaceCount)
        {
            Runs = runs;
            Width = width;
            Height = height;
            ExtraSpacingAfter = extraSpacingAfter;
            Alignment = alignment;
            SpaceCount = spaceCount;
        }
    }

    private readonly struct MTextColumnLayout
    {
        public IReadOnlyList<MTextLineLayout> Lines { get; }
        public float Width { get; }
        public float Height { get; }

        public MTextColumnLayout(IReadOnlyList<MTextLineLayout> lines, float width, float height)
        {
            Lines = lines;
            Width = width;
            Height = height;
        }
    }

    private readonly struct MTextStackedLayout
    {
        public RenderTextLayout UpperLayout { get; }
        public RenderTextLayout LowerLayout { get; }
        public float UpperAdvance { get; }
        public float LowerAdvance { get; }
        public float Width { get; }
        public float Height { get; }
        public float Gap { get; }
        public float Scale { get; }
        public MTextStackedKind Kind { get; }

        public MTextStackedLayout(
            RenderTextLayout upperLayout,
            RenderTextLayout lowerLayout,
            float upperAdvance,
            float lowerAdvance,
            float width,
            float height,
            float gap,
            float scale,
            MTextStackedKind kind)
        {
            UpperLayout = upperLayout;
            LowerLayout = lowerLayout;
            UpperAdvance = upperAdvance;
            LowerAdvance = lowerAdvance;
            Width = width;
            Height = height;
            Gap = gap;
            Scale = scale;
            Kind = kind;
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
        public int SpaceCount { get; }
        public RenderTextDecoration Decorations { get; }
        public float TrackingFactor { get; }
        public MTextStackAlignment StackAlignment { get; }
        public MTextStackedLayout? StackedLayout { get; }
        public RenderColor? BackgroundColor { get; }
        public float BackgroundPadding { get; }

        public MTextRunLayout(
            RenderTextLayout layout,
            RenderColor color,
            string? fontFamily,
            float widthFactor,
            float obliqueAngle,
            bool isBold,
            bool isItalic,
            float fontSize,
            float advance,
            int spaceCount,
            RenderTextDecoration decorations,
            float trackingFactor,
            MTextStackAlignment stackAlignment,
            MTextStackedLayout? stackedLayout,
            RenderColor? backgroundColor,
            float backgroundPadding)
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
            SpaceCount = spaceCount;
            Decorations = decorations;
            TrackingFactor = trackingFactor;
            StackAlignment = stackAlignment;
            StackedLayout = stackedLayout;
            BackgroundColor = backgroundColor;
            BackgroundPadding = backgroundPadding;
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
        public float TrackingFactor { get; }
        public RenderColor Color { get; }

        public MTextResolvedStyle(
            float height,
            float widthFactor,
            float obliqueAngle,
            bool isBold,
            bool isItalic,
            string? fontFamily,
            float trackingFactor,
            RenderColor color)
        {
            Height = height;
            WidthFactor = widthFactor;
            ObliqueAngle = obliqueAngle;
            IsBold = isBold;
            IsItalic = isItalic;
            FontFamily = fontFamily;
            TrackingFactor = trackingFactor;
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
        public static MTextFormatState Default => new(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            RenderTextDecoration.None,
            1f,
            MTextStackAlignment.Bottom);

        public RenderColor? Color { get; }
        public string? FontFamily { get; }
        public bool? IsBold { get; }
        public bool? IsItalic { get; }
        public MTextHeight Height { get; }
        public float? WidthFactor { get; }
        public float? ObliqueAngle { get; }
        public MTextAlignment? Alignment { get; }
        public RenderTextDecoration Decorations { get; }
        public float TrackingFactor { get; }
        public MTextStackAlignment StackAlignment { get; }

        public MTextFormatState(
            RenderColor? color,
            string? fontFamily,
            bool? isBold,
            bool? isItalic,
            MTextHeight? height,
            float? widthFactor,
            float? obliqueAngle,
            MTextAlignment? alignment,
            RenderTextDecoration decorations,
            float trackingFactor,
            MTextStackAlignment stackAlignment)
        {
            Color = color;
            FontFamily = fontFamily;
            IsBold = isBold;
            IsItalic = isItalic;
            Height = height ?? default;
            WidthFactor = widthFactor;
            ObliqueAngle = obliqueAngle;
            Alignment = alignment;
            Decorations = decorations;
            TrackingFactor = trackingFactor;
            StackAlignment = stackAlignment;
        }

        public MTextFormatState WithColor(RenderColor? color)
        {
            return new MTextFormatState(color, FontFamily, IsBold, IsItalic, Height, WidthFactor, ObliqueAngle, Alignment, Decorations, TrackingFactor, StackAlignment);
        }

        public MTextFormatState WithFont(MTextFont font)
        {
            var name = !string.IsNullOrWhiteSpace(font.Name) ? font.Name : FontFamily;
            var bold = font.IsBold ?? IsBold;
            var italic = font.IsItalic ?? IsItalic;
            return new MTextFormatState(Color, name, bold, italic, Height, WidthFactor, ObliqueAngle, Alignment, Decorations, TrackingFactor, StackAlignment);
        }

        public MTextFormatState WithHeight(MTextHeight height)
        {
            return new MTextFormatState(Color, FontFamily, IsBold, IsItalic, height, WidthFactor, ObliqueAngle, Alignment, Decorations, TrackingFactor, StackAlignment);
        }

        public MTextFormatState WithWidthFactor(float? widthFactor)
        {
            return new MTextFormatState(Color, FontFamily, IsBold, IsItalic, Height, widthFactor, ObliqueAngle, Alignment, Decorations, TrackingFactor, StackAlignment);
        }

        public MTextFormatState WithOblique(float? obliqueAngle)
        {
            return new MTextFormatState(Color, FontFamily, IsBold, IsItalic, Height, WidthFactor, obliqueAngle, Alignment, Decorations, TrackingFactor, StackAlignment);
        }

        public MTextFormatState WithAlignment(MTextAlignment? alignment)
        {
            return new MTextFormatState(Color, FontFamily, IsBold, IsItalic, Height, WidthFactor, ObliqueAngle, alignment, Decorations, TrackingFactor, StackAlignment);
        }

        public MTextFormatState WithTracking(float trackingFactor)
        {
            var next = trackingFactor <= 0f || float.IsNaN(trackingFactor) || float.IsInfinity(trackingFactor)
                ? 1f
                : trackingFactor;
            return new MTextFormatState(Color, FontFamily, IsBold, IsItalic, Height, WidthFactor, ObliqueAngle, Alignment, Decorations, next, StackAlignment);
        }

        public MTextFormatState WithStackAlignment(MTextStackAlignment? alignment)
        {
            return new MTextFormatState(Color, FontFamily, IsBold, IsItalic, Height, WidthFactor, ObliqueAngle, Alignment, Decorations, TrackingFactor, alignment ?? StackAlignment);
        }

        public MTextFormatState EnableDecoration(RenderTextDecoration decoration)
        {
            return new MTextFormatState(Color, FontFamily, IsBold, IsItalic, Height, WidthFactor, ObliqueAngle, Alignment, Decorations | decoration, TrackingFactor, StackAlignment);
        }

        public MTextFormatState DisableDecoration(RenderTextDecoration decoration)
        {
            return new MTextFormatState(Color, FontFamily, IsBold, IsItalic, Height, WidthFactor, ObliqueAngle, Alignment, Decorations & ~decoration, TrackingFactor, StackAlignment);
        }

        public MTextFormatState ToggleDecoration(RenderTextDecoration decoration)
        {
            return new MTextFormatState(Color, FontFamily, IsBold, IsItalic, Height, WidthFactor, ObliqueAngle, Alignment, Decorations ^ decoration, TrackingFactor, StackAlignment);
        }
    }
}
