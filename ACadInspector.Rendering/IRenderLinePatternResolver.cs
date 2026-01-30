using ACadSharp;
using ACadSharp.Entities;

namespace ACadInspector.Rendering;

public interface IRenderLinePatternResolver
{
    /// <summary>
    /// Resolves the line pattern for an entity, including document scale settings.
    /// </summary>
    RenderLinePattern ResolveLinePattern(Entity entity, CadDocument document, CadRenderSceneSettings settings);
}
