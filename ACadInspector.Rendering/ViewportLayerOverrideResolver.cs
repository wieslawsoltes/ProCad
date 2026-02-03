using System;
using System.Collections.Generic;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;

namespace ACadInspector.Rendering;

internal static class ViewportLayerOverrideResolver
{
    private static readonly string[] OverrideKeys =
    [
        "ACAD_LAYEROVERRIDE",
        "ACAD_LAYEROVERRIDES",
        "AcDbLayerViewportOverride"
    ];

    public static RenderLayerOverrides? Resolve(CadDocument document, Viewport viewport)
    {
        if (document is null || viewport is null)
        {
            return null;
        }

        var overrides = new Dictionary<Layer, RenderLayerOverride>(ReferenceEqualityComparer.Instance);
        foreach (var layer in document.Layers)
        {
            if (TryResolveLayerOverride(layer, viewport, document, out var layerOverride))
            {
                overrides[layer] = layerOverride;
            }
        }

        return overrides.Count > 0 ? new RenderLayerOverrides(overrides) : null;
    }

    private static bool TryResolveLayerOverride(
        Layer layer,
        Viewport viewport,
        CadDocument document,
        out RenderLayerOverride layerOverride)
    {
        layerOverride = default;
        if (layer.XDictionary is null)
        {
            return false;
        }

        foreach (var key in OverrideKeys)
        {
            if (layer.XDictionary.TryGetEntry<XRecord>(key, out var record))
            {
                if (TryParseViewportOverride(record, viewport, document, out layerOverride))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryParseViewportOverride(
        XRecord record,
        Viewport viewport,
        CadDocument document,
        out RenderLayerOverride layerOverride)
    {
        layerOverride = default;
        var color = (RenderColor?)null;
        LineType? lineType = null;
        LineWeightType? lineWeight = null;
        var matchedViewport = false;

        foreach (var entry in record.Entries)
        {
            if (!matchedViewport)
            {
                if (MatchesViewport(entry.Value, viewport))
                {
                    matchedViewport = true;
                }

                continue;
            }

            switch (entry.Code)
            {
                case 62 when entry.Value is short index:
                    if (index > 0)
                    {
                        var cadColor = new ACadSharp.Color(index);
                        color = new RenderColor(cadColor.R, cadColor.G, cadColor.B, 255);
                    }
                    break;
                case 420 when entry.Value is int rgb:
                    if (rgb > 0)
                    {
                        var cadColor = ACadSharp.Color.FromTrueColor((uint)rgb);
                        color = new RenderColor(cadColor.R, cadColor.G, cadColor.B, 255);
                    }
                    break;
                case 6 when entry.Value is string lineTypeName:
                    if (document.LineTypes.TryGetValue(lineTypeName, out var resolved))
                    {
                        lineType = resolved;
                    }
                    break;
                case 370 when entry.Value is short weight:
                    lineWeight = (LineWeightType)weight;
                    break;
            }
        }

        if (color.HasValue || lineType is not null || lineWeight.HasValue)
        {
            layerOverride = new RenderLayerOverride(color, lineType, lineWeight);
            return true;
        }

        return false;
    }

    private static bool MatchesViewport(object? value, Viewport viewport)
    {
        if (value is null)
        {
            return false;
        }

        if (value is Viewport viewportValue)
        {
            return ReferenceEquals(viewportValue, viewport) || viewportValue.Handle == viewport.Handle;
        }

        if (value is ulong handle)
        {
            return handle == viewport.Handle;
        }

        if (value is long longHandle)
        {
            return longHandle == (long)viewport.Handle;
        }

        if (value is int intHandle)
        {
            return intHandle == (int)viewport.Handle;
        }

        return false;
    }
}
