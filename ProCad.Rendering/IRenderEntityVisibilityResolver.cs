using ACadSharp.Entities;

namespace ProCad.Rendering;

public interface IRenderEntityVisibilityResolver
{
    bool ShouldRender(Entity entity, CadRenderSceneSettings settings);
}
