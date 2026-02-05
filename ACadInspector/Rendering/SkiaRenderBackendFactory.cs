using ACadInspector.Rendering;

namespace ACadInspector.Rendering.Backends;

public sealed class SkiaRenderBackendFactory : IRenderBackendFactory
{
    public static readonly SkiaRenderBackendFactory Instance = new();

    public IRenderBackend Create()
    {
        return new SkiaRenderBackend();
    }
}
