using System;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using ACadSharp.Objects;

namespace ACadInspector.Rendering;

public sealed class RenderBuildContext
{
    public CadDocument Document { get; }
    public CadRenderSceneSettings Settings { get; }
    public IRenderStyleResolver StyleResolver { get; }
    public IRenderLinePatternResolver LinePatternResolver { get; }
    public IRenderShapeResolver ShapeResolver { get; }
    public IRenderTextShaper TextShaper { get; }
    public IRenderEntityVisibilityResolver VisibilityResolver { get; }
    public IRenderGeometrySampler GeometrySampler { get; }
    public IRenderEntityOrderResolver EntityOrderResolver { get; }
    public IRenderEntityDispatcher Dispatcher { get; }
    public RenderLayerRegistry Layers { get; }
    public RenderDiagnostics Diagnostics { get; }
    public RenderSelectionContext SelectionContext { get; }
    public RenderBlockContext BlockContext { get; }
    internal RenderStatsAccumulator Stats { get; }
    internal RenderLayerOverrides? ViewportOverrides { get; }

    public RenderBuildContext(
        CadDocument document,
        CadRenderSceneSettings settings,
        IRenderStyleResolver styleResolver,
        IRenderLinePatternResolver linePatternResolver,
        IRenderShapeResolver shapeResolver,
        IRenderTextShaper textShaper,
        IRenderEntityVisibilityResolver visibilityResolver,
        IRenderGeometrySampler geometrySampler,
        IRenderEntityOrderResolver entityOrderResolver,
        IRenderEntityDispatcher dispatcher,
        RenderDiagnostics diagnostics,
        RenderStatsAccumulator stats,
        RenderLayerOverrides? viewportOverrides = null,
        RenderSelectionContext? selectionContext = null,
        RenderBlockContext? blockContext = null)
    {
        Document = document;
        Settings = settings;
        StyleResolver = styleResolver;
        LinePatternResolver = linePatternResolver;
        ShapeResolver = shapeResolver;
        TextShaper = textShaper;
        VisibilityResolver = visibilityResolver;
        GeometrySampler = geometrySampler;
        EntityOrderResolver = entityOrderResolver;
        Dispatcher = dispatcher;
        ViewportOverrides = viewportOverrides;
        SelectionContext = selectionContext ?? new RenderSelectionContext();
        BlockContext = blockContext ?? new RenderBlockContext();
        Layers = new RenderLayerRegistry(styleResolver, settings, SelectionContext, viewportOverrides);
        Diagnostics = diagnostics;
        Stats = stats;
    }

    public RenderLayerBuilder GetLayerBuilder(Entity entity)
    {
        var layer = entity.Layer ?? Layer.Default;
        return Layers.GetLayerBuilder(layer);
    }

    public RenderColor ResolveEntityColor(Entity entity)
    {
        return ResolveEntityColor(entity, ownerDepth: 0);
    }

    private RenderColor ResolveEntityColor(Entity entity, int ownerDepth)
    {
        if (entity.Color.IsByBlock)
        {
            var owner = SelectionContext.GetOwnerOverride(ownerDepth);
            if (owner is Entity ownerEntity)
            {
                return ResolveEntityColor(ownerEntity, ownerDepth + 1);
            }
        }

        var color = StyleResolver.ResolveEntityColor(entity, Settings);
        if (ViewportOverrides is not null && entity.Layer is not null &&
            (entity.Color.IsByLayer || (entity.Color.IsByBlock && IsBlockByLayer(entity))))
        {
            if (ViewportOverrides.TryResolveColor(entity.Layer, Settings, out var overrideColor))
            {
                color = new RenderColor(overrideColor.R, overrideColor.G, overrideColor.B, color.A);
            }
        }

        if (ViewportOverrides is not null && entity.Layer is not null &&
            (entity.Transparency.IsByLayer || (entity.Transparency.IsByBlock && IsBlockTransparencyByLayer(entity))))
        {
            if (ViewportOverrides.TryGetTransparency(entity.Layer, out var overrideTransparency))
            {
                var alpha = RenderStyleUtils.ResolveTransparencyAlpha(overrideTransparency);
                color = new RenderColor(color.R, color.G, color.B, CombineAlpha(color.A, alpha));
            }
        }

        if (Settings.PlotStyleTable is not null)
        {
            color = ApplyPlotStyleColor(entity, color, Settings.PlotStyleTable);
        }

        color = RenderStyleUtils.ApplyBrightnessContrast(color, Settings);

        return color;
    }

