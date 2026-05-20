using ACadSharp.Entities;
using ACadSharp.Tables;

namespace ProCad.Rendering;

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

        var typeOverrides = settings.EntityTypeVisibilityOverrides;
        if (typeOverrides is not null)
        {
            var typeName = entity.GetType().Name;
            if (typeOverrides.TryGetValue(typeName, out var isVisible) && !isVisible)
            {
                return false;
            }
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
