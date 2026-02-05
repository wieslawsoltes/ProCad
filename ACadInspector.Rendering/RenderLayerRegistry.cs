using System.Collections.Generic;
using ACadSharp.Tables;

namespace ACadInspector.Rendering;

public sealed class RenderLayerRegistry
{
    private readonly Dictionary<Layer, RenderLayerBuilder> _layers =
        new(ReferenceEqualityComparer.Instance);
    private readonly IRenderStyleResolver _styleResolver;
    private readonly CadRenderSceneSettings _settings;
    private readonly RenderLayerOverrides? _overrides;
    private readonly Func<RenderPrimitiveMetadata?>? _metadataProvider;

    public RenderLayerRegistry(
        IRenderStyleResolver styleResolver,
        CadRenderSceneSettings settings,
        RenderSelectionContext? selectionContext = null,
        RenderLayerOverrides? overrides = null)
    {
        _styleResolver = styleResolver;
        _settings = settings;
        _metadataProvider = selectionContext is null ? null : selectionContext.CreateMetadata;
        _overrides = overrides;
    }

    public int Count => _layers.Count;

    public IEnumerable<RenderLayerBuilder> Builders => _layers.Values;

    public IEnumerable<KeyValuePair<Layer, RenderLayerBuilder>> Entries => _layers;

    public RenderLayerBuilder GetLayerBuilder(Layer layer)
    {
        if (_layers.TryGetValue(layer, out var builder))
        {
            return builder;
        }

        var color = _overrides is not null && _overrides.TryResolveColor(layer, _settings, out var overrideColor)
            ? overrideColor
            : _styleResolver.ResolveLayerColor(layer, _settings);
        builder = new RenderLayerBuilder(layer.Name, color, layer.IsOn, _metadataProvider);
        _layers[layer] = builder;
        return builder;
    }
}
