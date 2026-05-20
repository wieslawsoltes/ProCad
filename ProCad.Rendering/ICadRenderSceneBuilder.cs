using ACadSharp;

namespace ProCad.Rendering;

public interface ICadRenderSceneBuilder
{
    RenderScene Build(CadDocument document, CadRenderSceneSettings settings);
    RenderScene BuildBlock(CadDocument document, ACadSharp.Tables.BlockRecord block, CadRenderSceneSettings settings);
}
