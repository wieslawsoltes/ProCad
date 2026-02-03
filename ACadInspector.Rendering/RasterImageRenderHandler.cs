using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class RasterImageRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is RasterImage;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var image = (RasterImage)entity;
        if (!image.ShowImage)
        {
            return;
        }

        var size = ResolveImageSize(image);
        if (size.X <= 0 || size.Y <= 0)
        {
            return;
        }

        var origin = RenderTransformUtils.Apply(transform, image.InsertPoint);
        var uVector = ResolveVector(transform, image.InsertPoint, image.UVector);
        var vVector = ResolveVector(transform, image.InsertPoint, image.VVector);
        if (uVector.LengthSquared() <= 0 || vVector.LengthSquared() <= 0)
        {
            return;
        }

        var sourcePath = ResolvePath(image.Definition?.FileName, context.Settings.SupportPaths);
        var label = string.IsNullOrWhiteSpace(sourcePath) ? null : Path.GetFileName(sourcePath);
        var color = context.ResolveEntityColor(image);
        var frameColor = new RenderColor(color.R, color.G, color.B, 255);
        var opacity = ResolveOpacity(color, image.Fade);

        var renderImage = new RenderImage(
            sourcePath,
            label,
            origin,
            uVector,
            vVector,
            new Vector2((float)size.X, (float)size.Y),
            frameColor,
            opacity);

        var builder = context.GetLayerBuilder(image);
        var loops = BuildClipLoops(image, transform, size);
        if (loops.Count > 0)
        {
            builder.Add(new RenderClipGroup(loops, new IRenderPrimitive[] { renderImage }, RenderLoopFillMode.NonZero));
        }
        else
        {
            builder.Add(renderImage);
        }
    }

    private static XY ResolveImageSize(RasterImage image)
    {
        var size = image.Size;
        if (size.X > 0 && size.Y > 0)
        {
            return size;
        }

        return image.Definition?.Size ?? new XY();
    }

    private static Vector2 ResolveVector(Transform transform, XYZ origin, XYZ vector)
    {
        var start = RenderTransformUtils.Apply(transform, origin);
        var end = RenderTransformUtils.Apply(transform, origin + vector);
        return end - start;
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> BuildClipLoops(
        RasterImage image,
        Transform transform,
        XY size)
    {
        if (!image.ClippingState || !image.Flags.HasFlag(ImageDisplayFlags.UseClippingBoundary))
        {
            return Array.Empty<IReadOnlyList<Vector2>>();
        }

        var vertices = ResolveBoundaryVertices(image, size);
        if (vertices.Count < 3)
        {
            return Array.Empty<IReadOnlyList<Vector2>>();
        }

        var loop = new List<Vector2>(vertices.Count);
        foreach (var vertex in vertices)
        {
            var world = image.InsertPoint + image.UVector * vertex.X + image.VVector * vertex.Y;
            loop.Add(RenderTransformUtils.Apply(transform, world));
        }

        return new[] { loop };
    }

    private static List<XY> ResolveBoundaryVertices(RasterImage image, XY size)
    {
        if (image.ClipBoundaryVertices.Count > 0)
        {
            if (image.ClipType == ClipType.Rectangular && image.ClipBoundaryVertices.Count >= 2)
            {
                var a = image.ClipBoundaryVertices[0];
                var b = image.ClipBoundaryVertices[1];
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

            return new List<XY>(image.ClipBoundaryVertices);
        }

        if (size.X <= 0 || size.Y <= 0)
        {
            return new List<XY>();
        }

        return new List<XY>
        {
            new XY(-0.5, -0.5),
            new XY(size.X - 0.5, -0.5),
            new XY(size.X - 0.5, size.Y - 0.5),
            new XY(-0.5, size.Y - 0.5)
        };
    }

    private static float ResolveOpacity(RenderColor color, byte fade)
    {
        var baseOpacity = color.A / 255f;
        var fadeFactor = Math.Clamp(1f - fade / 100f, 0f, 1f);
        return baseOpacity * fadeFactor;
    }

    private static string? ResolvePath(string? fileName, IReadOnlyList<string> supportPaths)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

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
}
