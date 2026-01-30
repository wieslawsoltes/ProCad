using System;
using System.Collections.Generic;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class ModelerGeometryRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is ModelerGeometry;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var geometry = (ModelerGeometry)entity;
        var wires = ResolveWires(geometry);
        if (wires.Count == 0)
        {
            return;
        }

        var builder = context.GetLayerBuilder(geometry);
        var color = context.ResolveEntityColor(geometry);
        var material = context.ResolveEntityMaterial(geometry);
        var thickness = context.ResolveLineWeight(geometry);
        var lineCap = context.ResolveLineCap(geometry);
        var lineJoin = context.ResolveLineJoin(geometry);
        var pattern = context.ResolveLinePattern(geometry);
        var geometryOffset = geometry.Point;
        var hasOffset = !geometryOffset.IsZero();
        var emitSurface = ShouldEmitSurface(context.Settings.VisualStyle);
        if (emitSurface)
        {
            var appended = TryAppendSurfaceFromAcis(
                geometry,
                builder,
                transform,
                material,
                context.Settings,
                context.Settings.Lighting);
            if (!appended)
            {
                AppendSurfaceFromWires(wires, builder, transform, material, context.Settings.Lighting, geometryOffset, hasOffset);
            }
        }

        foreach (var wire in wires)
        {
            AppendWire(
                wire,
                builder,
                transform,
                color,
                thickness,
                lineCap,
                lineJoin,
                pattern,
                context,
                geometryOffset,
                hasOffset);
        }
    }

    private static void AppendSurfaceFromWires(
        IReadOnlyList<ModelerGeometry.Wire> wires,
        RenderLayerBuilder builder,
        Transform transform,
        RenderMaterial material,
        RenderLightingSettings lighting,
        XYZ geometryOffset,
        bool hasOffset)
    {
        foreach (var wire in wires)
        {
            if (wire.Points.Count < 3)
            {
                continue;
            }

            var points = new List<XYZ>(wire.Points.Count);
            for (var i = 0; i < wire.Points.Count; i++)
            {
                var point = wire.Points[i];
                if (hasOffset)
                {
                    point = new XYZ(point.X + geometryOffset.X, point.Y + geometryOffset.Y, point.Z + geometryOffset.Z);
                }

                points.Add(point);
            }

            if (!IsClosed(points))
            {
                continue;
            }

            var combined = wire.ApplyTransformPresent
                ? RenderTransformUtils.Combine(transform, BuildWireTransform(wire))
                : transform;

            var transformed = new List<XYZ>(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                transformed.Add(RenderTransformUtils.Apply3D(combined, points[i]));
            }

            if (!RenderPolygonTriangulator.TryTriangulate(transformed, out var triangles))
            {
                continue;
            }

            foreach (var triangle in triangles)
            {
                var litColor = RenderLightingUtils.ComputeLitColor(triangle.A, triangle.B, triangle.C, lighting, material);
                var a = RenderTransformUtils.ToVector2(triangle.A);
                var b = RenderTransformUtils.ToVector2(triangle.B);
                var c = RenderTransformUtils.ToVector2(triangle.C);
                builder.Add(new RenderTriangle(
                    a,
                    b,
                    c,
                    litColor,
                    1f,
                    (float)triangle.A.Z,
                    (float)triangle.B.Z,
                    (float)triangle.C.Z));
            }
        }
    }

    private static bool TryAppendSurfaceFromAcis(
        ModelerGeometry geometry,
        RenderLayerBuilder builder,
        Transform transform,
        RenderMaterial material,
        CadRenderSceneSettings settings,
        RenderLightingSettings lighting)
    {
        if (!RenderAcisSatTessellator.TryTessellate(geometry, settings, out var triangles))
        {
            return false;
        }

        AppendSurfaceTriangles(triangles, builder, transform, material, lighting);
        return true;
    }

    private static void AppendSurfaceTriangles(
        IReadOnlyList<MeshTessellator.Triangle> triangles,
        RenderLayerBuilder builder,
        Transform transform,
        RenderMaterial material,
        RenderLightingSettings lighting)
    {
        foreach (var triangle in triangles)
        {
            var a3 = RenderTransformUtils.Apply3D(transform, triangle.A);
            var b3 = RenderTransformUtils.Apply3D(transform, triangle.B);
            var c3 = RenderTransformUtils.Apply3D(transform, triangle.C);
            var litColor = RenderLightingUtils.ComputeLitColor(a3, b3, c3, lighting, material);
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

    private static IReadOnlyList<ModelerGeometry.Wire> ResolveWires(ModelerGeometry geometry)
    {
        if (geometry.Silhouettes.Count == 0)
        {
            return geometry.Wires;
        }

        var count = 0;
        foreach (var silhouette in geometry.Silhouettes)
        {
            count += silhouette.Wires.Count;
        }

        var wires = new List<ModelerGeometry.Wire>(count);
        foreach (var silhouette in geometry.Silhouettes)
        {
            wires.AddRange(silhouette.Wires);
        }

        return wires;
    }

    private static void AppendWire(
        ModelerGeometry.Wire wire,
        RenderLayerBuilder builder,
        Transform transform,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        RenderLinePattern pattern,
        RenderBuildContext context,
        XYZ geometryOffset,
        bool hasOffset)
    {
        if (wire.Points.Count < 2)
        {
            return;
        }

        var points = new List<XYZ>(wire.Points.Count);
        for (var i = 0; i < wire.Points.Count; i++)
        {
            var point = wire.Points[i];
            if (hasOffset)
            {
                point = new XYZ(point.X + geometryOffset.X, point.Y + geometryOffset.Y, point.Z + geometryOffset.Z);
            }

            points.Add(point);
        }

        var combined = wire.ApplyTransformPresent
            ? RenderTransformUtils.Combine(transform, BuildWireTransform(wire))
            : transform;

        RenderPrimitiveBuilder.AddSampled(
            builder,
            points,
            combined,
            IsClosed(points),
            color,
            thickness,
            lineCap,
            lineJoin,
            pattern,
            context.ShapeResolver,
            context.Settings);
    }

    private static Transform BuildWireTransform(ModelerGeometry.Wire wire)
    {
        var xAxis = wire.XAxis.IsZero() ? XYZ.AxisX : wire.XAxis;
        var yAxis = wire.YAxis.IsZero() ? XYZ.AxisY : wire.YAxis;
        var zAxis = wire.ZAxis.IsZero() ? XYZ.AxisZ : wire.ZAxis;

        var scale = wire.Scale <= 0.0 ? 1.0 : wire.Scale;
        xAxis = new XYZ(xAxis.X * scale, xAxis.Y * scale, xAxis.Z * scale);
        yAxis = new XYZ(yAxis.X * scale, yAxis.Y * scale, yAxis.Z * scale);
        zAxis = new XYZ(zAxis.X * scale, zAxis.Y * scale, zAxis.Z * scale);

        var matrix = Matrix4.Identity;
        matrix.M00 = xAxis.X;
        matrix.M10 = yAxis.X;
        matrix.M20 = zAxis.X;
        matrix.M30 = wire.Translation.X;
        matrix.M01 = xAxis.Y;
        matrix.M11 = yAxis.Y;
        matrix.M21 = zAxis.Y;
        matrix.M31 = wire.Translation.Y;
        matrix.M02 = xAxis.Z;
        matrix.M12 = yAxis.Z;
        matrix.M22 = zAxis.Z;
        matrix.M32 = wire.Translation.Z;
        matrix.M33 = 1.0;

        return new Transform(matrix);
    }

    private static bool IsClosed(IReadOnlyList<XYZ> points)
    {
        if (points.Count < 3)
        {
            return false;
        }

        var first = points[0];
        var last = points[^1];
        var dx = first.X - last.X;
        var dy = first.Y - last.Y;
        var dz = first.Z - last.Z;
        const double tolerance = 1e-6;
        return Math.Abs(dx) <= tolerance && Math.Abs(dy) <= tolerance && Math.Abs(dz) <= tolerance;
    }

    private static bool ShouldEmitSurface(RenderVisualStyle style)
    {
        return style == RenderVisualStyle.Shaded || style == RenderVisualStyle.HiddenLine;
    }
}
