using System;
using System.Collections.Generic;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using CSMath.Geometry;

namespace ACadInspector.Rendering;

internal static class DimensionPrimitiveBuilder
{
    private const double Epsilon = 1e-6;

    public static bool TryBuild(
        Dimension dimension,
        RenderBuildContext context,
        out IReadOnlyList<Entity> entities)
    {
        entities = Array.Empty<Entity>();
        if (dimension is null)
        {
            return false;
        }

        var style = ResolveDimensionStyle(dimension);
        var layer = dimension.Layer ?? Layer.Default;
        var primitives = new List<Entity>();
        var ok = dimension switch
        {
            DimensionLinear linear => BuildLinear(linear, style, layer, primitives),
            DimensionAligned aligned => BuildAligned(aligned, style, layer, primitives),
            DimensionOrdinate ordinate => BuildOrdinate(ordinate, style, layer, primitives),
            DimensionDiameter diameter => BuildDiameter(diameter, style, layer, primitives),
            DimensionRadius radius => BuildRadius(radius, style, layer, primitives),
            DimensionAngular2Line angular2 => BuildAngular2Line(angular2, style, layer, primitives),
            DimensionAngular3Pt angular3 => BuildAngular3Pt(angular3, style, layer, primitives),
            _ => false
        };

        if (!ok || primitives.Count == 0)
        {
            return false;
        }

        entities = primitives;
        return true;
    }

    private static bool BuildLinear(
        DimensionLinear dimension,
        DimensionStyle style,
        Layer layer,
        List<Entity> primitives)
    {
        var dir = dimension.SecondPoint - dimension.FirstPoint;
        if (dir.IsZero())
        {
            return false;
        }

        var transform = Transform.CreateRotation(dimension.Normal, dimension.Rotation);
        var yVec = transform.ApplyTransform(XYZ.AxisY).Normalize();
        var xVec = transform.ApplyTransform(XYZ.AxisX).Normalize();

        var line1 = new Line3D(dimension.FirstPoint, yVec);
        var line2 = new Line3D(dimension.DefinitionPoint, xVec);
        var dimRef1 = line1.FindIntersection(line2);
        if (dimRef1.IsNaN())
        {
            return false;
        }

        var dimRef2 = dimension.DefinitionPoint;
        var dimDir = (dimRef2 - dimRef1).Normalize();
        if (dimDir.IsZero())
        {
            return false;
        }

        if (!style.SuppressFirstDimensionLine && !style.SuppressSecondDimensionLine)
        {
            primitives.Add(CreateDimensionLine(dimRef1, dimRef2, style, layer));
            primitives.Add(CreateDimensionArrow(dimRef1, ApplyArrowFlip(dimension.FlipArrow1, -dimDir), style, layer, dimension.Normal, ResolveArrow1(style)));
            primitives.Add(CreateDimensionArrow(dimRef2, ApplyArrowFlip(dimension.FlipArrow2, dimDir), style, layer, dimension.Normal, ResolveArrow2(style)));
        }

        var dirRef1 = (dimRef1 - dimension.FirstPoint).Normalize();
        var dirRef2 = (dimRef2 - dimension.SecondPoint).Normalize();
        var dimexo = style.ExtensionLineOffset * style.ScaleFactor;
        var dimexe = style.ExtensionLineExtension * style.ScaleFactor;

        if (!style.SuppressFirstExtensionLine)
        {
            primitives.Add(CreateExtensionLine(
                dimension.FirstPoint + dimexo * dirRef1,
                dimRef1 + dimexe * dirRef1,
                style,
                style.LineTypeExt1,
                layer));
        }

        if (!style.SuppressSecondExtensionLine)
        {
            primitives.Add(CreateExtensionLine(
                dimension.SecondPoint + dimexo * dirRef2,
                dimRef2 + dimexe * dirRef2,
                style,
                style.LineTypeExt2,
                layer));
        }

        var textRef = dimRef1.Mid(dimRef2);
        var gap = style.DimensionLineGap * style.ScaleFactor;
        var textRot = ResolveTextRotation(dimension.TextRotation, dimension.Rotation);
        if (textRot > MathHelper.HalfPI && textRot <= MathHelper.ThreeHalfPI)
        {
            gap = -gap;
            textRot += Math.PI;
        }

        var text = dimension.GetMeasurementText(style);
        var textPos = dimension.IsTextUserDefinedLocation
            ? dimension.TextMiddlePoint
            : (textRef + gap * yVec).Convert<XYZ>();
        primitives.Add(CreateDimensionText(dimension, style, layer, textPos, text, AttachmentPointType.MiddleCenter, textRot));

        return true;
    }

