using System;
using System.Collections.Generic;
using System.Numerics;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class ShapeRenderHandler : IRenderEntityHandler
{
    private const float Epsilon = 0.000001f;

    public bool CanHandle(Entity entity) => entity is Shape;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var shape = (Shape)entity;
        var style = shape.ShapeStyle;
        var shapeFile = style?.Filename;
        if (string.IsNullOrWhiteSpace(shapeFile))
        {
            return;
        }

        if (shape.ShapeNumber == 0)
        {
            return;
        }

        if (!context.ShapeResolver.TryResolveShape(shapeFile, (short)shape.ShapeNumber, context.Settings, out var geometry))
        {
            return;
        }

        var builder = context.GetLayerBuilder(shape);
        var color = context.ResolveEntityColor(shape);
        var thickness = context.ResolveLineWeight(shape);
        var lineCap = context.ResolveLineCap(shape);
        var lineJoin = context.ResolveLineJoin(shape);

        var rotation = (float)shape.Rotation;
        var oblique = (float)shape.ObliqueAngle;
        var relativeX = (float)shape.RelativeXScale;
        if (relativeX <= Epsilon)
        {
            relativeX = 1f;
        }

        var nominalHeight = geometry.Bounds.Size.Y;
        if (nominalHeight <= Epsilon)
        {
            nominalHeight = 1f;
        }

        var height = (float)shape.Size;
        if (height <= Epsilon)
        {
            height = 1f;
        }

        var scale = height / nominalHeight;
        var scaleX = scale * relativeX;
        var scaleY = scale;
        var shear = MathF.Tan(oblique);
        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);

        var shapeTransform = RenderTransformUtils.CombineWithNormal(transform, shape.Normal);
        var insertion = shape.InsertionPoint;

        foreach (var contour in geometry.Contours)
        {
            if (contour.Count == 0)
            {
                continue;
            }

            var points = new List<Vector2>(contour.Count);
            foreach (var point in contour)
            {
                var x = point.X * scaleX;
                var y = point.Y * scaleY;
                x += y * shear;

                var rx = x * cos - y * sin;
                var ry = x * sin + y * cos;

                var world = new XYZ(insertion.X + rx, insertion.Y + ry, insertion.Z);
                points.Add(RenderTransformUtils.Apply(shapeTransform, world));
            }

            if (points.Count == 1)
            {
                builder.Add(new RenderPoint(
                    points[0],
                    color,
                    thickness,
                    lineCap,
                    lineJoin,
                    context.Settings.PointDisplayMode,
                    context.Settings.PointDisplaySize));
            }
            else if (points.Count == 2)
            {
                builder.Add(new RenderLine(points[0], points[1], color, thickness, lineCap, lineJoin));
            }
            else
            {
                builder.Add(new RenderPolyline(points, isClosed: false, color, thickness, lineCap, lineJoin));
            }
        }
    }
}
