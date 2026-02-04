using ACadSharp.Entities;
using ACadSharp.Tables;

namespace ACadInspector.Rendering;

public sealed class DefaultRenderEntityVisibilityResolver : IRenderEntityVisibilityResolver
{
    public bool ShouldRender(Entity entity, CadRenderSceneSettings settings)
    {
        if (!settings.RenderAttributes && entity is AttributeEntity)
        {
            return false;
        }

        if (!settings.RenderAttributeDefinitions && entity is AttributeDefinition)
        {
            return false;
        }

        if (!settings.IncludeInvisible && entity.IsInvisible)
        {
            return false;
        }

        var layer = entity.Layer ?? Layer.Default;
        if (!settings.IncludeOffLayers && !layer.IsOn)
        {
            return false;
        }

        return true;
    }
}