    private static bool BuildAligned(
        DimensionAligned dimension,
        DimensionStyle style,
        Layer layer,
        List<Entity> primitives)
    {
        var dir = dimension.SecondPoint - dimension.FirstPoint;
        if (dir.IsZero())
        {
            return false;
        }

        var dimDir = dir.Normalize();
        var vec = XYZ.Cross(dimension.Normal, dimDir).Normalize();
        if (vec.IsZero())
        {
            return false;
        }

        var offset = dimension.Offset;
        var dimRef1 = dimension.FirstPoint + vec * offset;
        var dimRef2 = dimension.DefinitionPoint;
        if ((dimRef2 - dimRef1).IsZero())
        {
            return false;
        }

        if (!style.SuppressFirstDimensionLine && !style.SuppressSecondDimensionLine)
        {
            primitives.Add(CreateDimensionLine(dimRef1, dimRef2, style, layer));
            primitives.Add(CreateDimensionArrow(dimRef1, ApplyArrowFlip(dimension.FlipArrow1, -dimDir), style, layer, dimension.Normal, ResolveArrow1(style)));
            primitives.Add(CreateDimensionArrow(dimRef2, ApplyArrowFlip(dimension.FlipArrow2, dimDir), style, layer, dimension.Normal, ResolveArrow2(style)));
        }

        var sign = Math.Sign(offset);
        if (sign == 0)
        {
            sign = 1;
        }

        var thisexo = sign * style.ExtensionLineOffset * style.ScaleFactor;
        var thisexe = sign * style.ExtensionLineExtension * style.ScaleFactor;
        if (!style.SuppressFirstExtensionLine)
        {
            primitives.Add(CreateExtensionLine(
                dimension.FirstPoint + thisexo * vec,
                dimRef1 + thisexe * vec,
                style,
                style.LineTypeExt1,
                layer));
        }

        if (!style.SuppressSecondExtensionLine)
        {
            primitives.Add(CreateExtensionLine(
                dimension.SecondPoint + thisexo * vec,
                dimRef2 + thisexe * vec,
                style,
                style.LineTypeExt2,
                layer));
        }

        var textRef = dimRef1.Mid(dimRef2);
        var gap = style.DimensionLineGap * style.ScaleFactor;
        var textRot = ResolveTextRotation(dimension.TextRotation, dimension.ExtLineRotation);
        if (textRot > Math.PI / 2 && textRot <= (3 * Math.PI * 0.5))
        {
            gap = -gap;
            textRot += Math.PI;
        }

        var text = dimension.GetMeasurementText(style);
        var textPos = dimension.IsTextUserDefinedLocation
            ? dimension.TextMiddlePoint
            : (textRef + gap * vec).Convert<XYZ>();
        primitives.Add(CreateDimensionText(dimension, style, layer, textPos, text, AttachmentPointType.MiddleCenter, textRot));
        return true;
    }

