using System;
using System.Collections.Generic;
using System.Numerics;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class WipeoutRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is Wipeout;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var wipeout = (Wipeout)entity;
        var shouldRenderMask = wipeout.ShowImage;
        var shouldRenderFrame = !wipeout.ShowImage || context.Settings.WipeoutFrameVisibility.ShouldDisplay();
        if (!shouldRenderMask && !shouldRenderFrame)
        {
            return;
        }

        var loops = BuildWipeoutLoop(wipeout, transform);
        if (loops.Count == 0)
        {
            return;
        }

        var builder = context.GetLayerBuilder(wipeout);
        if (shouldRenderMask)
        {
            var fillColor = context.Settings.Background;
            if (wipeout.ClipMode == ClipMode.Outside)
            {
                var fillLoops = BuildOutsideFillLoops(context, loops[0]);
                builder.Add(new RenderHatchFill(fillLoops, fillColor, gradient: null, RenderLoopFillMode.EvenOdd));
            }
            else
            {
                builder.Add(new RenderFill(loops[0], fillColor));
            }
        }

        if (shouldRenderFrame)
        {
            var frameColor = context.ResolveEntityColor(wipeout);
            var thickness = context.ResolveLineWeight(wipeout);
            var lineCap = context.ResolveLineCap(wipeout);
            var lineJoin = context.ResolveLineJoin(wipeout);
            builder.Add(new RenderPolyline(
                loops[0],
                isClosed: true,
                frameColor,
                thickness,
                lineCap,
                lineJoin));
        }
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> BuildWipeoutLoop(
        Wipeout wipeout,
        Transform transform)
    {
        var vertices = ResolveBoundaryVertices(wipeout);
        if (vertices.Count < 3)
        {
            return Array.Empty<IReadOnlyList<Vector2>>();
        }

        var loop = new List<Vector2>(vertices.Count);
        foreach (var vertex in vertices)
        {
            var world = ResolveWorldPoint(wipeout, vertex);
            loop.Add(RenderTransformUtils.Apply(transform, world));
        }

        return new[] { loop };
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> BuildOutsideFillLoops(RenderBuildContext context, IReadOnlyList<Vector2> wipeoutLoop)
    {
        var clipBounds = RenderBounds.Empty;
        foreach (var point in wipeoutLoop)
        {
            clipBounds = clipBounds.Expand(point);
        }

        var viewBounds = RenderViewBoundsResolver.Resolve(context.Document, context.Settings).Expand(clipBounds);
        var size = viewBounds.Size;
        var padding = MathF.Max(MathF.Max(size.X, size.Y) * 0.1f, 1f);
        var outer = viewBounds.Inflate(padding);

        var outerLoop = new List<Vector2>(4)
        {
            new Vector2(outer.MinX, outer.MinY),
            new Vector2(outer.MaxX, outer.MinY),
            new Vector2(outer.MaxX, outer.MaxY),
            new Vector2(outer.MinX, outer.MaxY)
        };

        return new IReadOnlyList<Vector2>[] { outerLoop, wipeoutLoop };
    }

    private static List<XY> ResolveBoundaryVertices(Wipeout wipeout)
    {
        if (wipeout.ClipBoundaryVertices.Count > 0)
        {
            if (wipeout.ClipBoundaryVertices.Count >= 2 &&
                (wipeout.ClipType == ClipType.Rectangular || wipeout.ClipBoundaryVertices.Count == 2))
            {
                var a = wipeout.ClipBoundaryVertices[0];
                var b = wipeout.ClipBoundaryVertices[1];
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

            return new List<XY>(wipeout.ClipBoundaryVertices);
        }

        // Some files encode wipeout by size only; synthesize an axis-aligned boundary.
        if (wipeout.Size.X <= 0 || wipeout.Size.Y <= 0)
        {
            return new List<XY>();
        }

        return new List<XY>
        {
            new XY(-0.5, -0.5),
            new XY(wipeout.Size.X - 0.5, -0.5),
            new XY(wipeout.Size.X - 0.5, wipeout.Size.Y - 0.5),
            new XY(-0.5, wipeout.Size.Y - 0.5)
        };
    }

    private static XYZ ResolveWorldPoint(Wipeout wipeout, XY vertex)
    {
        return wipeout.InsertPoint + wipeout.UVector * vertex.X + wipeout.VVector * vertex.Y;
    }
}
