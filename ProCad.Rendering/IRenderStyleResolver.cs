using ACadSharp.Entities;
using ACadSharp.Tables;

namespace ProCad.Rendering;

public interface IRenderStyleResolver
{
    RenderColor ResolveEntityColor(Entity entity, CadRenderSceneSettings settings);
    RenderColor ResolveLayerColor(Layer layer, CadRenderSceneSettings settings);
    float ResolveLineWeight(Entity entity, CadRenderSceneSettings settings);
    RenderLineCap ResolveLineCap(Entity entity, CadRenderSceneSettings settings);
    RenderLineJoin ResolveLineJoin(Entity entity, CadRenderSceneSettings settings);
    /// <summary>
    /// Resolves the material shading settings for an entity.
    /// </summary>
    RenderMaterial ResolveEntityMaterial(Entity entity, CadRenderSceneSettings settings);
}