    private static bool BuildOrdinate(
        DimensionOrdinate dimension,
        DimensionStyle style,
        Layer layer,
        List<Entity> primitives)
    {
        var ref1 = dimension.FeatureLocation.Convert<XY>();
        var ref2 = dimension.LeaderEndpoint.Convert<XY>();
        var refDim = ref2 - ref1;
        if (refDim.IsZero())
        {
            return false;
        }

        var rotation = dimension.HorizontalDirection;
        if (dimension.IsOrdinateTypeX)
        {
            rotation += MathHelper.HalfPI;
        }

        var ocsDimRef = XY.Rotate(refDim, -rotation);
        var minOffset = 2 * style.ArrowSize;
        XY pto1;
        XY pto2;
        var side = 1;

        if (ocsDimRef.X >= 0)
        {
            if (ocsDimRef.X >= 2 * minOffset)
            {
                pto1 = new XY(ocsDimRef.X - minOffset, 0);
                pto2 = new XY(ocsDimRef.X - minOffset, ocsDimRef.Y);
            }
            else
            {
                pto1 = new XY(minOffset, 0);
                pto2 = new XY(ocsDimRef.X - minOffset, ocsDimRef.Y);
            }
        }
        else
        {
            if (ocsDimRef.X <= -2 * minOffset)
            {
                pto1 = new XY(ocsDimRef.X + minOffset, 0);
                pto2 = new XY(ocsDimRef.X + minOffset, ocsDimRef.Y);
            }
            else
            {
                pto1 = new XY(-minOffset, 0);
                pto2 = new XY(ocsDimRef.X + minOffset, ocsDimRef.Y);
            }

            side = -1;
        }

        pto1 = ref1 + XY.Rotate(pto1, rotation);
        pto2 = ref1 + XY.Rotate(pto2, rotation);

        var offset = style.ExtensionLineOffset * style.ScaleFactor;
        primitives.Add(CreateDimensionLine(
            XY.Polar(ref1, offset, rotation).Convert<XYZ>(),
            pto1.Convert<XYZ>(),
            style,
            layer));
        primitives.Add(CreateDimensionLine(pto1.Convert<XYZ>(), pto2.Convert<XYZ>(), style, layer));
        primitives.Add(CreateDimensionLine(pto2.Convert<XYZ>(), ref2.Convert<XYZ>(), style, layer));

        var text = dimension.GetMeasurementText(style);
        var midText = XY.Polar(ref2, side * style.DimensionLineGap * style.ScaleFactor, rotation);
        var textPos = dimension.IsTextUserDefinedLocation
            ? dimension.TextMiddlePoint
            : midText.Convert<XYZ>();
        var attachment = side < 0 ? AttachmentPointType.MiddleRight : AttachmentPointType.MiddleLeft;
        primitives.Add(CreateDimensionText(dimension, style, layer, textPos, text, attachment, rotation));

        return true;
    }

    private static bool BuildRadius(
        DimensionRadius dimension,
        DimensionStyle style,
        Layer layer,
        List<Entity> primitives)
    {
        var centerRef = dimension.DefinitionPoint.Convert<XY>();
        var ref1 = dimension.AngleVertex.Convert<XY>();
        var radius = dimension.Measurement;
        if (MathHelper.IsZero(radius))
        {
            return false;
        }

        var minOffset = 2 * style.ArrowSize * style.ScaleFactor;
        BuildRadial(dimension, style, layer, primitives, centerRef, ref1, radius, minOffset, drawRef2: false);
        return true;
    }

    private static bool BuildDiameter(
        DimensionDiameter dimension,
        DimensionStyle style,
        Layer layer,
        List<Entity> primitives)
    {
        var radius = dimension.Measurement * 0.5;
        if (MathHelper.IsZero(radius))
        {
            return false;
        }

        var centerRef = dimension.Center.Convert<XY>();
        var ref1 = dimension.AngleVertex.Convert<XY>();
        var minOffset = (2 * style.ArrowSize + style.DimensionLineGap) * style.ScaleFactor;
        BuildRadial(dimension, style, layer, primitives, centerRef, ref1, radius, minOffset, drawRef2: true);
        return true;
    }

