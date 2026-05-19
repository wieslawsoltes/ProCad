using System;
using System.Collections.Generic;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;

namespace ProCad.Rendering;

public sealed class LeaderRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is Leader;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var leader = (Leader)entity;
        if (leader.Vertices.Count < 2)
        {
            return;
        }

        var builder = context.GetLayerBuilder(leader);
        var color = context.ResolveEntityColor(leader);
        var thickness = context.ResolveLineWeight(leader);
        var lineCap = context.ResolveLineCap(leader);
        var lineJoin = context.ResolveLineJoin(leader);
        var pattern = context.ResolveLinePattern(leader);

        RenderPrimitiveBuilder.AddSampled(
            builder,
            leader.Vertices,
            transform,
            isClosed: false,
            color,
            thickness,
            lineCap,
            lineJoin,
            pattern,
            context.ShapeResolver,
            context.Settings);

        if (!leader.ArrowHeadEnabled)
        {
            return;
        }

        var arrow = CreateArrowEntity(leader);
        if (arrow is null)
        {
            return;
        }

        context.Dispatcher.Append(arrow, transform, context);
    }

    private static Entity? CreateArrowEntity(Leader leader)
    {
        if (leader.Vertices.Count < 2)
        {
            return null;
        }

        var start = leader.Vertices[0];
        var next = leader.Vertices[1];
        var dir = start - next;
        if (dir.IsZero())
        {
            return null;
        }

        dir = dir.Normalize();
        var style = leader.Style ?? DimensionStyle.Default;
        var arrowBlock = style.LeaderArrow;
        var scale = style.ArrowSize * style.ScaleFactor;
        if (scale <= 0)
        {
            return null;
        }

        var rotation = Math.Atan2(dir.Y, dir.X);

        if (arrowBlock is null)
        {
            var perp = XYZ.Cross(leader.Normal, dir).Normalize();
            var arrow = new Solid
            {
                FirstCorner = start,
                SecondCorner = start - scale * dir - scale / 6 * perp,
                ThirdCorner = start - scale * dir + scale / 6 * perp,
                FourthCorner = start - scale * dir + scale / 6 * perp,
                Color = leader.Color,
                LineWeight = leader.LineWeight,
                Layer = leader.Layer
            };
            return arrow;
        }

        return new Insert(arrowBlock)
        {
            InsertPoint = start,
            Color = leader.Color,
            XScale = scale,
            YScale = scale,
            ZScale = scale,
            Rotation = rotation,
            LineWeight = leader.LineWeight,
            Normal = leader.Normal,
            Layer = leader.Layer
        };
    }
}