    public float ResolveLineWeight(Entity entity)
    {
        var lineWeight = StyleResolver.ResolveLineWeight(entity, Settings);
        if (ViewportOverrides is not null &&
            (entity.LineWeight == LineWeightType.ByLayer || (entity.LineWeight == LineWeightType.ByBlock && IsBlockLineWeightByLayer(entity))))
        {
            if (entity.Layer is not null && ViewportOverrides.TryGetLineWeight(entity.Layer, out var overrideWeight))
            {
                lineWeight = RenderStyleUtils.ResolveLineWeight(overrideWeight, Settings);
            }
        }

        if (Settings.PlotStyleTable is not null)
        {
            lineWeight = ApplyPlotStyleLineWeight(entity, lineWeight, Settings.PlotStyleTable);
        }

        return lineWeight;
    }

    public RenderLinePattern ResolveLinePattern(Entity entity)
    {
        if (ViewportOverrides is not null && entity.LineType is not null &&
            (entity.LineType.Name.Equals(LineType.ByLayerName, StringComparison.OrdinalIgnoreCase) ||
             (entity.LineType.Name.Equals(LineType.ByBlockName, StringComparison.OrdinalIgnoreCase) && IsBlockLineTypeByLayer(entity))) &&
            entity.Layer is not null &&
            ViewportOverrides.TryGetLineType(entity.Layer, out var overrideType) &&
            overrideType is not null &&
            !overrideType.Name.Equals(LineType.ByLayerName, StringComparison.OrdinalIgnoreCase))
        {
            if (LinePatternResolver is DefaultRenderLinePatternResolver resolver)
            {
                return resolver.ResolveLinePattern(overrideType, entity.LineTypeScale, Document, Settings);
            }
        }

        return LinePatternResolver.ResolveLinePattern(entity, Document, Settings);
    }

    public RenderLineCap ResolveLineCap(Entity entity)
    {
        return StyleResolver.ResolveLineCap(entity, Settings);
    }

    public RenderLineJoin ResolveLineJoin(Entity entity)
    {
        return StyleResolver.ResolveLineJoin(entity, Settings);
    }

    /// <summary>
    /// Resolves material shading information for an entity.
    /// </summary>
    public RenderMaterial ResolveEntityMaterial(Entity entity)
    {
        var material = StyleResolver.ResolveEntityMaterial(entity, Settings);
        return RenderStyleUtils.ApplyBrightnessContrast(material, Settings);
    }

    private RenderColor ApplyPlotStyleColor(Entity entity, RenderColor color, RenderPlotStyleTable table)
    {
        if (TryResolvePlotStyle(entity, table, out var style))
        {
            var resolved = color;
            if (style.Color.HasValue)
            {
                var overrideColor = style.Color.Value;
                resolved = new RenderColor(overrideColor.R, overrideColor.G, overrideColor.B, resolved.A);
            }

            if (style.Screening.HasValue)
            {
                resolved = RenderStyleUtils.ApplyScreening(resolved, style.Screening.Value);
            }

            if (style.Transparency.HasValue)
            {
                var alpha = RenderStyleUtils.ApplyTransparency(resolved.A, style.Transparency.Value);
                resolved = new RenderColor(resolved.R, resolved.G, resolved.B, alpha);
            }

            return resolved;
        }

        return color;
    }

    private float ApplyPlotStyleLineWeight(Entity entity, float lineWeight, RenderPlotStyleTable table)
    {
        if (!Settings.DisplayLineWeight)
        {
            return 0f;
        }

        if (TryResolvePlotStyle(entity, table, out var style) && style.LineWeightMm.HasValue)
        {
            var weightMm = MathF.Max(Settings.MinLineWeightMm, style.LineWeightMm.Value);
            return weightMm / Settings.MillimetersPerUnit;
        }

        return lineWeight;
    }

    private bool TryResolvePlotStyle(Entity entity, RenderPlotStyleTable table, out RenderPlotStyle style)
    {
        style = default;
        if (table.IsNamed || IsNamedPlotStyleMode())
        {
            return TryResolveNamedPlotStyle(entity, table, out style);
        }

        var index = ResolvePlotStyleColorIndex(entity);
        if (index <= 0)
        {
            return false;
        }

        return table.TryGetByColorIndex(index, out style);
    }