    private static bool BuildAngular2Line(
        DimensionAngular2Line dimension,
        DimensionStyle style,
        Layer layer,
        List<Entity> primitives)
    {
        var center = dimension.AngleVertex;
        var dir1 = (dimension.FirstPoint - center).Normalize();
        var dir2 = (dimension.SecondPoint - center).Normalize();
        if (dir1.IsZero() || dir2.IsZero())
        {
            return false;
        }

        var radius = ResolveAngularRadius(center, dimension.DimensionArc, dimension.DefinitionPoint);
        if (radius <= Epsilon)
        {
            return false;
        }

        var angle1 = Math.Atan2(dir1.Y, dir1.X);
        var angle2 = Math.Atan2(dir2.Y, dir2.X);
        var arcAngle = ResolveAngularReference(center, dimension.DimensionArc);
        if (!IsAngleBetween(angle1, angle2, arcAngle))
        {
            (angle1, angle2) = (angle2, angle1);
            (dir1, dir2) = (dir2, dir1);
        }

        var arcStart = center + dir1 * radius;
        var arcEnd = center + dir2 * radius;

        if (!style.SuppressFirstExtensionLine)
        {
            primitives.Add(CreateExtensionLine(dimension.FirstPoint, arcStart, style, style.LineTypeExt1, layer));
        }

        if (!style.SuppressSecondExtensionLine)
        {
            primitives.Add(CreateExtensionLine(dimension.SecondPoint, arcEnd, style, style.LineTypeExt2, layer));
        }

        if (!style.SuppressFirstDimensionLine && !style.SuppressSecondDimensionLine)
        {
            primitives.Add(CreateArc(center, radius, angle1, angle2, style, layer));
            var tangent1 = new XYZ(-dir1.Y, dir1.X, 0);
            var tangent2 = new XYZ(-dir2.Y, dir2.X, 0);
            primitives.Add(CreateDimensionArrow(arcStart, ApplyArrowFlip(dimension.FlipArrow1, tangent1), style, layer, dimension.Normal, ResolveArrow1(style)));
            primitives.Add(CreateDimensionArrow(arcEnd, ApplyArrowFlip(dimension.FlipArrow2, -tangent2), style, layer, dimension.Normal, ResolveArrow2(style)));
        }

        var text = dimension.GetMeasurementText(style);
        var textRot = ResolveTextRotation(dimension.TextRotation, (angle1 + angle2) * 0.5);
        var textPos = ResolveAngularTextPosition(dimension, style, center, radius, angle1, angle2);
        primitives.Add(CreateDimensionText(dimension, style, layer, textPos, text, AttachmentPointType.MiddleCenter, textRot));

        return true;
    }

