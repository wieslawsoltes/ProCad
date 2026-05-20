using System.Collections.Generic;
using ACadSharp;
using ACadSharp.Tables;

namespace ProCad.Rendering;

public readonly struct RenderLayerOverride
{
    public RenderColor? Color { get; }
    public int? ColorIndex { get; }
    public LineType? LineType { get; }
    public LineWeightType? LineWeight { get; }
    public Transparency? Transparency { get; }
    public string? PlotStyleName { get; }
    public ulong? PlotStyleHandle { get; }

    public RenderLayerOverride(
        RenderColor? color,
        int? colorIndex,
        LineType? lineType,
        LineWeightType? lineWeight,
        Transparency? transparency = null,
        string? plotStyleName = null,
        ulong? plotStyleHandle = null)
    {
        Color = color;
        ColorIndex = colorIndex;
        LineType = lineType;
        LineWeight = lineWeight;
        Transparency = transparency;
        PlotStyleName = plotStyleName;
        PlotStyleHandle = plotStyleHandle;
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

    public bool TryResolveColor(Layer layer, CadRenderSceneSettings settings, out RenderColor color)
    {
        if (_overrides.TryGetValue(layer, out var layerOverride))
        {
            if (layerOverride.Color.HasValue)
            {
                color = layerOverride.Color.Value;
                return true;
            }

            if (layerOverride.ColorIndex.HasValue && layerOverride.ColorIndex.Value > 0)
            {
                var cadColor = new ACadSharp.Color((short)layerOverride.ColorIndex.Value);
                color = RenderStyleUtils.ResolveColor(cadColor, settings, 255);
                return true;
            }
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

    public bool TryGetColorIndex(Layer layer, out int colorIndex)
    {
        colorIndex = 0;
        if (_overrides.TryGetValue(layer, out var layerOverride) && layerOverride.ColorIndex.HasValue)
        {
            colorIndex = layerOverride.ColorIndex.Value;
            return colorIndex > 0;
        }

        return false;
    }

    public bool TryGetPlotStyleName(Layer layer, out string name)
    {
        name = string.Empty;
        if (_overrides.TryGetValue(layer, out var layerOverride) && !string.IsNullOrWhiteSpace(layerOverride.PlotStyleName))
        {
            name = layerOverride.PlotStyleName!;
            return true;
        }

        return false;
    }

    public bool TryGetPlotStyleHandle(Layer layer, out ulong handle)
    {
        handle = 0;
        if (_overrides.TryGetValue(layer, out var layerOverride) && layerOverride.PlotStyleHandle.HasValue)
        {
            handle = layerOverride.PlotStyleHandle.Value;
            return handle != 0;
        }

        return false;
    }

    public bool TryGetTransparency(Layer layer, out Transparency transparency)
    {
        transparency = Transparency.ByLayer;
        if (_overrides.TryGetValue(layer, out var layerOverride) && layerOverride.Transparency.HasValue)
        {
            transparency = layerOverride.Transparency.Value;
            return true;
        }

        return false;
    }
}
