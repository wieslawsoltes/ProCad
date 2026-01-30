using ACadSharp.Entities;

namespace ACadInspector.Rendering;

public interface IRenderEntityVisibilityResolver
{
    bool ShouldRender(Entity entity, CadRenderSceneSettings settings);
}