    private static bool BuildAngular3Pt(
        DimensionAngular3Pt dimension,
        DimensionStyle style,
        Layer layer,
        List<Entity> primitives)
    {
        var center = dimension.AngleVertex;
        var dir1 = (dimension.FirstPoint - center).Normalize();
        var dir2 = (dimension.SecondPoint - center).Normalize();
        if (dir1.IsZero() || dir2.IsZero())
        {
            return false;
        }

        var radius = ResolveAngularRadius(center, dimension.DefinitionPoint, default);
        if (radius <= Epsilon)
        {
            radius = dimension.FirstPoint.DistanceFrom(center);
        }

        if (radius <= Epsilon)
        {
            return false;
        }

        var angle1 = Math.Atan2(dir1.Y, dir1.X);
        var angle2 = Math.Atan2(dir2.Y, dir2.X);
        var arcAngle = ResolveAngularReference(center, dimension.DefinitionPoint);
        if (!IsAngleBetween(angle1, angle2, arcAngle))
        {
            (angle1, angle2) = (angle2, angle1);
            (dir1, dir2) = (dir2, dir1);
        }

        var arcStart = center + dir1 * radius;
        var arcEnd = center + dir2 * radius;

        if (!style.SuppressFirstExtensionLine)
        {
            primitives.Add(CreateExtensionLine(dimension.FirstPoint, arcStart, style, style.LineTypeExt1, layer));
        }

        if (!style.SuppressSecondExtensionLine)
        {
            primitives.Add(CreateExtensionLine(dimension.SecondPoint, arcEnd, style, style.LineTypeExt2, layer));
        }

        if (!style.SuppressFirstDimensionLine && !style.SuppressSecondDimensionLine)
        {
            primitives.Add(CreateArc(center, radius, angle1, angle2, style, layer));
            var tangent1 = new XYZ(-dir1.Y, dir1.X, 0);
            var tangent2 = new XYZ(-dir2.Y, dir2.X, 0);
            primitives.Add(CreateDimensionArrow(arcStart, ApplyArrowFlip(dimension.FlipArrow1, tangent1), style, layer, dimension.Normal, ResolveArrow1(style)));
            primitives.Add(CreateDimensionArrow(arcEnd, ApplyArrowFlip(dimension.FlipArrow2, -tangent2), style, layer, dimension.Normal, ResolveArrow2(style)));
        }

        var text = dimension.GetMeasurementText(style);
        var textRot = ResolveTextRotation(dimension.TextRotation, (angle1 + angle2) * 0.5);
        var textPos = ResolveAngularTextPosition(dimension, style, center, radius, angle1, angle2);
        primitives.Add(CreateDimensionText(dimension, style, layer, textPos, text, AttachmentPointType.MiddleCenter, textRot));

        return true;
    }

    private static void BuildRadial(
        Dimension dimension,
        DimensionStyle style,
        Layer layer,
        List<Entity> primitives,
        XY centerRef,
        XY ref1,
        double radius,
        double minOffset,
        bool drawRef2)
    {
        var textPos = dimension.TextMiddlePoint;
        var offset = centerRef.DistanceFrom(textPos.Convert<XY>());
        var defPoint = centerRef;
        var angleRef = defPoint.GetAngle(ref1);

        short inside;
        if (offset >= radius && offset <= radius + minOffset)
        {
            offset = radius + minOffset;
            inside = -1;
        }
        else if (offset >= radius - minOffset && offset <= radius)
        {
            offset = radius - minOffset;
            inside = 1;
        }
        else if (offset > radius)
        {
            inside = -1;
        }
        else
        {
            inside = 1;
        }

        var dimRef = XY.Polar(defPoint, offset - style.DimensionLineGap * style.ScaleFactor, angleRef);

        if (!style.SuppressFirstDimensionLine && !style.SuppressSecondDimensionLine)
        {
            primitives.Add(CreateRadialLine(dimRef, ref1, angleRef, inside, style, layer));

            if (drawRef2 && inside < 0)
            {
                var dimRef2 = XY.Polar(centerRef, radius + minOffset - style.DimensionLineGap * style.ScaleFactor, Math.PI + angleRef);
                primitives.Add(CreateRadialLine(dimRef2, defPoint, Math.PI + angleRef, inside, style, layer));
            }
        }

        if (!MathHelper.IsZero(style.CenterMarkSize))
        {
            primitives.AddRange(CreateCenterCross(centerRef.Convert<XYZ>(), radius, style, layer));
        }

        var text = dimension.GetMeasurementText(style);
        var textRot = angleRef;
        short reverse = 1;
        if (textRot > MathHelper.HalfPI && textRot <= MathHelper.ThreeHalfPI)
        {
            textRot += Math.PI;
            reverse = -1;
        }

        if (!dimension.IsTextUserDefinedLocation)
        {
            var newTextPos = XY.Polar(dimRef, -reverse * inside * style.DimensionLineGap * style.ScaleFactor, textRot);
            textPos = newTextPos.Convert<XYZ>();
        }

        var attachment = reverse * inside < 0 ? AttachmentPointType.MiddleLeft : AttachmentPointType.MiddleRight;
        primitives.Add(CreateDimensionText(dimension, style, layer, textPos, text, attachment, textRot));
    }

