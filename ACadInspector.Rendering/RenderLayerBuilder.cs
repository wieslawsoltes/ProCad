using System;
using System.Collections.Generic;

namespace ACadInspector.Rendering;

public sealed class RenderLayerBuilder
{
    private static readonly IReadOnlyDictionary<IRenderPrimitive, RenderPrimitiveMetadata> EmptyMetadata =
        new Dictionary<IRenderPrimitive, RenderPrimitiveMetadata>(ReferenceEqualityComparer.Instance);

    private readonly Func<RenderPrimitiveMetadata?>? _metadataProvider;
    private Dictionary<IRenderPrimitive, RenderPrimitiveMetadata>? _metadata;

    public string Name { get; }
    public RenderColor Color { get; }
    public bool IsVisible { get; }
    public List<IRenderPrimitive> Primitives { get; } = new();
    public RenderBounds Bounds { get; private set; } = RenderBounds.Empty;

    public RenderLayerBuilder(
        string name,
        RenderColor color,
        bool isVisible,
        Func<RenderPrimitiveMetadata?>? metadataProvider = null)
    {
        Name = name;
        Color = color;
        IsVisible = isVisible;
        _metadataProvider = metadataProvider;
    }

    public void Add(IRenderPrimitive primitive)
    {
        Primitives.Add(primitive);
        Bounds = Bounds.Expand(primitive.Bounds);
        TrackMetadata(primitive, _metadataProvider?.Invoke());
    }

    public void Add(IRenderPrimitive primitive, RenderPrimitiveMetadata? metadata)
    {
        Primitives.Add(primitive);
        Bounds = Bounds.Expand(primitive.Bounds);
        TrackMetadata(primitive, metadata);
    }

    public void AddMetadata(IRenderPrimitive primitive, RenderPrimitiveMetadata metadata)
    {
        _metadata ??= new Dictionary<IRenderPrimitive, RenderPrimitiveMetadata>(ReferenceEqualityComparer.Instance);
        _metadata[primitive] = metadata;
    }

    public bool TryGetMetadata(IRenderPrimitive primitive, out RenderPrimitiveMetadata metadata)
    {
        if (_metadata is null)
        {
            metadata = default;
            return false;
        }

        return _metadata.TryGetValue(primitive, out metadata);
    }

    public IReadOnlyDictionary<IRenderPrimitive, RenderPrimitiveMetadata> Metadata => _metadata ?? EmptyMetadata;

    private void TrackMetadata(IRenderPrimitive primitive, RenderPrimitiveMetadata? metadata)
    {
        if (!metadata.HasValue)
        {
            return;
        }

        _metadata ??= new Dictionary<IRenderPrimitive, RenderPrimitiveMetadata>(ReferenceEqualityComparer.Instance);
        _metadata[primitive] = metadata.Value;
    }
}
