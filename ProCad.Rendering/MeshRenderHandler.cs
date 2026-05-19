using System;
using System.Collections.Generic;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Rendering;

public sealed class MeshRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is Mesh;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var mesh = (Mesh)entity;
        if (mesh.Vertices.Count < 3 || mesh.Faces.Count == 0)
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
        var subdivisionLevel = ResolveSubdivisionLevel(mesh, context.Settings);
        var emitSurface = shadePolicy.EmitSurfaces;
        var emitEdges = shadePolicy.EmitEdges;
        var lighting = RenderShadeEdgeUtils.ResolveLighting(context.Settings, shadePolicy);

        foreach (var face in mesh.Faces)
        {
            if (emitSurface)
            {
                AppendFaceSurface(mesh, face, subdivisionLevel, builder, transform, color, material, lighting, shadePolicy);
            }

            if (!emitEdges)
            {
                continue;
            }

            if (subdivisionLevel > 0 && !HasHiddenEdges(face))
            {
                AppendSubdividedFace(
                    mesh,
                    face,
                    subdivisionLevel,
                    builder,
                    transform,
                    edgeColor,
                    thickness,
                    lineCap,
                    lineJoin,
                    pattern,
                    context);
                continue;
            }

            AppendFaceEdges(mesh, face, builder, transform, edgeColor, thickness, lineCap, lineJoin, pattern, context);
        }
    }

    private static void AppendFaceEdges(
        Mesh mesh,
        int[] face,
        RenderLayerBuilder builder,
        Transform transform,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        RenderLinePattern pattern,
        RenderBuildContext context)
    {
        if (face is null || face.Length < 3)
        {
            return;
        }

        var points = new List<XYZ>(face.Length);
        var edgeVisible = new List<bool>(face.Length);

        for (var i = 0; i < face.Length; i++)
        {
            var index = face[i];
            var visible = index >= 0;
            var vertexIndex = Math.Abs(index);
            if (vertexIndex < 0 || vertexIndex >= mesh.Vertices.Count)
            {
                points.Clear();
                break;
            }

            points.Add(mesh.Vertices[vertexIndex]);
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

    private static void AppendSubdividedFace(
        Mesh mesh,
        int[] face,
        int subdivisionLevel,
        RenderLayerBuilder builder,
        Transform transform,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        RenderLinePattern pattern,
        RenderBuildContext context)
    {
        if (face is null || face.Length < 3 || subdivisionLevel <= 0)
        {
            return;
        }

        var vertices = new List<XYZ>(face.Length);
        for (var i = 0; i < face.Length; i++)
        {
            var index = face[i];
            if (index == int.MinValue)
            {
                vertices.Clear();
                break;
            }

            var vertexIndex = index >= 0 ? index : -index;
            if (vertexIndex < 0 || vertexIndex >= mesh.Vertices.Count)
            {
                vertices.Clear();
                break;
            }

            vertices.Add(mesh.Vertices[vertexIndex]);
        }

        if (vertices.Count < 3)
        {
            return;
        }

        var triangles = MeshTessellator.Tessellate(vertices, subdivisionLevel);
        foreach (var triangle in triangles)
        {
            var points = new[] { triangle.A, triangle.B, triangle.C };
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
        }
    }

    private static void AppendFaceSurface(
        Mesh mesh,
        int[] face,
        int subdivisionLevel,
        RenderLayerBuilder builder,
        Transform transform,
        RenderColor entityColor,
        RenderMaterial material,
        RenderLightingSettings lighting,
        RenderShadeEdgePolicy shadePolicy)
    {
        if (face is null || face.Length < 3)
        {
            return;
        }

        var vertices = new List<XYZ>(face.Length);
        for (var i = 0; i < face.Length; i++)
        {
            var index = face[i];
            if (index == int.MinValue)
            {
                vertices.Clear();
                break;
            }

            var vertexIndex = index >= 0 ? index : -index;
            if (vertexIndex < 0 || vertexIndex >= mesh.Vertices.Count)
            {
                vertices.Clear();
                break;
            }

            vertices.Add(mesh.Vertices[vertexIndex]);
        }

        if (vertices.Count < 3)
        {
            return;
        }

        var triangles = MeshTessellator.Tessellate(vertices, subdivisionLevel);
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

    private static int ResolveSubdivisionLevel(Mesh mesh, CadRenderSceneSettings settings)
    {
        if (mesh.SubdivisionLevel <= 0)
        {
            return 0;
        }

        const int maxSubdivisionLevel = 3;
        var qualityLimit = settings.Quality switch
        {
            RenderQuality.Draft => 0,
            RenderQuality.Medium => 1,
            RenderQuality.High => maxSubdivisionLevel,
            _ => 1
        };

        if (qualityLimit <= 0)
        {
            return 0;
        }

        var level = Math.Min(mesh.SubdivisionLevel, maxSubdivisionLevel);
        return Math.Min(level, qualityLimit);
    }

    private static bool HasHiddenEdges(int[] face)
    {
        if (face is null)
        {
            return false;
        }

        for (var i = 0; i < face.Length; i++)
        {
            if (face[i] < 0)
            {
                return true;
            }
        }

        return false;
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