    private static Line CreateDimensionLine(XYZ start, XYZ end, DimensionStyle style, Layer layer)
    {
        return new Line(start, end)
        {
            Color = style.DimensionLineColor,
            LineType = style.LineType ?? LineType.ByLayer,
            LineWeight = style.DimensionLineWeight,
            Layer = layer
        };
    }

    private static Line CreateExtensionLine(
        XYZ start,
        XYZ end,
        DimensionStyle style,
        LineType lineType,
        Layer layer)
    {
        return new Line(start, end)
        {
            Color = style.ExtensionLineColor,
            LineType = lineType ?? LineType.ByLayer,
            LineWeight = style.ExtensionLineWeight,
            Layer = layer
        };
    }

    private static Entity CreateDimensionArrow(
        XYZ insertPoint,
        XYZ dir,
        DimensionStyle style,
        Layer layer,
        XYZ normal,
        BlockRecord arrowBlock)
    {
        var scale = style.ArrowSize * style.ScaleFactor;
        var rotation = Math.Atan2(dir.Y, dir.X);

        if (arrowBlock is null)
        {
            var perp = XYZ.Cross(normal, dir).Normalize();
            var arrow = new Solid
            {
                FirstCorner = insertPoint,
                SecondCorner = insertPoint - scale * dir - scale / 6 * perp,
                ThirdCorner = insertPoint - scale * dir + scale / 6 * perp,
                FourthCorner = insertPoint - scale * dir + scale / 6 * perp,
                Color = style.DimensionLineColor,
                LineWeight = style.DimensionLineWeight,
                Layer = layer
            };
            return arrow;
        }

        return new Insert(arrowBlock)
        {
            InsertPoint = insertPoint,
            Color = style.DimensionLineColor,
            XScale = scale,
            YScale = scale,
            ZScale = scale,
            Rotation = rotation,
            LineWeight = style.DimensionLineWeight,
            Normal = normal,
            Layer = layer
        };
    }

    private static Line CreateRadialLine(XY start, XY end, double rotation, short reversed, DimensionStyle style, Layer layer)
    {
        var ext = -style.ArrowSize * style.ScaleFactor;
        var endRef = XY.Polar(end, reversed * ext, rotation);
        return CreateDimensionLine(start.Convert<XYZ>(), endRef.Convert<XYZ>(), style, layer);
    }

    private static IEnumerable<Entity> CreateCenterCross(XYZ center, double radius, DimensionStyle style, Layer layer)
    {
        var lines = new List<Entity>();
        if (MathHelper.IsZero(style.CenterMarkSize))
        {
            return lines;
        }

        var dist = Math.Abs(style.CenterMarkSize * style.ScaleFactor);
        var c1 = new XYZ(0.0, -dist, 0) + center;
        var c2 = new XYZ(0.0, dist, 0) + center;
        lines.Add(new Line(c1, c2)
        {
            Color = style.ExtensionLineColor,
            LineWeight = style.ExtensionLineWeight,
            Layer = layer
        });
        c1 = new XYZ(-dist, 0.0, 0) + center;
        c2 = new XYZ(dist, 0.0, 0) + center;
        lines.Add(new Line(c1, c2)
        {
            Color = style.ExtensionLineColor,
            LineWeight = style.ExtensionLineWeight,
            Layer = layer
        });

        if (style.CenterMarkSize < 0)
        {
            c1 = new XYZ(2 * dist, 0.0, 0) + center;
            c2 = new XYZ(radius + dist, 0.0, 0) + center;
            lines.Add(new Line(c1, c2) { Color = style.ExtensionLineColor, LineWeight = style.ExtensionLineWeight, Layer = layer });

            c1 = new XYZ(-2 * dist, 0.0, 0) + center;
            c2 = new XYZ(-radius - dist, 0.0, 0) + center;
            lines.Add(new Line(c1, c2) { Color = style.ExtensionLineColor, LineWeight = style.ExtensionLineWeight, Layer = layer });

            c1 = new XYZ(0.0, 2 * dist, 0) + center;
            c2 = new XYZ(0.0, radius + dist, 0) + center;
            lines.Add(new Line(c1, c2) { Color = style.ExtensionLineColor, LineWeight = style.ExtensionLineWeight, Layer = layer });

            c1 = new XYZ(0.0, -2 * dist, 0) + center;
            c2 = new XYZ(0.0, -radius - dist, 0) + center;
            lines.Add(new Line(c1, c2) { Color = style.ExtensionLineColor, LineWeight = style.ExtensionLineWeight, Layer = layer });
        }

        return lines;
    }

