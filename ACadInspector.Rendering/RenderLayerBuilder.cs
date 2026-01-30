using System.Collections.Generic;

namespace ACadInspector.Rendering;

public sealed class RenderLayerBuilder
{
    public string Name { get; }
    public RenderColor Color { get; }
    public bool IsVisible { get; }
    public List<IRenderPrimitive> Primitives { get; } = new();
    public RenderBounds Bounds { get; private set; } = RenderBounds.Empty;

    public RenderLayerBuilder(string name, RenderColor color, bool isVisible)
    {
        Name = name;
        Color = color;
        IsVisible = isVisible;
    }

    public void Add(IRenderPrimitive primitive)
    {
        Primitives.Add(primitive);
        Bounds = Bounds.Expand(primitive.Bounds);
    }
}