    private bool TryResolveNamedPlotStyle(Entity entity, RenderPlotStyleTable table, out RenderPlotStyle style)
    {
        style = default;
        if (!IsNamedPlotStyleMode() || !table.IsNamed)
        {
            return false;
        }

        var name = ResolveNamedPlotStyleName(entity);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return table.TryGetByName(name, out style);
    }

    private string? ResolveNamedPlotStyleName(Entity entity)
    {
        var layer = entity.Layer;
        if (layer is null || Document?.RootDictionary is null)
        {
            return null;
        }

        if (ViewportOverrides is not null)
        {
            if (ViewportOverrides.TryGetPlotStyleName(layer, out var overrideName))
            {
                return overrideName;
            }

            if (ViewportOverrides.TryGetPlotStyleHandle(layer, out var overrideHandle) &&
                TryResolvePlotStyleNameFromDictionary(overrideHandle, out var overrideResolved))
            {
                return overrideResolved;
            }
        }

        var handle = layer.PlotStyleName;
        if (handle != 0 && TryResolvePlotStyleNameFromDictionary(handle, out var resolved))
        {
            return resolved;
        }

        return TryResolveDefaultPlotStyleName(out var defaultName) ? defaultName : null;
    }

    private bool IsNamedPlotStyleMode()
    {
        return Document?.Header?.PlotStyleMode != 0;
    }

    private int ResolvePlotStyleColorIndex(Entity entity)
    {
        if (ViewportOverrides is not null && entity.Layer is not null &&
            (entity.Color.IsByLayer || (entity.Color.IsByBlock && IsBlockByLayer(entity))) &&
            ViewportOverrides.TryGetColorIndex(entity.Layer, out var overrideIndex))
        {
            return overrideIndex;
        }

        return entity.GetActiveColor().Index;
    }

    private bool TryResolvePlotStyleNameFromDictionary(ulong handle, out string name)
    {
        name = string.Empty;
        if (Document?.RootDictionary is null)
        {
            return false;
        }

        if (!Document.RootDictionary.TryGetEntry(CadDictionary.AcadPlotStyleName, out CadDictionary plotStyles))
        {
            return false;
        }

        var handles = plotStyles.EntryHandles;
        var names = plotStyles.EntryNames;
        var count = Math.Min(handles.Length, names.Length);
        for (var i = 0; i < count; i++)
        {
            if (handles[i] == handle)
            {
                name = names[i];
                return !string.IsNullOrWhiteSpace(name);
            }
        }

        return false;
    }

    private bool TryResolveDefaultPlotStyleName(out string name)
    {
        name = string.Empty;
        if (Document?.RootDictionary is null)
        {
            return false;
        }

        if (!Document.RootDictionary.TryGetEntry(CadDictionary.AcadPlotStyleName, out CadDictionary plotStyles))
        {
            return false;
        }

        if (plotStyles is CadDictionaryWithDefault withDefault &&
            withDefault.DefaultEntry is NonGraphicalObject defaultEntry &&
            !string.IsNullOrWhiteSpace(defaultEntry.Name))
        {
            name = defaultEntry.Name;
            return true;
        }

        return false;
    }

    private static bool IsBlockByLayer(Entity entity)
    {
        return entity.Owner is BlockRecord record && record.BlockEntity is not null &&
               record.BlockEntity.Color.IsByLayer;
    }

    private static bool IsBlockLineTypeByLayer(Entity entity)
    {
        return entity.Owner is BlockRecord record && record.BlockEntity is not null &&
               record.BlockEntity.LineType is not null &&
               record.BlockEntity.LineType.Name.Equals(LineType.ByLayerName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlockLineWeightByLayer(Entity entity)
    {
        return entity.Owner is BlockRecord record && record.BlockEntity is not null &&
               record.BlockEntity.LineWeight == LineWeightType.ByLayer;
    }

    private static bool IsBlockTransparencyByLayer(Entity entity)
    {
        return entity.Owner is BlockRecord record && record.BlockEntity is not null &&
               record.BlockEntity.Transparency.IsByLayer;
    }

    private static byte CombineAlpha(byte baseAlpha, byte overlayAlpha)
    {
        if (overlayAlpha >= 255)
        {
            return baseAlpha;
        }

        var combined = baseAlpha * overlayAlpha / 255f;
        return (byte)Math.Clamp((int)Math.Round(combined), 0, 255);
    }
}
