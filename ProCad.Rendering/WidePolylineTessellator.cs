using System;
using System.Collections.Generic;
using System.Numerics;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Rendering;

internal static class WidePolylineTessellator
{
    private const float WidthEpsilon = 0.0001f;
    private const float PositionEpsilon = 0.000001f;

    public static bool TryAddWidePolylineFill(
        RenderLayerBuilder builder,
        IPolyline polyline,
        Transform transform,
        RenderColor color,
        int precision)
    {
        if (!TryResolveWidthDefaults(polyline, out var defaultStart, out var defaultEnd, out var constantWidth))
        {
            return false;
        }

        var vertices = ToVertexList(polyline.Vertices);
        if (vertices.Count < 2)
        {
            return false;
        }

        var hasWideSegments = false;
        var isClosed = polyline.IsClosed;
        for (var i = 0; i < vertices.Count; i++)
        {
            var curr = vertices[i];
            var next = i + 1 < vertices.Count ? vertices[i + 1] : (isClosed ? vertices[0] : null);
            if (next is null)
            {
                break;
            }

            ResolveSegmentWidths(curr, defaultStart, defaultEnd, constantWidth, out var startWidth, out var endWidth);
            if (startWidth <= WidthEpsilon && endWidth <= WidthEpsilon)
            {
                continue;
            }

            hasWideSegments = true;
            AddSegmentFill(builder, curr, next, startWidth, endWidth, precision, transform, color);
        }

        return hasWideSegments;
    }

    private static void AddSegmentFill(
        RenderLayerBuilder builder,
        IVertex startVertex,
        IVertex endVertex,
        float startWidth,
        float endWidth,
        int precision,
        Transform transform,
        RenderColor color)
    {
        var bulge = startVertex.Bulge;
        if (Math.Abs(bulge) <= double.Epsilon)
        {
            var start = startVertex.Location.Convert<XYZ>();
            var end = endVertex.Location.Convert<XYZ>();
            AddSegmentQuads(builder, start, end, startWidth, endWidth, transform, color);
            return;
        }

        var p1 = startVertex.Location.Convert<XY>();
        var p2 = endVertex.Location.Convert<XY>();
        var arc = Arc.CreateFromBulge(p1, p2, bulge);
        var points = arc.PolygonalVertexes(Math.Max(precision, 2));
        if (points.Count < 2)
        {
            return;
        }

        var startPoint = new XYZ(p1.X, p1.Y, 0);
        if (!IsClose(points[0], startPoint))
        {
            if (IsClose(points[^1], startPoint))
            {
                points.Reverse();
            }
        }

        var count = points.Count;
        for (var i = 0; i < count - 1; i++)
        {
            var t0 = (float)i / (count - 1);
            var t1 = (float)(i + 1) / (count - 1);
            var width0 = Lerp(startWidth, endWidth, t0);
            var width1 = Lerp(startWidth, endWidth, t1);
            AddSegmentQuads(builder, points[i], points[i + 1], width0, width1, transform, color);
        }
    }

    private static void AddSegmentQuads(
        RenderLayerBuilder builder,
        XYZ start,
        XYZ end,
        float startWidth,
        float endWidth,
        Transform transform,
        RenderColor color)
    {
        if (startWidth <= WidthEpsilon && endWidth <= WidthEpsilon)
        {
            return;
        }

        var dx = (float)(end.X - start.X);
        var dy = (float)(end.Y - start.Y);
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len <= WidthEpsilon)
        {
            return;
        }

        var invLen = 1f / len;
        var nx = -dy * invLen;
        var ny = dx * invLen;

        var half0 = startWidth * 0.5f;
        var half1 = endWidth * 0.5f;

        var left0 = new XYZ(start.X + nx * half0, start.Y + ny * half0, start.Z);
        var right0 = new XYZ(start.X - nx * half0, start.Y - ny * half0, start.Z);
        var left1 = new XYZ(end.X + nx * half1, end.Y + ny * half1, end.Z);
        var right1 = new XYZ(end.X - nx * half1, end.Y - ny * half1, end.Z);

        var quad = new List<Vector2>(4)
        {
            RenderTransformUtils.Apply(transform, left0),
            RenderTransformUtils.Apply(transform, left1),
            RenderTransformUtils.Apply(transform, right1),
            RenderTransformUtils.Apply(transform, right0)
        };

        builder.Add(new RenderFill(quad, color));
    }

    private static bool TryResolveWidthDefaults(
        IPolyline polyline,
        out float defaultStart,
        out float defaultEnd,
        out float constantWidth)
    {
        defaultStart = 0f;
        defaultEnd = 0f;
        constantWidth = 0f;

        switch (polyline)
        {
            case LwPolyline lw:
                constantWidth = (float)lw.ConstantWidth;
                return constantWidth > WidthEpsilon || HasVertexWidths(lw.Vertices);
            case Polyline<Vertex2D> poly2d:
                defaultStart = (float)poly2d.StartWidth;
                defaultEnd = (float)poly2d.EndWidth;
                return defaultStart > WidthEpsilon || defaultEnd > WidthEpsilon || HasVertexWidths(poly2d.Vertices);
            case Polyline<Vertex3D> poly3d:
                defaultStart = (float)poly3d.StartWidth;
                defaultEnd = (float)poly3d.EndWidth;
                return defaultStart > WidthEpsilon || defaultEnd > WidthEpsilon || HasVertexWidths(poly3d.Vertices);
            default:
                return false;
        }
    }

    private static void ResolveSegmentWidths(
        IVertex vertex,
        float defaultStart,
        float defaultEnd,
        float constantWidth,
        out float startWidth,
        out float endWidth)
    {
        switch (vertex)
        {
            case LwPolyline.Vertex lwVertex:
                startWidth = (float)lwVertex.StartWidth;
                endWidth = (float)lwVertex.EndWidth;
                if (startWidth <= WidthEpsilon)
                {
                    startWidth = constantWidth;
                }
                if (endWidth <= WidthEpsilon)
                {
                    endWidth = constantWidth;
                }
                return;
            case Vertex vertexEntity:
                startWidth = (float)vertexEntity.StartWidth;
                endWidth = (float)vertexEntity.EndWidth;
                if (startWidth <= WidthEpsilon)
                {
                    startWidth = defaultStart;
                }
                if (endWidth <= WidthEpsilon)
                {
                    endWidth = defaultEnd;
                }
                return;
            default:
                startWidth = defaultStart;
                endWidth = defaultEnd;
                return;
        }
    }

    private static bool HasVertexWidths<TVertex>(IEnumerable<TVertex> vertices)
        where TVertex : IVertex
    {
        foreach (var vertex in vertices)
        {
            if (vertex is LwPolyline.Vertex lwVertex)
            {
                if (lwVertex.StartWidth > WidthEpsilon || lwVertex.EndWidth > WidthEpsilon)
                {
                    return true;
                }
            }
            else if (vertex is Vertex vertexEntity)
            {
                if (vertexEntity.StartWidth > WidthEpsilon || vertexEntity.EndWidth > WidthEpsilon)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<IVertex> ToVertexList(IEnumerable<IVertex> vertices)
    {
        if (vertices is List<IVertex> list)
        {
            return list;
        }

        var result = new List<IVertex>();
        foreach (var vertex in vertices)
        {
            result.Add(vertex);
        }

        return result;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    private static bool IsClose(XYZ a, XYZ b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz <= PositionEpsilon * PositionEpsilon;
    }
}
