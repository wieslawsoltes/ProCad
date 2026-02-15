using System.Collections.Generic;
using System.Numerics;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class Ole2FrameRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is Ole2Frame;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var frame = (Ole2Frame)entity;
        if (!context.Settings.OleFrameVisibility.ShouldDisplay())
        {
            return;
        }

        var builder = context.GetLayerBuilder(frame);
        var color = context.ResolveEntityColor(frame);
        var thickness = context.ResolveLineWeight(frame);
        var lineCap = context.ResolveLineCap(frame);
        var lineJoin = context.ResolveLineJoin(frame);

        var min = frame.LowerRightCorner;
        var max = frame.UpperLeftCorner;
        var minX = System.Math.Min(min.X, max.X);
        var maxX = System.Math.Max(min.X, max.X);
        var minY = System.Math.Min(min.Y, max.Y);
        var maxY = System.Math.Max(min.Y, max.Y);

        var corners = new List<Vector2>(4)
        {
            RenderTransformUtils.Apply(transform, new XYZ(minX, minY, 0)),
            RenderTransformUtils.Apply(transform, new XYZ(maxX, minY, 0)),
            RenderTransformUtils.Apply(transform, new XYZ(maxX, maxY, 0)),
            RenderTransformUtils.Apply(transform, new XYZ(minX, maxY, 0))
        };

        builder.Add(new RenderPolyline(corners, isClosed: true, color, thickness, lineCap, lineJoin));

        if (!string.IsNullOrWhiteSpace(frame.SourceApplication))
        {
            var label = frame.SourceApplication;
            var center = new Vector2((float)((minX + maxX) * 0.5), (float)((minY + maxY) * 0.5));
            var anchor = RenderTransformUtils.Apply(transform, new XYZ(center.X, center.Y, 0));
            builder.Add(new RenderText(
                label,
                anchor,
                new Vector2(-label.Length * 0.5f, 0f),
                label.Length,
                1f,
                1f,
                1f,
                0f,
                0f,
                false,
                false,
                false,
                false,
                color,
                null));
        }
    }
}
