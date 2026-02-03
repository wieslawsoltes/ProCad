using System;
using System.Collections.Generic;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class WipeoutRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is Wipeout;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var wipeout = (Wipeout)entity;
        if (wipeout.ClipMode == ClipMode.Outside)
        {
            // TODO: Support inverted wipeout clipping (clip outside boundary). Currently rendered as inside only.
        }
        var loops = BuildWipeoutLoop(wipeout, transform);
        if (loops.Count == 0)
        {
            return;
        }

        var builder = context.GetLayerBuilder(wipeout);
        var fillColor = context.Settings.Background;
        builder.Add(new RenderFill(loops[0], fillColor));

        if (context.Settings.WipeoutFrameVisibility.ShouldDisplay())
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

    private static IReadOnlyList<IReadOnlyList<System.Numerics.Vector2>> BuildWipeoutLoop(
        Wipeout wipeout,
        Transform transform)
    {
        var vertices = ResolveBoundaryVertices(wipeout);
        if (vertices.Count < 3)
        {
            return Array.Empty<IReadOnlyList<System.Numerics.Vector2>>();
        }

        var loop = new List<System.Numerics.Vector2>(vertices.Count);
        foreach (var vertex in vertices)
        {
            var world = ResolveWorldPoint(wipeout, vertex);
            loop.Add(RenderTransformUtils.Apply(transform, world));
        }

        return new[] { loop };
    }

    private static List<XY> ResolveBoundaryVertices(Wipeout wipeout)
    {
        var useClipBoundary = wipeout.ClippingState;
        if (!useClipBoundary || wipeout.ClipBoundaryVertices.Count == 0)
        {
            var size = wipeout.Size;
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

        if (wipeout.ClipBoundaryVertices.Count > 0)
        {
            if (wipeout.ClipType == ClipType.Rectangular && wipeout.ClipBoundaryVertices.Count >= 2)
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

        return new List<XY>();
    }

    private static XYZ ResolveWorldPoint(Wipeout wipeout, XY vertex)
    {
        return wipeout.InsertPoint + wipeout.UVector * vertex.X + wipeout.VVector * vertex.Y;
    }
}
