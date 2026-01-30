using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class DimensionRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is Dimension;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var dimension = (Dimension)entity;
        if (DimensionPrimitiveBuilder.TryBuild(dimension, context, out var primitives))
        {
            foreach (var primitive in primitives)
            {
                context.Dispatcher.Append(primitive, transform, context);
            }

            return;
        }

        var block = dimension.Block;
        if (block is null)
        {
            dimension.UpdateBlock();
            block = dimension.Block;
            if (block is null)
            {
                return;
            }
        }

        var ordered = context.EntityOrderResolver.OrderEntities(block.Entities, block);
        foreach (var child in ordered)
        {
            context.Dispatcher.Append(child, transform, context);
        }
    }
}
