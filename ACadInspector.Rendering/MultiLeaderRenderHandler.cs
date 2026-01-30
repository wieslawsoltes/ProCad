using System;
using System.Collections.Generic;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class MultiLeaderRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is MultiLeader;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var multiLeader = (MultiLeader)entity;
        var data = ResolveContextData(multiLeader);
        if (data is null)
        {
            return;
        }

        var baseColor = context.ResolveEntityColor(multiLeader);
        var baseThickness = context.ResolveLineWeight(multiLeader);
        var lineCap = context.ResolveLineCap(multiLeader);
        var lineJoin = context.ResolveLineJoin(multiLeader);

        foreach (var root in data.LeaderRoots)
        {
            foreach (var line in root.Lines)
            {
                RenderLeaderLine(multiLeader, data, line, transform, context, baseColor, baseThickness, lineCap, lineJoin);
            }
        }

        RenderContent(multiLeader, data, transform, context);
    }

    private static MultiLeaderObjectContextData? ResolveContextData(MultiLeader multiLeader)
    {
        if (multiLeader is null)
        {
            return null;
        }

        return multiLeader.ContextData;
    }

    private static void RenderLeaderLine(
        MultiLeader multiLeader,
        MultiLeaderObjectContextData contextData,
        MultiLeaderObjectContextData.LeaderLine line,
        Transform transform,
        RenderBuildContext context,
        RenderColor baseColor,
        float baseThickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin)
    {
        var points = line.Points;
        if (points is null || points.Count < 2)
        {
            RenderLeaderSegments(multiLeader, contextData, line, transform, context, baseColor, baseThickness, lineCap, lineJoin);
            return;
        }

        var lineType = ResolveLineType(multiLeader, line);
        var pattern = ResolveLinePattern(multiLeader, lineType, context);
        var color = ResolveLineColor(multiLeader, line, baseColor);
        var thickness = ResolveLineWeight(multiLeader, line, baseThickness, context.Settings);

        var builder = context.GetLayerBuilder(multiLeader);
        RenderPrimitiveBuilder.AddSampled(
            builder,
            points,
            transform,
            isClosed: false,
            color,
            thickness,
            lineCap,
            lineJoin,
            pattern,
            context.ShapeResolver,
            context.Settings);

        AppendArrow(multiLeader, contextData, line, points, transform, context);
    }

    private static void RenderLeaderSegments(
        MultiLeader multiLeader,
        MultiLeaderObjectContextData contextData,
        MultiLeaderObjectContextData.LeaderLine line,
        Transform transform,
        RenderBuildContext context,
        RenderColor baseColor,
        float baseThickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin)
    {
        if (line.StartEndPoints.Count == 0)
        {
            return;
        }

        var lineType = ResolveLineType(multiLeader, line);
        var pattern = ResolveLinePattern(multiLeader, lineType, context);
        var color = ResolveLineColor(multiLeader, line, baseColor);
        var thickness = ResolveLineWeight(multiLeader, line, baseThickness, context.Settings);
        var builder = context.GetLayerBuilder(multiLeader);

        foreach (var pair in line.StartEndPoints)
        {
            var segment = new List<XYZ> { pair.StartPoint, pair.EndPoint };
            RenderPrimitiveBuilder.AddSampled(
                builder,
                segment,
                transform,
                isClosed: false,
                color,
                thickness,
                lineCap,
                lineJoin,
                pattern,
                context.ShapeResolver,
                context.Settings);
        }

        if (line.StartEndPoints.Count > 0)
        {
            var first = line.StartEndPoints[0];
            AppendArrow(multiLeader, contextData, line, new[] { first.StartPoint, first.EndPoint }, transform, context);
        }
    }

    private static void AppendArrow(
        MultiLeader multiLeader,
        MultiLeaderObjectContextData contextData,
        MultiLeaderObjectContextData.LeaderLine line,
        IList<XYZ> points,
        Transform transform,
        RenderBuildContext context)
    {
        if (points.Count < 2)
        {
            return;
        }

        var arrowBlock = ResolveArrowBlock(multiLeader, line);
        var arrowSize = ResolveArrowSize(multiLeader, contextData, line);
        if (arrowSize <= 0)
        {
            return;
        }

        var start = points[0];
        var next = points[1];
        var dir = start - next;
        if (dir.IsZero())
        {
            return;
        }

        dir = dir.Normalize();
        var rotation = Math.Atan2(dir.Y, dir.X);
        var lineColor = ResolveLineColorValue(multiLeader, line);
        var normal = ResolveTextNormal(contextData);
        var arrow = CreateArrowEntity(multiLeader, start, dir, rotation, arrowSize, arrowBlock, lineColor, normal);
        if (arrow is null)
        {
            return;
        }

        context.Dispatcher.Append(arrow, transform, context);
    }

    private static Entity? CreateArrowEntity(
        MultiLeader multiLeader,
        XYZ insertPoint,
        XYZ dir,
        double rotation,
        double arrowSize,
        BlockRecord? arrowBlock,
        Color lineColor,
        XYZ normal)
    {
        if (arrowSize <= 0)
        {
            return null;
        }

        var arrowNormal = normal.IsZero() ? XYZ.AxisZ : normal.Normalize();
        var arrowColor = lineColor;
        if (arrowColor.IsByLayer || arrowColor.IsByBlock)
        {
            arrowColor = multiLeader.Color;
        }

        if (arrowBlock is null)
        {
            var perp = XYZ.Cross(arrowNormal, dir).Normalize();
            return new Solid
            {
                FirstCorner = insertPoint,
                SecondCorner = insertPoint - arrowSize * dir - arrowSize / 6 * perp,
                ThirdCorner = insertPoint - arrowSize * dir + arrowSize / 6 * perp,
                FourthCorner = insertPoint - arrowSize * dir + arrowSize / 6 * perp,
                Color = arrowColor,
                LineWeight = multiLeader.LineWeight,
                Layer = multiLeader.Layer
            };
        }

        return new Insert(arrowBlock)
        {
            InsertPoint = insertPoint,
            Color = arrowColor,
            XScale = arrowSize,
            YScale = arrowSize,
            ZScale = arrowSize,
            Rotation = rotation,
            LineWeight = multiLeader.LineWeight,
            Normal = arrowNormal,
            Layer = multiLeader.Layer
        };
    }

    private static RenderLinePattern ResolveLinePattern(
        MultiLeader multiLeader,
        LineType? lineType,
        RenderBuildContext context)
    {
        var line = new Line
        {
            LineType = lineType ?? multiLeader.LineType ?? LineType.ByLayer,
            LineTypeScale = multiLeader.LineTypeScale,
            Layer = multiLeader.Layer ?? Layer.Default,
            Color = multiLeader.Color
        };

        return context.LinePatternResolver.ResolveLinePattern(line, context.Document, context.Settings);
    }

    private static LineType? ResolveLineType(MultiLeader multiLeader, MultiLeaderObjectContextData.LeaderLine line)
    {
        if (line.OverrideFlags.HasFlag(LeaderLinePropertOverrideFlags.LineType))
        {
            return line.LineType;
        }

        if (multiLeader.PropertyOverrideFlags.HasFlag(MultiLeaderPropertyOverrideFlags.LeaderLineType))
        {
            return multiLeader.LeaderLineType;
        }

        return multiLeader.Style?.LeaderLineType;
    }

    private static RenderColor ResolveLineColor(
        MultiLeader multiLeader,
        MultiLeaderObjectContextData.LeaderLine line,
        RenderColor fallback)
    {
        var color = ResolveLineColorValue(multiLeader, line);
        return RenderLeaderUtils.ResolveColor(color, fallback);
    }

    private static Color ResolveLineColorValue(
        MultiLeader multiLeader,
        MultiLeaderObjectContextData.LeaderLine line)
    {
        if (line.OverrideFlags.HasFlag(LeaderLinePropertOverrideFlags.LineColor))
        {
            return line.LineColor;
        }

        if (multiLeader.PropertyOverrideFlags.HasFlag(MultiLeaderPropertyOverrideFlags.LineColor))
        {
            return multiLeader.LineColor;
        }

        return multiLeader.Style?.LineColor ?? Color.ByLayer;
    }

    private static float ResolveLineWeight(
        MultiLeader multiLeader,
        MultiLeaderObjectContextData.LeaderLine line,
        float fallback,
        CadRenderSceneSettings settings)
    {
        var weight = ResolveLineWeightValue(multiLeader, line);
        return RenderLeaderUtils.ResolveLineWeight(weight, settings, fallback);
    }

    private static LineWeightType ResolveLineWeightValue(
        MultiLeader multiLeader,
        MultiLeaderObjectContextData.LeaderLine line)
    {
        if (line.OverrideFlags.HasFlag(LeaderLinePropertOverrideFlags.LineWeight))
        {
            return line.LineWeight;
        }

        if (multiLeader.PropertyOverrideFlags.HasFlag(MultiLeaderPropertyOverrideFlags.LeaderLineWeight))
        {
            return multiLeader.LeaderLineWeight;
        }

        return multiLeader.Style?.LeaderLineWeight ?? LineWeightType.ByLayer;
    }

    private static BlockRecord? ResolveArrowBlock(
        MultiLeader multiLeader,
        MultiLeaderObjectContextData.LeaderLine line)
    {
        if (line.OverrideFlags.HasFlag(LeaderLinePropertOverrideFlags.Arrowhead))
        {
            return line.Arrowhead;
        }

        if (multiLeader.PropertyOverrideFlags.HasFlag(MultiLeaderPropertyOverrideFlags.Arrowhead))
        {
            return multiLeader.Arrowhead;
        }

        return multiLeader.Style?.Arrowhead;
    }

    private static double ResolveArrowSize(
        MultiLeader multiLeader,
        MultiLeaderObjectContextData contextData,
        MultiLeaderObjectContextData.LeaderLine line)
    {
        if (line.OverrideFlags.HasFlag(LeaderLinePropertOverrideFlags.ArrowheadSize))
        {
            return line.ArrowheadSize;
        }

        if (multiLeader.PropertyOverrideFlags.HasFlag(MultiLeaderPropertyOverrideFlags.ArrowheadSize))
        {
            return multiLeader.ArrowheadSize;
        }

        if (contextData.ArrowheadSize > 0)
        {
            return contextData.ArrowheadSize;
        }

        return multiLeader.Style?.ArrowheadSize ?? 0.0;
    }

    private static void RenderContent(
        MultiLeader multiLeader,
        MultiLeaderObjectContextData contextData,
        Transform transform,
        RenderBuildContext context)
    {
        if (contextData.HasTextContents && !string.IsNullOrWhiteSpace(contextData.TextLabel))
        {
            var text = CreateTextContent(multiLeader, contextData);
            context.Dispatcher.Append(text, transform, context);
            return;
        }

        if (contextData.HasContentsBlock && contextData.BlockContent is not null)
        {
            var insert = CreateBlockContent(multiLeader, contextData);
            context.Dispatcher.Append(insert, transform, context);
        }
    }

    private static MText CreateTextContent(MultiLeader multiLeader, MultiLeaderObjectContextData contextData)
    {
        var height = contextData.TextHeight;
        if (height <= 0)
        {
            height = multiLeader.Style?.TextHeight ?? 1.0;
        }

        var text = new MText
        {
            Value = contextData.TextLabel ?? string.Empty,
            InsertPoint = contextData.TextLocation,
            Height = height,
            AlignmentPoint = new XYZ(Math.Cos(contextData.TextRotation), Math.Sin(contextData.TextRotation), 0),
            AttachmentPoint = ResolveAttachmentPoint(multiLeader, contextData),
            Color = ResolveTextColor(multiLeader, contextData),
            Layer = multiLeader.Layer,
            Style = ResolveTextStyle(multiLeader, contextData),
            Normal = ResolveTextNormal(contextData)
        };

        ApplyTextBackground(text, multiLeader, contextData);
        return text;
    }

    private static void ApplyTextBackground(MText text, MultiLeader multiLeader, MultiLeaderObjectContextData contextData)
    {
        var flags = BackgroundFillFlags.None;
        if (contextData.BackgroundFillEnabled)
        {
            flags |= BackgroundFillFlags.UseBackgroundFillColor;
            text.BackgroundColor = contextData.BackgroundFillColor;
        }

        if (contextData.BackgroundMaskFillOn)
        {
            flags |= BackgroundFillFlags.UseDrawingWindowColor;
        }

        if (multiLeader.TextFrame)
        {
            flags |= BackgroundFillFlags.TextFrame;
        }

        text.BackgroundFillFlags = flags;
    }

    private static Color ResolveTextColor(MultiLeader multiLeader, MultiLeaderObjectContextData contextData)
    {
        if (contextData.TextColor.IsByLayer || contextData.TextColor.IsByBlock)
        {
            if (multiLeader.TextColor.IsByLayer || multiLeader.TextColor.IsByBlock)
            {
                return multiLeader.Color;
            }

            return multiLeader.TextColor;
        }

        return contextData.TextColor;
    }

    private static TextStyle ResolveTextStyle(MultiLeader multiLeader, MultiLeaderObjectContextData contextData)
    {
        var style = contextData.TextStyle;
        if (style is not null)
        {
            return style;
        }

        return multiLeader.TextStyle ?? TextStyle.Default;
    }

    private static XYZ ResolveTextNormal(MultiLeaderObjectContextData contextData)
    {
        var normal = contextData.TextNormal;
        return normal.IsZero() ? XYZ.AxisZ : normal.Normalize();
    }

    private static XYZ ResolveBlockNormal(MultiLeaderObjectContextData contextData)
    {
        var normal = contextData.BlockContentNormal;
        if (normal.IsZero())
        {
            normal = contextData.TextNormal;
        }

        return normal.IsZero() ? XYZ.AxisZ : normal.Normalize();
    }

    private static AttachmentPointType ResolveAttachmentPoint(MultiLeader multiLeader, MultiLeaderObjectContextData contextData)
    {
        var horizontal = ResolveTextAttachmentPoint(multiLeader, contextData);
        var vertical = ResolveVerticalAttachment(multiLeader, contextData, horizontal);

        return ComposeAttachmentPoint(horizontal, vertical);
    }

    private static TextAttachmentPointType ResolveTextAttachmentPoint(
        MultiLeader multiLeader,
        MultiLeaderObjectContextData contextData)
    {
        var attachmentPoint = contextData.TextAttachmentPoint;
        if (IsValidTextAttachmentPoint(attachmentPoint))
        {
            return attachmentPoint;
        }

        attachmentPoint = multiLeader.TextAttachmentPoint;
        if (IsValidTextAttachmentPoint(attachmentPoint))
        {
            return attachmentPoint;
        }

        var alignment = ResolveTextAlignment(multiLeader, contextData);
        return alignment switch
        {
            TextAlignmentType.Center => TextAttachmentPointType.Center,
            TextAlignmentType.Right => TextAttachmentPointType.Right,
            _ => TextAttachmentPointType.Left
        };
    }

    private static bool IsValidTextAttachmentPoint(TextAttachmentPointType attachmentPoint)
    {
        return attachmentPoint == TextAttachmentPointType.Left
            || attachmentPoint == TextAttachmentPointType.Center
            || attachmentPoint == TextAttachmentPointType.Right;
    }

    private static TextAlignmentType ResolveTextAlignment(
        MultiLeader multiLeader,
        MultiLeaderObjectContextData contextData)
    {
        var alignment = contextData.TextAlignment;
        if (IsValidTextAlignment(alignment))
        {
            return alignment;
        }

        alignment = multiLeader.TextAlignment;
        if (IsValidTextAlignment(alignment))
        {
            return alignment;
        }

        return TextAlignmentType.Left;
    }

    private static bool IsValidTextAlignment(TextAlignmentType alignment)
    {
        return alignment == TextAlignmentType.Left
            || alignment == TextAlignmentType.Center
            || alignment == TextAlignmentType.Right;
    }

    private static VerticalAttachment ResolveVerticalAttachment(
        MultiLeader multiLeader,
        MultiLeaderObjectContextData contextData,
        TextAttachmentPointType horizontal)
    {
        var direction = ResolveTextAttachmentDirection(multiLeader, contextData);
        var attachmentType = direction == TextAttachmentDirectionType.Vertical
            ? ResolveVerticalAttachmentType(contextData)
            : ResolveHorizontalAttachmentType(contextData, horizontal);

        return MapVerticalAttachment(attachmentType);
    }

    private static TextAttachmentDirectionType ResolveTextAttachmentDirection(
        MultiLeader multiLeader,
        MultiLeaderObjectContextData contextData)
    {
        foreach (var root in contextData.LeaderRoots)
        {
            if (root.TextAttachmentDirection == TextAttachmentDirectionType.Vertical)
            {
                return TextAttachmentDirectionType.Vertical;
            }

            if (root.TextAttachmentDirection == TextAttachmentDirectionType.Horizontal)
            {
                return TextAttachmentDirectionType.Horizontal;
            }
        }

        var direction = multiLeader.TextAttachmentDirection;
        if (direction == TextAttachmentDirectionType.Horizontal || direction == TextAttachmentDirectionType.Vertical)
        {
            return direction;
        }

        return multiLeader.Style?.TextAttachmentDirection ?? TextAttachmentDirectionType.Horizontal;
    }

    private static TextAttachmentType ResolveVerticalAttachmentType(MultiLeaderObjectContextData contextData)
    {
        foreach (var root in contextData.LeaderRoots)
        {
            if (root.TextAttachmentDirection == TextAttachmentDirectionType.Vertical)
            {
                if (root.Direction.Y < 0)
                {
                    return contextData.TextBottomAttachment;
                }

                return contextData.TextTopAttachment;
            }
        }

        return contextData.TextTopAttachment;
    }

    private static TextAttachmentType ResolveHorizontalAttachmentType(
        MultiLeaderObjectContextData contextData,
        TextAttachmentPointType horizontal)
    {
        return horizontal == TextAttachmentPointType.Right
            ? contextData.TextRightAttachment
            : contextData.TextLeftAttachment;
    }

    private static VerticalAttachment MapVerticalAttachment(TextAttachmentType attachment)
    {
        return attachment switch
        {
            TextAttachmentType.TopOfTopLine => VerticalAttachment.Top,
            TextAttachmentType.MiddleOfTopLine => VerticalAttachment.Top,
            TextAttachmentType.MiddleOfText => VerticalAttachment.Middle,
            TextAttachmentType.CenterOfText => VerticalAttachment.Middle,
            TextAttachmentType.CenterOfTextOverline => VerticalAttachment.Middle,
            _ => VerticalAttachment.Bottom
        };
    }

    private static AttachmentPointType ComposeAttachmentPoint(
        TextAttachmentPointType horizontal,
        VerticalAttachment vertical)
    {
        return vertical switch
        {
            VerticalAttachment.Top => horizontal switch
            {
                TextAttachmentPointType.Center => AttachmentPointType.TopCenter,
                TextAttachmentPointType.Right => AttachmentPointType.TopRight,
                _ => AttachmentPointType.TopLeft
            },
            VerticalAttachment.Middle => horizontal switch
            {
                TextAttachmentPointType.Center => AttachmentPointType.MiddleCenter,
                TextAttachmentPointType.Right => AttachmentPointType.MiddleRight,
                _ => AttachmentPointType.MiddleLeft
            },
            _ => horizontal switch
            {
                TextAttachmentPointType.Center => AttachmentPointType.BottomCenter,
                TextAttachmentPointType.Right => AttachmentPointType.BottomRight,
                _ => AttachmentPointType.BottomLeft
            }
        };
    }

    private enum VerticalAttachment
    {
        Top,
        Middle,
        Bottom
    }

    private static Insert CreateBlockContent(MultiLeader multiLeader, MultiLeaderObjectContextData contextData)
    {
        var block = contextData.BlockContent ?? multiLeader.BlockContent;
        var scale = contextData.BlockContentScale;
        if (scale.IsZero())
        {
            scale = multiLeader.BlockContentScale;
        }

        if (scale.IsZero())
        {
            scale = new XYZ(1, 1, 1);
        }

        var rotation = contextData.BlockContentRotation;
        if (Math.Abs(rotation) < 0.0001)
        {
            rotation = multiLeader.BlockContentRotation;
        }

        return new Insert(block)
        {
            InsertPoint = contextData.BlockContentLocation,
            XScale = scale.X,
            YScale = scale.Y,
            ZScale = scale.Z,
            Rotation = rotation,
            Color = ResolveBlockColor(multiLeader, contextData),
            Layer = multiLeader.Layer,
            Normal = ResolveBlockNormal(contextData)
        };
    }

    private static Color ResolveBlockColor(MultiLeader multiLeader, MultiLeaderObjectContextData contextData)
    {
        var color = contextData.BlockContentColor;
        if (color.IsByLayer || color.IsByBlock)
        {
            color = multiLeader.BlockContentColor;
        }

        if (color.IsByLayer || color.IsByBlock)
        {
            color = multiLeader.Color;
        }

        return color;
    }
}
