using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ACadSharp.Entities;
using ACadSharp.Objects;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class PdfUnderlayRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is PdfUnderlay;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var underlay = (PdfUnderlay)entity;
        if (!underlay.Flags.HasFlag(UnderlayDisplayFlags.ShowUnderlay))
        {
            return;
        }

        var vertices = ResolveBoundaryVertices(underlay);
        var bounds = ResolveBounds(vertices);
        if (bounds.Size.X <= 0 || bounds.Size.Y <= 0)
        {
            return;
        }

        var axis = ResolveAxes(underlay);
        var originWorld = underlay.InsertPoint + axis.U * bounds.Min.X + axis.V * bounds.Min.Y;
        var origin = RenderTransformUtils.Apply(transform, originWorld);
        var uVector = ResolveVector(transform, originWorld, axis.U);
        var vVector = ResolveVector(transform, originWorld, axis.V);
        if (uVector.LengthSquared() <= 0 || vVector.LengthSquared() <= 0)
        {
            return;
        }

        var sourcePath = ResolvePath(underlay.Definition, context.Settings.SupportPaths);
        var label = ResolveLabel(underlay.Definition);
        var color = ResolveUnderlayColor(underlay, context);
        var frameColor = new RenderColor(color.R, color.G, color.B, 255);
        var opacity = ResolveOpacity(color, underlay.Fade);

        var renderImage = new RenderImage(
            sourcePath,
            label,
            origin,
            uVector,
            vVector,
            new Vector2(bounds.Size.X, bounds.Size.Y),
            frameColor,
            opacity);

        var builder = context.GetLayerBuilder(underlay);
        var loops = BuildClipLoops(underlay, transform, vertices, axis);
        if (underlay.Flags.HasFlag(UnderlayDisplayFlags.ClippingOn) && vertices.Count >= 3)
        {
            builder.Add(new RenderClipGroup(loops, new IRenderPrimitive[] { renderImage }));
        }
        else
        {
            builder.Add(renderImage);
        }

        if (context.Settings.UnderlayFrameVisibility.ShouldDisplay())
        {
            AppendFrame(builder, loops, underlay, context);
        }
    }

    private static List<XY> ResolveBoundaryVertices(PdfUnderlay underlay)
    {
        if (underlay.ClipBoundaryVertices.Count == 0)
        {
            return new List<XY>
            {
                new XY(0, 0),
                new XY(1, 0),
                new XY(1, 1),
                new XY(0, 1)
            };
        }

        if (underlay.ClipBoundaryVertices.Count >= 2 && underlay.ClipBoundaryVertices.Count < 3)
        {
            var a = underlay.ClipBoundaryVertices[0];
            var b = underlay.ClipBoundaryVertices[1];
            var minX = Math.Min(a.X, b.X);
            var maxX = Math.Max(a.X, b.X);
            var minY = Math.Min(a.Y, b.Y);
            var maxY = Math.Max(a.Y, b.Y);
            return new List<XY>
            {
                new XY(minX, minY),
                new XY(maxX, minY),
                new XY(maxX, maxY),
                new XY(minX, maxY)
            };
        }

        return new List<XY>(underlay.ClipBoundaryVertices);
    }

    private static RenderBounds ResolveBounds(IReadOnlyList<XY> vertices)
    {
        var bounds = RenderBounds.Empty;
        foreach (var vertex in vertices)
        {
            bounds = bounds.Expand(new Vector2((float)vertex.X, (float)vertex.Y));
        }

        return bounds;
    }

    private static (XYZ U, XYZ V) ResolveAxes(PdfUnderlay underlay)
    {
        var rotation = underlay.Rotation;
        var cos = Math.Cos(rotation);
        var sin = Math.Sin(rotation);
        var u = new XYZ(cos * underlay.XScale, sin * underlay.XScale, 0);
        var v = new XYZ(-sin * underlay.YScale, cos * underlay.YScale, 0);
        return (u, v);
    }

    private static Vector2 ResolveVector(Transform transform, XYZ origin, XYZ axis)
    {
        var start = RenderTransformUtils.Apply(transform, origin);
        var end = RenderTransformUtils.Apply(transform, origin + axis);
        return end - start;
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> BuildClipLoops(
        PdfUnderlay underlay,
        Transform transform,
        IReadOnlyList<XY> vertices,
        (XYZ U, XYZ V) axis)
    {
        var loop = new List<Vector2>(vertices.Count);
        foreach (var vertex in vertices)
        {
            var world = underlay.InsertPoint + axis.U * vertex.X + axis.V * vertex.Y;
            loop.Add(RenderTransformUtils.Apply(transform, world));
        }

        return new[] { loop };
    }

    private static void AppendFrame(
        RenderLayerBuilder builder,
        IReadOnlyList<IReadOnlyList<Vector2>> loops,
        PdfUnderlay underlay,
        RenderBuildContext context)
    {
        if (loops.Count == 0)
        {
            return;
        }

        var color = context.ResolveEntityColor(underlay);
        var thickness = context.ResolveLineWeight(underlay);
        var lineCap = context.ResolveLineCap(underlay);
        var lineJoin = context.ResolveLineJoin(underlay);

        foreach (var loop in loops)
        {
            if (loop is null || loop.Count < 2)
            {
                continue;
            }

            builder.Add(new RenderPolyline(
                loop,
                isClosed: true,
                color,
                thickness,
                lineCap,
                lineJoin));
        }
    }

    private static RenderColor ResolveUnderlayColor(PdfUnderlay underlay, RenderBuildContext context)
    {
        if (underlay.Flags.HasFlag(UnderlayDisplayFlags.Monochrome))
        {
            return context.Settings.FallbackColor;
        }

        return context.ResolveEntityColor(underlay);
    }

    private static float ResolveOpacity(RenderColor color, byte fade)
    {
        var baseOpacity = color.A / 255f;
        var fadeFactor = Math.Clamp(1f - fade / 100f, 0f, 1f);
        return baseOpacity * fadeFactor;
    }

    private static string? ResolvePath(PdfUnderlayDefinition? definition, IReadOnlyList<string> supportPaths)
    {
        if (definition is null || string.IsNullOrWhiteSpace(definition.File))
        {
            return null;
        }

        var fileName = definition.File;
        if (Path.IsPathRooted(fileName))
        {
            return fileName;
        }

        foreach (var path in supportPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var candidate = Path.Combine(path, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return fileName;
    }

    private static string? ResolveLabel(PdfUnderlayDefinition? definition)
    {
        if (definition is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(definition.File)
            ? "PDF"
            : Path.GetFileName(definition.File);
    }
}
