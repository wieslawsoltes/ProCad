namespace ProCad.Rendering;

public sealed class RenderOverlayScene
{
    public static readonly RenderOverlayScene Empty = new(Array.Empty<RenderOverlayPrimitive>());

    public IReadOnlyList<RenderOverlayPrimitive> Primitives { get; }

    public RenderOverlayScene(IReadOnlyList<RenderOverlayPrimitive> primitives)
    {
        Primitives = primitives ?? throw new ArgumentNullException(nameof(primitives));
    }
}
