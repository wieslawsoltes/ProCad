using ProCad.Rendering;

namespace ProCad.Rendering.Backends;

public sealed class SkiaRenderBackendFactory : IRenderBackendFactory
{
    public static readonly SkiaRenderBackendFactory Instance = new();

    public IRenderBackend Create()
    {
        return new SkiaRenderBackend();
    }
}
