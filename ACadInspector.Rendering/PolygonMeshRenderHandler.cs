using System.Collections.Generic;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class PolygonMeshRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is PolygonMesh;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var mesh = (PolygonMesh)entity;
        var vertices = ToVertexList(mesh.Vertices);
        if (vertices.Count == 0)
        {
            return;
        }

        var mCount = mesh.MVertexCount > 0 ? mesh.MVertexCount : (short)0;
        var nCount = mesh.NVertexCount > 0 ? mesh.NVertexCount : (short)0;
        if (mCount <= 0 || nCount <= 0)
        {
            GuessDimensions(vertices.Count, ref mCount, ref nCount);
        }

        if (mCount <= 0 || nCount <= 0)
        {
            return;
        }

        var total = mCount * nCount;
        if (total <= 0 || total > vertices.Count)
        {
            total = vertices.Count;
            nCount = (short)(total / mCount);
        }

        var builder = context.GetLayerBuilder(mesh);
        var color = context.ResolveEntityColor(mesh);
        var thickness = context.ResolveLineWeight(mesh);
        var lineCap = context.ResolveLineCap(mesh);
        var lineJoin = context.ResolveLineJoin(mesh);
        var pattern = context.ResolveLinePattern(mesh);

        var closeM = mesh.Flags.HasFlag(PolylineFlags.ClosedPolylineOrClosedPolygonMeshInM);
        var closeN = mesh.Flags.HasFlag(PolylineFlags.ClosedPolygonMeshInN);

        var meshTransform = RenderTransformUtils.CombineWithNormal(transform, mesh.Normal);

        // Render rows (M direction).
        for (var n = 0; n < nCount; n++)
        {
            var row = new List<CSMath.XYZ>(mCount + (closeM ? 1 : 0));
            for (var m = 0; m < mCount; m++)
            {
                var index = n * mCount + m;
                if (index >= vertices.Count)
                {
                    break;
                }

                row.Add(vertices[index].Location);
            }

            if (row.Count < 2)
            {
                continue;
            }

            RenderPrimitiveBuilder.AddSampled(
                builder,
                row,
                meshTransform,
                closeM,
                color,
                thickness,
                lineCap,
                lineJoin,
                pattern,
                context.ShapeResolver,
                context.Settings);
        }

        // Render columns (N direction).
        for (var m = 0; m < mCount; m++)
        {
            var column = new List<CSMath.XYZ>(nCount + (closeN ? 1 : 0));
            for (var n = 0; n < nCount; n++)
            {
                var index = n * mCount + m;
                if (index >= vertices.Count)
                {
                    break;
                }

                column.Add(vertices[index].Location);
            }

            if (column.Count < 2)
            {
                continue;
            }

            RenderPrimitiveBuilder.AddSampled(
                builder,
                column,
                meshTransform,
                closeN,
                color,
                thickness,
                lineCap,
                lineJoin,
                pattern,
                context.ShapeResolver,
                context.Settings);
        }
    }

    private static List<PolygonMeshVertex> ToVertexList(IEnumerable<PolygonMeshVertex> vertices)
    {
        if (vertices is List<PolygonMeshVertex> list)
        {
            return list;
        }

        return new List<PolygonMeshVertex>(vertices);
    }

    private static void GuessDimensions(int count, ref short mCount, ref short nCount)
    {
        if (count <= 0)
        {
            return;
        }

        var side = (short)System.Math.Max(1, (int)System.Math.Sqrt(count));
        mCount = side;
        nCount = (short)System.Math.Max(1, count / side);
    }
}