    private static Arc CreateArc(XYZ center, double radius, double start, double end, DimensionStyle style, Layer layer)
    {
        return new Arc(center, radius, start, end)
        {
            Color = style.DimensionLineColor,
            LineType = style.LineType ?? LineType.ByLayer,
            LineWeight = style.DimensionLineWeight,
            Layer = layer
        };
    }

    private static MText CreateDimensionText(
        Dimension dimension,
        DimensionStyle style,
        Layer layer,
        XYZ position,
        string text,
        AttachmentPointType attachment,
        double rotation)
    {
        var height = style.TextHeight * style.ScaleFactor;
        if (height <= 0)
        {
            height = style.TextHeight;
        }

        var mText = new MText
        {
            Value = text,
            AttachmentPoint = attachment,
            InsertPoint = position,
            Height = height,
            AlignmentPoint = new XYZ(Math.Cos(rotation), Math.Sin(rotation), 0),
            Color = style.TextColor,
            Layer = layer,
            Style = style.Style
        };

        ApplyTextBackground(mText, style);
        return mText;
    }

    private static void ApplyTextBackground(MText text, DimensionStyle style)
    {
        var flags = BackgroundFillFlags.None;
        if (style.TextBackgroundFillMode == DimensionTextBackgroundFillMode.DrawingBackgroundColor)
        {
            flags |= BackgroundFillFlags.UseDrawingWindowColor;
        }
        else if (style.TextBackgroundFillMode == DimensionTextBackgroundFillMode.DimensionTextBackgroundColor)
        {
            flags |= BackgroundFillFlags.UseBackgroundFillColor;
            text.BackgroundColor = style.TextBackgroundColor;
        }

        if (style.DimensionLineGap < 0)
        {
            flags |= BackgroundFillFlags.TextFrame;
        }

        text.BackgroundFillFlags = flags;
    }

    private static BlockRecord ResolveArrow1(DimensionStyle style)
    {
        if (!style.SeparateArrowBlocks)
        {
            return style.ArrowBlock;
        }

        return style.DimArrow1 ?? style.ArrowBlock;
    }

    private static BlockRecord ResolveArrow2(DimensionStyle style)
    {
        if (!style.SeparateArrowBlocks)
        {
            return style.ArrowBlock;
        }

        return style.DimArrow2 ?? style.ArrowBlock;
    }

    private static XYZ ApplyArrowFlip(bool flip, XYZ dir)
    {
        return flip ? -dir : dir;
    }

    private static double ResolveTextRotation(double explicitRotation, double fallbackRotation)
    {
        if (Math.Abs(explicitRotation) > Epsilon)
        {
            return explicitRotation;
        }

        return fallbackRotation;
    }

    private static double ResolveAngularRadius(XYZ center, XYZ arcPoint, XYZ fallbackPoint)
    {
        if (!arcPoint.IsZero())
        {
            return center.DistanceFrom(arcPoint);
        }

        if (!fallbackPoint.IsZero())
        {
            return center.DistanceFrom(fallbackPoint);
        }

        return 0.0;
    }

    private static double ResolveAngularReference(XYZ center, XYZ arcPoint)
    {
        if (!arcPoint.IsZero())
        {
            return Math.Atan2(arcPoint.Y - center.Y, arcPoint.X - center.X);
        }

        return 0.0;
    }

