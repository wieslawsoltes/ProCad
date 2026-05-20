using ACadSharp.Entities;

namespace ProCad.Rendering;

public interface IRenderTextShaper
{
    /// <summary>
    /// Shapes a single-line text entity into layout metrics.
    /// </summary>
    RenderTextLayout Shape(TextEntity text, CadRenderSceneSettings settings);
    /// <summary>
    /// Shapes a multi-line text entity into layout metrics.
    /// </summary>
    RenderTextLayout Shape(MText text, CadRenderSceneSettings settings);
}
