using System.Collections.Generic;
using ACadSharp;
using ACadSharp.Tables;

namespace ACadInspector.Rendering;

public readonly struct RenderLayerOverride
{
    public RenderColor? Color { get; }
    public LineType? LineType { get; }
    public LineWeightType? LineWeight { get; }

    public RenderLayerOverride(RenderColor? color, LineType? lineType, LineWeightType? lineWeight)
    {
        Color = color;
        LineType = lineType;
        LineWeight = lineWeight;
    }
}

public sealed class RenderLayerOverrides
{
    private readonly Dictionary<Layer, RenderLayerOverride> _overrides;

    public RenderLayerOverrides(Dictionary<Layer, RenderLayerOverride> overrides)
    {
        _overrides = overrides;
    }

    public bool TryGetOverride(Layer layer, out RenderLayerOverride layerOverride)
    {
        return _overrides.TryGetValue(layer, out layerOverride);
    }

    public bool TryGetColor(Layer layer, out RenderColor color)
    {
        if (_overrides.TryGetValue(layer, out var layerOverride) && layerOverride.Color.HasValue)
        {
            color = layerOverride.Color.Value;
            return true;
        }

        color = default;
        return false;
    }

    public bool TryGetLineType(Layer layer, out LineType? lineType)
    {
        lineType = null;
        if (_overrides.TryGetValue(layer, out var layerOverride) && layerOverride.LineType is not null)
        {
            lineType = layerOverride.LineType;
            return true;
        }

        return false;
    }

    public bool TryGetLineWeight(Layer layer, out LineWeightType lineWeight)
    {
        lineWeight = LineWeightType.ByLayer;
        if (_overrides.TryGetValue(layer, out var layerOverride) && layerOverride.LineWeight.HasValue)
        {
            lineWeight = layerOverride.LineWeight.Value;
            return true;
        }

        return false;
    }
}
