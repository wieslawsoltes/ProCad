using System;
using System.Collections.Generic;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;

namespace ProCad.Rendering;

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
        int? colorIndex = null;
        LineType? lineType = null;
        LineWeightType? lineWeight = null;
        Transparency? transparency = null;
        string? plotStyleName = null;
        ulong? plotStyleHandle = null;
        var matchedViewport = false;

        foreach (var entry in record.Entries)
        {
            if (IsViewportMarker(entry))
            {
                var isTarget = MatchesViewport(entry.Value, viewport);
                if (matchedViewport && !isTarget)
                {
                    break;
                }

                if (isTarget)
                {
                    matchedViewport = true;
                }

                continue;
            }

            if (!matchedViewport)
            {
                continue;
            }

            switch (entry.Code)
            {
                case 62:
                    if (TryResolveColorIndex(entry.Value, out var resolvedIndex))
                    {
                        colorIndex = resolvedIndex;
                    }
                    break;
                case 420 when entry.Value is int rgb:
                    if (rgb > 0)
                    {
                        var cadColor = ACadSharp.Color.FromTrueColor((uint)rgb);
                        color = new RenderColor(cadColor.R, cadColor.G, cadColor.B, 255);
                    }
                    break;
                case 1 when entry.Value is string name:
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        plotStyleName = name;
                    }
                    break;
                case 6 when entry.Value is string lineTypeName:
                    if (document.LineTypes.TryGetValue(lineTypeName, out var resolved))
                    {
                        lineType = resolved;
                    }
                    break;
                case 390:
                    if (TryResolveHandle(entry.Value, out var handle))
                    {
                        plotStyleHandle = handle;
                    }
                    break;
                case 370 when entry.Value is short weight:
                    lineWeight = (LineWeightType)weight;
                    break;
                case 440:
                    if (TryResolveTransparency(entry.Value, out var resolvedTransparency))
                    {
                        transparency = resolvedTransparency;
                    }
                    break;
                case 441:
                    if (TryResolveTransparency(entry.Value, out var resolvedAlternateTransparency))
                    {
                        transparency = resolvedAlternateTransparency;
                    }
                    break;
            }
        }

        if (color.HasValue || colorIndex.HasValue || lineType is not null || lineWeight.HasValue ||
            transparency.HasValue || !string.IsNullOrWhiteSpace(plotStyleName) || plotStyleHandle.HasValue)
        {
            layerOverride = new RenderLayerOverride(color, colorIndex, lineType, lineWeight, transparency, plotStyleName, plotStyleHandle);
            return true;
        }

        return false;
    }

    private static bool IsViewportMarker(XRecord.Entry entry)
    {
        if (entry.Value is Viewport)
        {
            return true;
        }

        if (!entry.HasLinkedObject)
        {
            return false;
        }

        return entry.Value is ulong or long or int;
    }

    private static bool MatchesViewport(object? value, Viewport viewport)
    {
        if (value is null)
        {
            return false;
        }

        if (value is Viewport viewportValue)
        {
            if (ReferenceEquals(viewportValue, viewport))
            {
                return true;
            }

            if (viewport.Handle != 0 && viewportValue.Handle != 0)
            {
                return viewportValue.Handle == viewport.Handle;
            }

            return false;
        }

        if (value is ulong handle)
        {
            return viewport.Handle != 0 && handle == viewport.Handle;
        }

        if (value is long longHandle)
        {
            return viewport.Handle != 0 && longHandle == (long)viewport.Handle;
        }

        if (value is int intHandle)
        {
            return viewport.Handle != 0 && intHandle == (int)viewport.Handle;
        }

        return false;
    }

    private static bool TryResolveTransparency(object? value, out Transparency transparency)
    {
        transparency = Transparency.ByLayer;
        if (value is null)
        {
            return false;
        }

        if (value is Transparency transparencyValue)
        {
            transparency = transparencyValue;
            return true;
        }

        if (value is int intValue)
        {
            transparency = Transparency.FromAlphaValue(intValue);
            return true;
        }

        if (value is short shortValue)
        {
            transparency = Transparency.FromAlphaValue(shortValue);
            return true;
        }

        if (value is long longValue)
        {
            transparency = Transparency.FromAlphaValue((int)longValue);
            return true;
        }

        return false;
    }

    private static bool TryResolveColorIndex(object? value, out int index)
    {
        index = 0;
        if (value is null)
        {
            return false;
        }

        if (value is short shortValue)
        {
            index = shortValue;
            return index > 0;
        }

        if (value is int intValue)
        {
            index = intValue;
            return index > 0;
        }

        if (value is long longValue)
        {
            if (longValue <= 0 || longValue > int.MaxValue)
            {
                return false;
            }

            index = (int)longValue;
            return index > 0;
        }

        return false;
    }

    private static bool TryResolveHandle(object? value, out ulong handle)
    {
        handle = 0;
        if (value is null)
        {
            return false;
        }

        if (value is ulong ulongValue)
        {
            handle = ulongValue;
            return handle != 0;
        }

        if (value is long longValue)
        {
            if (longValue <= 0)
            {
                return false;
            }

            handle = (ulong)longValue;
            return true;
        }

        if (value is int intValue)
        {
            if (intValue <= 0)
            {
                return false;
            }

            handle = (ulong)intValue;
            return true;
        }

        if (value is ACadSharp.CadObject cadObject && cadObject.Handle != 0)
        {
            handle = cadObject.Handle;
            return true;
        }

        return false;
    }
}
