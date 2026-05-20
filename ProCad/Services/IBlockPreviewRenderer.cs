using ProCad.Rendering;

namespace ProCad.Services;

public interface IBlockPreviewRenderer
{
    byte[]? Render(CadRenderStateSnapshot snapshot, int size);
}
