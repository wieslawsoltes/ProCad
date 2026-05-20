using System.Collections.Generic;

namespace ProCad.Rendering;

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
    private static readonly IReadOnlyDictionary<IRenderPrimitive, RenderPrimitiveMetadata> EmptyMetadata =
        new Dictionary<IRenderPrimitive, RenderPrimitiveMetadata>(ReferenceEqualityComparer.Instance);

    public RenderColor Background { get; }
    public RenderVisualStyle VisualStyle { get; }
    public RenderHiddenLineSettings HiddenLineSettings { get; }
    public RenderBounds Bounds { get; }
    public IReadOnlyList<RenderLayer> Layers { get; }
    public bool IsPaperSpace { get; }
    public RenderBounds? PaperBounds { get; }
    public RenderColor? PaperColor { get; }
    /// <summary>
    /// Gets the spatial index for fast hit testing.
    /// </summary>
    public RenderSpatialIndex SpatialIndex { get; }
    /// <summary>
    /// Gets the metadata map for render primitives.
    /// </summary>
    public IReadOnlyDictionary<IRenderPrimitive, RenderPrimitiveMetadata> PrimitiveMetadata { get; }
    public RenderDiagnostics Diagnostics { get; }
    public RenderStats Stats { get; }

    public RenderScene(
        IReadOnlyList<RenderLayer> layers,
        RenderBounds bounds,
        RenderColor background,
        RenderVisualStyle visualStyle,
        RenderHiddenLineSettings hiddenLineSettings,
        RenderSpatialIndex spatialIndex,
        IReadOnlyDictionary<IRenderPrimitive, RenderPrimitiveMetadata>? primitiveMetadata,
        RenderDiagnostics diagnostics,
        RenderStats stats,
        bool isPaperSpace = false,
        RenderBounds? paperBounds = null,
        RenderColor? paperColor = null)
    {
        Layers = layers;
        Bounds = bounds;
        Background = background;
        VisualStyle = visualStyle;
        HiddenLineSettings = hiddenLineSettings;
        SpatialIndex = spatialIndex ?? RenderSpatialIndex.Empty;
        PrimitiveMetadata = primitiveMetadata ?? EmptyMetadata;
        Diagnostics = diagnostics;
        Stats = stats;
        IsPaperSpace = isPaperSpace;
        PaperBounds = paperBounds;
        PaperColor = paperColor;
    }
}
