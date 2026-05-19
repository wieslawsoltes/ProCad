namespace ProCad.Rendering;

public static class RenderBackendRegistry
{
    public static IRenderBackendFactory? Factory { get; set; }
}
