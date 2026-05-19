using ACadSharp.Entities;
using CSMath;

namespace ProCad.Rendering;

public sealed class DimensionRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is Dimension;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var dimension = (Dimension)entity;
        var block = dimension.Block;
        if (block is not null && block.Entities.Count > 0)
        {
            var orderedEntities = context.EntityOrderResolver.OrderEntities(block.Entities, block);
            foreach (var child in orderedEntities)
            {
                context.Dispatcher.Append(child, transform, context);
            }

            return;
        }

        if (DimensionPrimitiveBuilder.TryBuild(dimension, context, out var primitives))
        {
            foreach (var primitive in primitives)
            {
                context.Dispatcher.Append(primitive, transform, context);
            }

            return;
        }

        if (block is null || block.Entities.Count == 0)
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
