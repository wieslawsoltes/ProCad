using ACadInspector.Rendering;

namespace ACadInspector.Services;

public interface IBlockPreviewRenderer
{
    byte[]? Render(CadRenderStateSnapshot snapshot, int size);
}
