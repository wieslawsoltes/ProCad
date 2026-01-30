using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class LineRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is Line;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var line = (Line)entity;
        var builder = context.GetLayerBuilder(line);
        var color = context.ResolveEntityColor(line);
        var thickness = context.ResolveLineWeight(line);
        var lineCap = context.ResolveLineCap(line);
        var lineJoin = context.ResolveLineJoin(line);
        var pattern = context.ResolveLinePattern(line);
        var start3 = RenderTransformUtils.Apply3D(transform, line.StartPoint);
        var end3 = RenderTransformUtils.Apply3D(transform, line.EndPoint);
        var start = RenderTransformUtils.ToVector2(start3);
        var end = RenderTransformUtils.ToVector2(end3);
        RenderLinePatternStroker.AddLine(
            builder,
            start,
            end,
            pattern,
            color,
            thickness,
            lineCap,
            lineJoin,
            context.ShapeResolver,
            context.Settings,
            (float)start3.Z,
            (float)end3.Z);
    }
}
