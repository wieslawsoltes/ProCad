using System.Collections.Generic;
using ACadSharp.Tables;

namespace ACadInspector.Rendering;

public sealed class RenderLayerRegistry
{
    private readonly Dictionary<Layer, RenderLayerBuilder> _layers =
        new(ReferenceEqualityComparer.Instance);
    private readonly IRenderStyleResolver _styleResolver;
    private readonly CadRenderSceneSettings _settings;

    public RenderLayerRegistry(IRenderStyleResolver styleResolver, CadRenderSceneSettings settings)
    {
        _styleResolver = styleResolver;
        _settings = settings;
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

        var color = _styleResolver.ResolveLayerColor(layer, _settings);
        builder = new RenderLayerBuilder(layer.Name, color, layer.IsOn);
        _layers[layer] = builder;
        return builder;
    }
}
