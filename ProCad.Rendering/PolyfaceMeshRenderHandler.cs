using System;
using System.Collections.Generic;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Rendering;

public sealed class PolyfaceMeshRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is PolyfaceMesh;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var mesh = (PolyfaceMesh)entity;
        if (mesh.Vertices.Count == 0 || mesh.Faces.Count == 0)
        {
            return;
        }

        var builder = context.GetLayerBuilder(mesh);
        var color = context.ResolveEntityColor(mesh);
        var shadePolicy = RenderShadeEdgeUtils.Resolve(context.Settings);
        var edgeColor = RenderShadeEdgeUtils.ResolveEdgeColor(color, shadePolicy);
        var thickness = context.ResolveLineWeight(mesh);
        var lineCap = context.ResolveLineCap(mesh);
        var lineJoin = context.ResolveLineJoin(mesh);
        var pattern = context.ResolveLinePattern(mesh);
        var material = context.ResolveEntityMaterial(mesh);
        var emitSurface = shadePolicy.EmitSurfaces;
        var emitEdges = shadePolicy.EmitEdges;
        var lighting = RenderShadeEdgeUtils.ResolveLighting(context.Settings, shadePolicy);

        foreach (var face in mesh.Faces)
        {
            if (emitSurface)
            {
                AppendFaceSurface(mesh, face, builder, transform, color, material, lighting, shadePolicy);
            }

            if (!emitEdges)
            {
                continue;
            }

            AppendFaceEdges(mesh, face, builder, transform, edgeColor, thickness, lineCap, lineJoin, pattern, context);
        }
    }

    private static void AppendFaceEdges(
        PolyfaceMesh mesh,
        VertexFaceRecord face,
        RenderLayerBuilder builder,
        Transform transform,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        RenderLinePattern pattern,
        RenderBuildContext context)
    {
        if (face is null)
        {
            return;
        }

        var indices = new[] { face.Index1, face.Index2, face.Index3, face.Index4 };
        var points = new List<XYZ>(4);
        var edgeVisible = new List<bool>(4);

        for (var i = 0; i < indices.Length; i++)
        {
            var index = indices[i];
            if (index == 0)
            {
                continue;
            }

            var visible = index > 0;
            var vertexIndex = Math.Abs(index) - 1;
            if (vertexIndex < 0 || vertexIndex >= mesh.Vertices.Count)
            {
                points.Clear();
                break;
            }

            points.Add(mesh.Vertices[vertexIndex].Location);
            edgeVisible.Add(visible);
        }

        if (points.Count < 3)
        {
            return;
        }

        if (AllVisible(edgeVisible))
        {
            RenderPrimitiveBuilder.AddSampled(
                builder,
                points,
                transform,
                isClosed: true,
                color,
                thickness,
                lineCap,
                lineJoin,
                pattern,
                context.ShapeResolver,
                context.Settings);
            return;
        }

        for (var i = 0; i < points.Count; i++)
        {
            if (!edgeVisible[i])
            {
                continue;
            }

            var start = points[i];
            var end = points[(i + 1) % points.Count];
            var segment = new[] { start, end };
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
    }

    private static void AppendFaceSurface(
        PolyfaceMesh mesh,
        VertexFaceRecord face,
        RenderLayerBuilder builder,
        Transform transform,
        RenderColor entityColor,
        RenderMaterial material,
        RenderLightingSettings lighting,
        RenderShadeEdgePolicy shadePolicy)
    {
        if (face is null)
        {
            return;
        }

        var indices = new[] { face.Index1, face.Index2, face.Index3, face.Index4 };
        var vertices = new List<XYZ>(4);

        for (var i = 0; i < indices.Length; i++)
        {
            var index = indices[i];
            if (index == 0)
            {
                continue;
            }

            var vertexIndex = Math.Abs(index) - 1;
            if (vertexIndex < 0 || vertexIndex >= mesh.Vertices.Count)
            {
                vertices.Clear();
                break;
            }

            vertices.Add(mesh.Vertices[vertexIndex].Location);
        }

        if (vertices.Count < 3)
        {
            return;
        }

        var triangles = MeshTessellator.Tessellate(vertices, subdivisionLevel: 0);
        foreach (var triangle in triangles)
        {
            var a3 = RenderTransformUtils.Apply3D(transform, triangle.A);
            var b3 = RenderTransformUtils.Apply3D(transform, triangle.B);
            var c3 = RenderTransformUtils.Apply3D(transform, triangle.C);
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
        }
    }

    private static bool AllVisible(IReadOnlyList<bool> edgeVisible)
    {
        for (var i = 0; i < edgeVisible.Count; i++)
        {
            if (!edgeVisible[i])
            {
                return false;
            }
        }

        return true;
    }

}
