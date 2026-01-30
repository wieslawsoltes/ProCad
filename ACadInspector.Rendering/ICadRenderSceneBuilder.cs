using ACadSharp;

namespace ACadInspector.Rendering;

public interface ICadRenderSceneBuilder
{
    RenderScene Build(CadDocument document, CadRenderSceneSettings settings);
}
