using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ACadSharp.Entities;
using ACadSharp.Objects;
using CSMath;

namespace ProCad.Rendering;

public sealed class PdfUnderlayRenderHandler : IRenderEntityHandler
{
    private enum UnderlayClipMode
    {
        None,
        Inside,
        Outside
    }

    public bool CanHandle(Entity entity) => entity is PdfUnderlay;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var underlay = (PdfUnderlay)entity;
        var shouldRenderUnderlay = underlay.Flags.HasFlag(UnderlayDisplayFlags.ShowUnderlay);
        var shouldRenderFrame = !shouldRenderUnderlay || context.Settings.UnderlayFrameVisibility.ShouldDisplay();
        if (!shouldRenderUnderlay && !shouldRenderFrame)
        {
            return;
        }

        var clipVertices = ResolveClipBoundaryVertices(underlay);
        var clippingEnabled = underlay.Flags.HasFlag(UnderlayDisplayFlags.ClippingOn) && clipVertices.Count >= 3;
        var clipMode = ResolveClipMode(underlay, clippingEnabled);
        var imageVertices = ResolveImageBoundaryVertices();
        var bounds = ResolveBounds(imageVertices);
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

        var builder = context.GetLayerBuilder(underlay);
        var imageLoops = BuildLoops(underlay, transform, imageVertices, axis);
        if (clippingEnabled)
        {
            var clipLoops = BuildLoops(underlay, transform, clipVertices, axis);
            if (shouldRenderUnderlay)
            {
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

                if (clipMode == UnderlayClipMode.Outside &&
                    imageLoops.Count > 0 &&
                    clipLoops.Count > 0)
                {
                    builder.Add(new RenderClipGroup(
                        [imageLoops[0], clipLoops[0]],
                        new IRenderPrimitive[] { renderImage },
                        RenderLoopFillMode.EvenOdd));
                }
                else
                {
                    builder.Add(new RenderClipGroup(clipLoops, new IRenderPrimitive[] { renderImage }, RenderLoopFillMode.NonZero));
                }
            }

            if (shouldRenderFrame)
            {
                AppendFrame(builder, clipLoops, underlay, context);
            }
        }
        else
        {
            if (shouldRenderUnderlay)
            {
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
                builder.Add(renderImage);
            }

            if (shouldRenderFrame)
            {
                AppendFrame(builder, imageLoops, underlay, context);
            }
        }
    }

    private static List<XY> ResolveClipBoundaryVertices(PdfUnderlay underlay)
    {
        if (underlay.ClipBoundaryVertices.Count == 0)
        {
            return ResolveDefaultBoundaryVertices();
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

    private static List<XY> ResolveImageBoundaryVertices()
    {
        // AutoCAD keeps underlay image extents independent from clipping boundaries.
        // Clipping limits visibility, but does not remap/re-scale the image contents.
        return ResolveDefaultBoundaryVertices();
    }

    private static UnderlayClipMode ResolveClipMode(PdfUnderlay underlay, bool clippingEnabled)
    {
        if (!clippingEnabled)
        {
            return UnderlayClipMode.None;
        }

        if (underlay.Flags.HasFlag(UnderlayDisplayFlags.ClipInsideMode))
        {
            return UnderlayClipMode.Inside;
        }

        // When no explicit clip boundary exists, avoid inverting the entire underlay.
        if (underlay.ClipBoundaryVertices.Count == 0)
        {
            return UnderlayClipMode.Inside;
        }

        return UnderlayClipMode.Outside;
    }

    private static List<XY> ResolveDefaultBoundaryVertices()
    {
        return new List<XY>
        {
            new XY(0, 0),
            new XY(1, 0),
            new XY(1, 1),
            new XY(0, 1)
        };
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
        var localU = new XYZ(cos * underlay.XScale, sin * underlay.XScale, 0);
        var localV = new XYZ(-sin * underlay.YScale, cos * underlay.YScale, 0);
        var normal = RenderTransformUtils.NormalizeNormal(underlay.Normal);
        if (normal.Equals(XYZ.AxisZ))
        {
            return (localU, localV);
        }

        var ocs = new Transform(Matrix4.GetArbitraryAxis(normal));
        var u = ocs.ApplyTransform(localU);
        var v = ocs.ApplyTransform(localV);
        return (u, v);
    }

    private static Vector2 ResolveVector(Transform transform, XYZ origin, XYZ axis)
    {
        var start = RenderTransformUtils.Apply(transform, origin);
        var end = RenderTransformUtils.Apply(transform, origin + axis);
        return end - start;
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> BuildLoops(
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
        var color = underlay.Flags.HasFlag(UnderlayDisplayFlags.Monochrome)
            ? context.Settings.FallbackColor
            : context.ResolveEntityColor(underlay);
        color = ApplyUnderlayContrast(color, underlay.Contrast);

        if (underlay.Flags.HasFlag(UnderlayDisplayFlags.AdjustForBackground))
        {
            color = ApplyBackgroundContrastCompensation(color, context.Settings);
        }

        return color;
    }

    private static RenderColor ApplyUnderlayContrast(RenderColor color, byte contrast)
    {
        var mappedContrast = Math.Clamp(contrast * 0.5f, 0f, 50f);
        return RenderStyleUtils.ApplyBrightnessContrast(color, brightness: 50f, contrast: mappedContrast);
    }

    private static RenderColor ApplyBackgroundContrastCompensation(RenderColor color, CadRenderSceneSettings settings)
    {
        var background = settings.Background;
        var delta = MathF.Abs(ComputeLuminance(color) - ComputeLuminance(background));
        if (delta >= 0.35f)
        {
            return color;
        }

        var target = ResolveHighContrastColor(background, color.A);
        var amount = (0.35f - delta) / 0.35f;
        return Lerp(color, target, amount);
    }

    private static float ComputeLuminance(RenderColor color)
    {
        return (0.2126f * color.R + 0.7152f * color.G + 0.0722f * color.B) / 255f;
    }

    private static RenderColor ResolveHighContrastColor(RenderColor background, byte alpha)
    {
        return ComputeLuminance(background) < 0.5f
            ? new RenderColor(255, 255, 255, alpha)
            : new RenderColor(0, 0, 0, alpha);
    }

    private static RenderColor Lerp(RenderColor source, RenderColor target, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        var r = (byte)Math.Clamp((int)Math.Round(source.R + (target.R - source.R) * amount), 0, 255);
        var g = (byte)Math.Clamp((int)Math.Round(source.G + (target.G - source.G) * amount), 0, 255);
        var b = (byte)Math.Clamp((int)Math.Round(source.B + (target.B - source.B) * amount), 0, 255);
        return new RenderColor(r, g, b, source.A);
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
