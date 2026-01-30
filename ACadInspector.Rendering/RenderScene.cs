using System.Collections.Generic;

namespace ACadInspector.Rendering;

public sealed class RenderLayer
{
    public string Name { get; }
    public RenderColor Color { get; }
    public bool IsVisible { get; }
    public IReadOnlyList<IRenderPrimitive> Primitives { get; }
    public RenderBounds Bounds { get; }

    public RenderLayer(string name, RenderColor color, bool isVisible, IReadOnlyList<IRenderPrimitive> primitives, RenderBounds bounds)
    {
        Name = name;
        Color = color;
        IsVisible = isVisible;
        Primitives = primitives;
        Bounds = bounds;
    }
}

public sealed class RenderScene
{
    public RenderColor Background { get; }
    public RenderVisualStyle VisualStyle { get; }
    public RenderBounds Bounds { get; }
    public IReadOnlyList<RenderLayer> Layers { get; }
    public RenderDiagnostics Diagnostics { get; }
    public RenderStats Stats { get; }

    public RenderScene(
        IReadOnlyList<RenderLayer> layers,
        RenderBounds bounds,
        RenderColor background,
        RenderVisualStyle visualStyle,
        RenderDiagnostics diagnostics,
        RenderStats stats)
    {
        Layers = layers;
        Bounds = bounds;
        Background = background;
        VisualStyle = visualStyle;
        Diagnostics = diagnostics;
        Stats = stats;
    }
}