    private static XYZ ResolveAngularTextPosition(
        Dimension dimension,
        DimensionStyle style,
        XYZ center,
        double radius,
        double startAngle,
        double endAngle)
    {
        if (dimension.IsTextUserDefinedLocation)
        {
            return dimension.TextMiddlePoint;
        }

        var mid = NormalizeAngle((startAngle + endAngle) * 0.5);
        var gap = style.DimensionLineGap * style.ScaleFactor;
        var offset = radius + gap;
        return XY.Polar(center.Convert<XY>(), offset, mid).Convert<XYZ>();
    }

    private static bool IsAngleBetween(double start, double end, double angle)
    {
        start = NormalizeAngle(start);
        end = NormalizeAngle(end);
        angle = NormalizeAngle(angle);

        if (start <= end)
        {
            return angle >= start && angle <= end;
        }

        return angle >= start || angle <= end;
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= MathHelper.TwoPI;
        if (angle < 0)
        {
            angle += MathHelper.TwoPI;
        }

        return angle;
    }

    private static DimensionStyle ResolveDimensionStyle(Dimension dimension)
    {
        if (!dimension.HasStyleOverride)
        {
            return dimension.Style;
        }

        var map = dimension.GetStyleOverrideMap();
        if (map is null)
        {
            return dimension.Style;
        }

        var resolved = (DimensionStyle)dimension.Style.Clone();
        resolved.Name = "override";

        foreach (var entry in map.DxfProperties)
        {
            ApplyStyleOverride(resolved, entry.Key, entry.Value.StoredValue);
        }

        return resolved;
    }

    private static void ApplyStyleOverride(DimensionStyle style, int code, object? value)
    {
        if (value is null)
        {
            return;
        }

        switch (code)
        {
            case 40:
                style.ScaleFactor = ResolveDouble(value, style.ScaleFactor);
                break;
            case 41:
                style.ArrowSize = ResolveDouble(value, style.ArrowSize);
                break;
            case 42:
                style.ExtensionLineOffset = ResolveDouble(value, style.ExtensionLineOffset);
                break;
            case 43:
                style.DimensionLineIncrement = ResolveDouble(value, style.DimensionLineIncrement);
                break;
            case 44:
                style.ExtensionLineExtension = ResolveDouble(value, style.ExtensionLineExtension);
                break;
            case 46:
                style.DimensionLineExtension = ResolveDouble(value, style.DimensionLineExtension);
                break;
            case 140:
                style.TextHeight = ResolveDouble(value, style.TextHeight);
                break;
            case 141:
                style.CenterMarkSize = ResolveDouble(value, style.CenterMarkSize);
                break;
            case 176:
                style.DimensionLineColor = ResolveColor(value, style.DimensionLineColor);
                break;
            case 177:
                style.ExtensionLineColor = ResolveColor(value, style.ExtensionLineColor);
                break;
            case 178:
                style.TextColor = ResolveColor(value, style.TextColor);
                break;
            case 371:
                style.DimensionLineWeight = ResolveLineWeight(value, style.DimensionLineWeight);
                break;
            case 372:
                style.ExtensionLineWeight = ResolveLineWeight(value, style.ExtensionLineWeight);
                break;
        }
    }

    private static double ResolveDouble(object value, double fallback)
    {
        return value switch
        {
            double d => d,
            float f => f,
            int i => i,
            short s => s,
            _ => fallback
        };
    }

    private static Color ResolveColor(object value, Color fallback)
    {
        return value switch
        {
            Color color => color,
            short index => new Color(index),
            int index => new Color((short)index),
            byte index => new Color((short)index),
            _ => fallback
        };
    }

    private static LineWeightType ResolveLineWeight(object value, LineWeightType fallback)
    {
        return value switch
        {
            LineWeightType weight => weight,
            short weight => (LineWeightType)weight,
            int weight => (LineWeightType)weight,
            _ => fallback
        };
    }
}
