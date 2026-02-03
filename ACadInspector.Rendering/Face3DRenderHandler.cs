using System;
using System.Collections.Generic;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class Face3DRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is Face3D;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var face = (Face3D)entity;
        var vertices = BuildVertices(face);
        if (vertices.Count < 3)
        {
            return;
        }

        var builder = context.GetLayerBuilder(face);
        var color = context.ResolveEntityColor(face);
        var shadePolicy = RenderShadeEdgeUtils.Resolve(context.Settings);
        var edgeColor = RenderShadeEdgeUtils.ResolveEdgeColor(color, shadePolicy);
        var thickness = context.ResolveLineWeight(face);
        var lineCap = context.ResolveLineCap(face);
        var lineJoin = context.ResolveLineJoin(face);
        var pattern = context.ResolveLinePattern(face);
        var material = context.ResolveEntityMaterial(face);
        var emitSurface = shadePolicy.EmitSurfaces;
        if (emitSurface)
        {
            var lighting = RenderShadeEdgeUtils.ResolveLighting(context.Settings, shadePolicy);
            AppendSurface(builder, vertices, transform, color, material, lighting, shadePolicy);
        }

        var isQuad = vertices.Count == 4;
        AppendEdge(builder, vertices, 0, 1, face.Flags.HasFlag(InvisibleEdgeFlags.First), transform, edgeColor, thickness, lineCap, lineJoin, pattern, context, shadePolicy);
        AppendEdge(builder, vertices, 1, 2, face.Flags.HasFlag(InvisibleEdgeFlags.Second), transform, edgeColor, thickness, lineCap, lineJoin, pattern, context, shadePolicy);

        var thirdEnd = isQuad ? 3 : 0;
        AppendEdge(builder, vertices, 2, thirdEnd, face.Flags.HasFlag(InvisibleEdgeFlags.Third), transform, edgeColor, thickness, lineCap, lineJoin, pattern, context, shadePolicy);

        if (isQuad)
        {
            AppendEdge(builder, vertices, 3, 0, face.Flags.HasFlag(InvisibleEdgeFlags.Fourth), transform, edgeColor, thickness, lineCap, lineJoin, pattern, context, shadePolicy);
        }
    }

    private static List<XYZ> BuildVertices(Face3D face)
    {
        var vertices = new List<XYZ>(4)
        {
            face.FirstCorner,
            face.SecondCorner,
            face.ThirdCorner
        };

        if (face.FourthCorner != face.ThirdCorner)
        {
            vertices.Add(face.FourthCorner);
        }

        return vertices;
    }

    private static void AppendEdge(
        RenderLayerBuilder builder,
        IReadOnlyList<XYZ> vertices,
        int start,
        int end,
        bool isHidden,
        Transform transform,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        RenderLinePattern pattern,
        RenderBuildContext context,
        RenderShadeEdgePolicy shadePolicy)
    {
        if (isHidden || !shadePolicy.EmitEdges)
        {
            return;
        }

        if (start < 0 || end < 0 || start >= vertices.Count || end >= vertices.Count)
        {
            return;
        }

        var segment = new[] { vertices[start], vertices[end] };
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

    private static void AppendSurface(
        RenderLayerBuilder builder,
        IReadOnlyList<XYZ> vertices,
        Transform transform,
        RenderColor entityColor,
        RenderMaterial material,
        RenderLightingSettings lighting,
        RenderShadeEdgePolicy shadePolicy)
    {
        if (vertices.Count < 3)
        {
            return;
        }

        var a3 = RenderTransformUtils.Apply3D(transform, vertices[0]);
        var b3 = RenderTransformUtils.Apply3D(transform, vertices[1]);
        var c3 = RenderTransformUtils.Apply3D(transform, vertices[2]);
        var litColor = RenderShadeEdgeUtils.ResolveSurfaceColor(entityColor, material, lighting, a3, b3, c3, shadePolicy);
        var a = RenderTransformUtils.ToVector2(a3);
        var b = RenderTransformUtils.ToVector2(b3);
        var c = RenderTransformUtils.ToVector2(c3);
        builder.Add(new RenderTriangle(
            a,
            b,
            c,
            litColor,
            1f,
            (float)a3.Z,
            (float)b3.Z,
            (float)c3.Z));

        if (vertices.Count == 4)
        {
            var d3 = RenderTransformUtils.Apply3D(transform, vertices[3]);
            var litColor2 = RenderShadeEdgeUtils.ResolveSurfaceColor(entityColor, material, lighting, a3, c3, d3, shadePolicy);
            var d = RenderTransformUtils.ToVector2(d3);
            builder.Add(new RenderTriangle(
                a,
                c,
                d,
                litColor2,
                1f,
                (float)a3.Z,
                (float)c3.Z,
                (float)d3.Z));
        }
    }

}
