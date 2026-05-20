using ProCad.Rendering;

namespace ProCad.Services;

public sealed class CadRenderFocusRequest
{
    public RenderBounds Bounds { get; }
    public double Padding { get; }

    public CadRenderFocusRequest(RenderBounds bounds, double padding)
    {
        Bounds = bounds;
        Padding = padding;
    }
}
