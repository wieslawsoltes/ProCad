using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;

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
    internal RenderStatsAccumulator Stats { get; }

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
        RenderStatsAccumulator stats)
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
        Layers = new RenderLayerRegistry(styleResolver, settings);
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
        return StyleResolver.ResolveEntityColor(entity, Settings);
    }

    public float ResolveLineWeight(Entity entity)
    {
        return StyleResolver.ResolveLineWeight(entity, Settings);
    }

    public RenderLinePattern ResolveLinePattern(Entity entity)
    {
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
        return StyleResolver.ResolveEntityMaterial(entity, Settings);
    }
}
