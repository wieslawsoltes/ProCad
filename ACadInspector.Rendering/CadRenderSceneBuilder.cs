using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Header;
using ACadSharp.Objects;
using ACadSharp.Objects.Evaluations;
using ACadSharp.Tables;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class CadRenderSceneBuilder : ICadRenderSceneBuilder
{
    private static readonly Transform IdentityTransform = new(Matrix4.Identity);

    private readonly IRenderEntityDispatcher _dispatcher;
    private readonly IRenderStyleResolver _styleResolver;
    private readonly IRenderLinePatternResolver _linePatternResolver;
    private readonly IRenderShapeResolver _shapeResolver;
    private readonly IRenderTextShaper _textShaper;
    private readonly IRenderEntityVisibilityResolver _visibilityResolver;
    private readonly IRenderGeometrySampler _geometrySampler;
    private readonly IRenderEntityOrderResolver _orderResolver;
    private readonly IRenderCacheStampProvider _cacheStampProvider;

    public CadRenderSceneBuilder(
        IRenderEntityDispatcher dispatcher,
        IRenderStyleResolver styleResolver,
        IRenderLinePatternResolver linePatternResolver,
        IRenderShapeResolver shapeResolver,
        IRenderTextShaper textShaper,
        IRenderEntityVisibilityResolver visibilityResolver,
        IRenderGeometrySampler geometrySampler,
        IRenderEntityOrderResolver orderResolver,
        IRenderCacheStampProvider cacheStampProvider)
    {
        _dispatcher = dispatcher;
        _styleResolver = styleResolver;
        _linePatternResolver = linePatternResolver;
        _shapeResolver = shapeResolver;
        _textShaper = textShaper;
        _visibilityResolver = visibilityResolver;
        _geometrySampler = geometrySampler;
        _orderResolver = orderResolver;
        _cacheStampProvider = cacheStampProvider;
    }

    public RenderScene Build(CadDocument document, CadRenderSceneSettings settings)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var stopwatch = Stopwatch.StartNew();
        var stats = new RenderStatsAccumulator();
        var diagnostics = new RenderDiagnostics();
        if (settings.IsPaperSpace)
        {
            var layout = ResolvePaperSpaceLayout(document, settings.LayoutName);
            var layoutScaling = ResolvePaperSpaceLineTypeScaling(layout);
            var paperSettings = WithViewportScale(settings, viewportScale: 1f, layoutScaling, annotationScaleFactor: 1f);
            var context = new RenderBuildContext(
                document,
                paperSettings,
                _styleResolver,
                _linePatternResolver,
                _shapeResolver,
                _textShaper,
                _visibilityResolver,
                _geometrySampler,
                _orderResolver,
                _dispatcher,
                diagnostics,
                stats);

            BuildPaperSpace(document, layout, paperSettings, context, diagnostics);
            return BuildScene(context, stats, stopwatch);
        }

        var modelContext = new RenderBuildContext(
            document,
            settings,
            _styleResolver,
            _linePatternResolver,
            _shapeResolver,
            _textShaper,
            _visibilityResolver,
            _geometrySampler,
            _orderResolver,
            _dispatcher,
            diagnostics,
            stats);
        AppendEntities(document.Entities, document.ModelSpace, IdentityTransform, modelContext);
        return BuildScene(modelContext, stats, stopwatch);
    }

    public RenderScene BuildBlock(CadDocument document, BlockRecord block, CadRenderSceneSettings settings)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (block is null)
        {
            throw new ArgumentNullException(nameof(block));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var stopwatch = Stopwatch.StartNew();
        var stats = new RenderStatsAccumulator();
        var diagnostics = new RenderDiagnostics();
        var context = new RenderBuildContext(
            document,
            settings,
            _styleResolver,
            _linePatternResolver,
            _shapeResolver,
            _textShaper,
            _visibilityResolver,
            _geometrySampler,
            _orderResolver,
            _dispatcher,
            diagnostics,
            stats);

        var basePoint = block.BlockEntity?.BasePoint ?? XYZ.Zero;
        var offset = new XYZ(-basePoint.X, -basePoint.Y, -basePoint.Z);
        var transform = new Transform(Matrix4.CreateTranslation(offset));
        using var rootScope = context.BlockContext.EnterRoot(block);
        var overrideSet = settings.DynamicBlockOverrideProvider?.GetBlockOverrides(block);
        var propertyProvider = DynamicBlockPropertyProvider.Create(null, overrideSet);
        var actionMap = DynamicBlockActionResolver.TryCreate(block, propertyProvider);
        var visibilityFilter = ResolveVisibilityFilter(block, settings.DynamicBlockVisibilityStateName ?? overrideSet?.VisibilityStateName);
        AppendBlockEntities(block, transform, context, visibilityFilter, actionMap);
        return BuildScene(context, stats, stopwatch);
    }

    private void AppendBlockEntities(
        BlockRecord block,
        Transform transform,
        RenderBuildContext context,
        DynamicBlockVisibilityFilter? visibilityFilter,
        DynamicBlockActionMap? actionMap)
    {
        var ordered = context.EntityOrderResolver.OrderEntities(block.Entities, block);
        foreach (var child in ordered)
        {
            if (child is null)
            {
                continue;
            }

            if (visibilityFilter is not null && !visibilityFilter.IsVisible(child))
            {
                continue;
            }

            if (!context.VisibilityResolver.ShouldRender(child, context.Settings))
            {
                continue;
            }

            var childTransform = transform;
            if (actionMap is not null && actionMap.TryGetTransform(child, out var actionTransform))
            {
                childTransform = RenderTransformUtils.Combine(transform, actionTransform);
            }

            context.Dispatcher.Append(child, childTransform, context);
        }
    }

    private static DynamicBlockVisibilityFilter? ResolveVisibilityFilter(
        BlockRecord block,
        string? stateName)
    {
        if (block?.EvaluationGraph is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(stateName))
        {
            return null;
        }

        var parameter = ResolveVisibilityParameter(block);
        if (parameter is null)
        {
            return null;
        }

        if (!parameter.States.TryGetValue(stateName, out var state))
        {
            return null;
        }

        return new DynamicBlockVisibilityFilter(parameter, state);
    }

    private static BlockVisibilityParameter? ResolveVisibilityParameter(BlockRecord block)
    {
        var graph = block.EvaluationGraph;
        if (graph is null)
        {
            return null;
        }

        foreach (var node in graph.Nodes)
        {
            if (node?.Expression is BlockVisibilityParameter parameter)
            {
                return parameter;
            }
        }

        return null;
    }

    private void BuildPaperSpace(
        CadDocument document,
        Layout? layout,
        CadRenderSceneSettings settings,
        RenderBuildContext context,
        RenderDiagnostics diagnostics)
    {
        var paperSpace = ResolvePaperSpaceBlock(document, layout);
        if (paperSpace is null)
        {
            return;
        }

        var viewports = ResolveViewports(layout, paperSpace);
        foreach (var viewport in viewports)
        {
            if (!ShouldRenderViewport(viewport))
            {
                continue;
            }

            var viewportScale = NormalizeScale(viewport.ScaleFactor);
            var viewportSettings = WithViewportScale(
                settings,
                viewportScale,
                settings.PaperSpaceLineTypeScalingOverride,
                settings.AnnotationScaleFactor);
            var layerOverrides = ViewportLayerOverrideResolver.Resolve(document, viewport);
            var viewportContext = new RenderBuildContext(
                document,
                viewportSettings,
                _styleResolver,
                _linePatternResolver,
                _shapeResolver,
                _textShaper,
                _visibilityResolver,
                _geometrySampler,
                _orderResolver,
                _dispatcher,
                diagnostics,
                context.Stats,
                layerOverrides);

            var transform = BuildViewportTransform(viewport);
            var viewportBlock = ResolveViewportBlock(document, viewport);
            var viewportEntities = ResolveViewportEntities(document, viewport);
            var frozenLayers = ResolveViewportFrozenLayers(viewport);
            if (frozenLayers.Count > 0)
            {
                viewportEntities = FilterFrozenLayers(viewportEntities, frozenLayers);
            }

            AppendEntities(viewportEntities, viewportBlock, transform, viewportContext);

            var clipLoops = BuildViewportClip(viewport, settings);
            foreach (var entry in viewportContext.Layers.Entries)
            {
                var builder = entry.Value;
                if (builder.Primitives.Count == 0)
                {
                    continue;
                }

                var targetBuilder = context.Layers.GetLayerBuilder(entry.Key);
                targetBuilder.Add(new RenderClipGroup(clipLoops, builder.Primitives, RenderLoopFillMode.NonZero));
                foreach (var kvp in builder.Metadata)
                {
                    targetBuilder.AddMetadata(kvp.Key, kvp.Value);
                }
            }
        }

        AppendEntities(paperSpace.Entities, paperSpace, IdentityTransform, context);
    }

    private void AppendEntities(
        IEnumerable<Entity> entities,
        BlockRecord? block,
        Transform transform,
        RenderBuildContext context)
    {
        var ordered = context.EntityOrderResolver.OrderEntities(entities, block);
        foreach (var entity in ordered)
        {
            AppendEntity(entity, transform, context);
        }
    }

    private void AppendEntity(Entity entity, Transform transform, RenderBuildContext context)
    {
        if (entity is null)
        {
            return;
        }

        var shouldRender = context.VisibilityResolver.ShouldRender(entity, context.Settings);
        context.Stats.TrackEntity(shouldRender);
        if (!shouldRender)
        {
            return;
        }

        context.Dispatcher.Append(entity, transform, context);
    }

    private static RenderScene BuildScene(
        RenderBuildContext context,
        RenderStatsAccumulator stats,
        Stopwatch stopwatch)
    {
        var builders = new List<RenderLayerBuilder>(context.Layers.Count);
        foreach (var builder in context.Layers.Builders)
        {
            builders.Add(builder);
        }

        builders.Sort(static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));

        var layers = new List<RenderLayer>(builders.Count);
        var sceneBounds = RenderBounds.Empty;
        var metadata = new Dictionary<IRenderPrimitive, RenderPrimitiveMetadata>(ReferenceEqualityComparer.Instance);
        foreach (var builder in builders)
        {
            var layer = new RenderLayer(builder.Name, builder.Color, builder.IsVisible, builder.Primitives, builder.Bounds);
            layers.Add(layer);
            sceneBounds = sceneBounds.Expand(builder.Bounds);

            foreach (var kvp in builder.Metadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        var spatialIndex = RenderSpatialIndex.Build(layers);
        stopwatch.Stop();
        var renderStats = RenderStats.Build(stats, layers, stopwatch.Elapsed, context.Settings.PerformanceBudget);
        return new RenderScene(
            layers,
            sceneBounds,
            context.Settings.Background,
            context.Settings.VisualStyle,
            context.Settings.HiddenLineSettings,
            spatialIndex,
            metadata,
            context.Diagnostics,
            renderStats);
    }

    private static Layout? ResolvePaperSpaceLayout(CadDocument document, string? layoutName)
    {
        if (!string.IsNullOrWhiteSpace(layoutName) && document.Layouts is not null)
        {
            foreach (var layout in document.Layouts)
            {
                if (!layout.IsPaperSpace)
                {
                    continue;
                }

                if (string.Equals(layout.Name, layoutName, StringComparison.OrdinalIgnoreCase))
                {
                    return layout;
                }
            }
        }

        if (document.Layouts is not null)
        {
            Layout? best = null;
            foreach (var layout in document.Layouts)
            {
                if (!layout.IsPaperSpace)
                {
                    continue;
                }

                if (best is null || layout.TabOrder < best.TabOrder)
                {
                    best = layout;
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        return document.PaperSpace?.Layout;
    }

    private static BlockRecord? ResolvePaperSpaceBlock(CadDocument document, Layout? layout)
    {
        if (layout?.AssociatedBlock is not null)
        {
            return layout.AssociatedBlock;
        }

        return document.PaperSpace;
    }

    private static List<Viewport> ResolveViewports(Layout? layout, BlockRecord? paperSpace)
    {
        var result = new List<Viewport>();
        IEnumerable<Viewport> candidates = Array.Empty<Viewport>();
        if (layout?.Viewports is not null)
        {
            candidates = layout.Viewports;
        }
        else if (paperSpace is not null)
        {
            candidates = paperSpace.Viewports;
        }

        foreach (var viewport in candidates)
        {
            result.Add(viewport);
        }

        return result;
    }

    private static SpaceLineTypeScaling? ResolvePaperSpaceLineTypeScaling(Layout? layout)
    {
        if (layout is null)
        {
            return null;
        }

        return layout.LayoutFlags.HasFlag(LayoutFlags.PaperSpaceLinetypeScaling)
            ? SpaceLineTypeScaling.Normal
            : SpaceLineTypeScaling.Viewport;
    }

    private static bool ShouldRenderViewport(Viewport viewport)
    {
        if (viewport.RepresentsPaper)
        {
            return false;
        }

        if (viewport.Status.HasFlag(ViewportStatusFlags.ViewportOff))
        {
            return false;
        }

        return viewport.Width > 0 && viewport.Height > 0 && viewport.ViewHeight > 0;
    }

    private static IEnumerable<Entity> ResolveViewportEntities(CadDocument document, Viewport viewport)
    {
        if (viewport.Document is not null)
        {
            return viewport.SelectEntities();
        }

        return document.Entities;
    }

    private static BlockRecord? ResolveViewportBlock(CadDocument document, Viewport viewport)
    {
        if (viewport.Document is not null)
        {
            return viewport.Document.ModelSpace;
        }

        return document.ModelSpace;
    }

    private static Transform BuildViewportTransform(Viewport viewport)
    {
        var viewHeight = viewport.ViewHeight;
        var viewWidth = viewport.ViewWidth;
        var scaleX = viewWidth > 0.0 ? viewport.Width / viewWidth : 1.0;
        var scaleY = viewHeight > 0.0 ? viewport.Height / viewHeight : 1.0;
        var twist = -viewport.TwistAngle;

        var viewDirection = RenderTransformUtils.NormalizeNormal(viewport.ViewDirection);
        var worldToView = Matrix4.GetArbitraryAxis(viewDirection);
        if (!Matrix4.Inverse(worldToView, out var viewToDcs))
        {
            viewToDcs = Matrix4.Identity;
        }

        var matrix = Matrix4.CreateTranslation(new XYZ(-viewport.ViewTarget.X, -viewport.ViewTarget.Y, -viewport.ViewTarget.Z));
        matrix = viewToDcs * matrix;
        matrix = Matrix4.CreateTranslation(new XYZ(-viewport.ViewCenter.X, -viewport.ViewCenter.Y, 0)) * matrix;
        if (Math.Abs(twist) > 0.0001)
        {
            matrix = Matrix4.CreateRotationMatrix(0, 0, twist) * matrix;
        }

        matrix = Matrix4.CreateScalingMatrix(scaleX, scaleY, 1.0) * matrix;
        matrix = Matrix4.CreateTranslation(new XYZ(viewport.Center.X, viewport.Center.Y, 0)) * matrix;
        return new Transform(matrix);
    }

    private IReadOnlyList<IReadOnlyList<Vector2>> BuildViewportClip(Viewport viewport, CadRenderSceneSettings settings)
    {
        if (viewport.Boundary is not null &&
            viewport.Status.HasFlag(ViewportStatusFlags.NonRectangularClipping))
        {
            var boundaryLoops = BuildBoundaryLoops(viewport.Boundary, settings);
            if (boundaryLoops.Count > 0)
            {
                return boundaryLoops;
            }
        }

        return BuildRectangleClip(viewport);
    }

    private IReadOnlyList<IReadOnlyList<Vector2>> BuildBoundaryLoops(Entity boundary, CadRenderSceneSettings settings)
    {
        IReadOnlyList<XYZ>? points = boundary switch
        {
            IPolyline polyline => _geometrySampler.SamplePolyline(polyline, settings.ResolvePolylineArcPrecision()),
            Circle circle => _geometrySampler.SampleCircle(circle, settings.ResolveCirclePrecision()),
            Ellipse ellipse => _geometrySampler.SampleEllipse(ellipse, settings.ResolveCirclePrecision()),
            Spline spline => _geometrySampler.SampleSpline(spline, settings.ResolveSplinePrecision()),
            _ => null
        };

        if (points is null || points.Count < 3)
        {
            return Array.Empty<IReadOnlyList<Vector2>>();
        }

        var loop = new List<Vector2>(points.Count);
        foreach (var point in points)
        {
            loop.Add(RenderTransformUtils.ToVector2(point));
        }

        return new[] { loop };
    }

    private static HashSet<string> ResolveViewportFrozenLayers(Viewport viewport)
    {
        var frozenLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (viewport.FrozenLayers is null || viewport.FrozenLayers.Count == 0)
        {
            return frozenLayers;
        }

        foreach (var layer in viewport.FrozenLayers)
        {
            if (layer is null)
            {
                continue;
            }

            var name = layer.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                frozenLayers.Add(name);
            }
        }

        return frozenLayers;
    }

    private static IEnumerable<Entity> FilterFrozenLayers(IEnumerable<Entity> entities, HashSet<string> frozenLayers)
    {
        foreach (var entity in entities)
        {
            var layerName = entity.Layer?.Name;
            if (layerName is not null && frozenLayers.Contains(layerName))
            {
                continue;
            }

            yield return entity;
        }
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> BuildRectangleClip(Viewport viewport)
    {
        var width = (float)viewport.Width;
        var height = (float)viewport.Height;
        if (width <= 0f || height <= 0f)
        {
            return Array.Empty<IReadOnlyList<Vector2>>();
        }

        var center = viewport.Center;
        var halfW = width * 0.5f;
        var halfH = height * 0.5f;
        var loop = new List<Vector2>
        {
            new Vector2((float)(center.X - halfW), (float)(center.Y - halfH)),
            new Vector2((float)(center.X + halfW), (float)(center.Y - halfH)),
            new Vector2((float)(center.X + halfW), (float)(center.Y + halfH)),
            new Vector2((float)(center.X - halfW), (float)(center.Y + halfH))
        };

        return new[] { loop };
    }

    private static CadRenderSceneSettings WithViewportScale(
        CadRenderSceneSettings settings,
        float viewportScale,
        SpaceLineTypeScaling? paperSpaceScaling,
        float annotationScaleFactor)
    {
        return new CadRenderSceneSettings
        {
            SupportPaths = settings.SupportPaths,
            Quality = settings.Quality,
            VisualStyle = settings.VisualStyle,
            Lighting = settings.Lighting,
            EnableHatchFills = settings.EnableHatchFills,
            EnableHatchPatterns = settings.EnableHatchPatterns,
            EnableHatchGradients = settings.EnableHatchGradients,
            HiddenLineSettings = settings.HiddenLineSettings,
            ShadeEdge = settings.ShadeEdge,
            ShadeDiffuseToAmbientPercentage = settings.ShadeDiffuseToAmbientPercentage,
            Background = settings.Background,
            FallbackColor = settings.FallbackColor,
            MillimetersPerUnit = settings.MillimetersPerUnit,
            DefaultLineWeightMm = settings.DefaultLineWeightMm,
            MinLineWeightMm = settings.MinLineWeightMm,
            DisplayLineWeight = settings.DisplayLineWeight,
            LineTypeDotLengthMm = settings.LineTypeDotLengthMm,
            PolylineArcPrecision = settings.PolylineArcPrecision,
            SplinePrecision = settings.SplinePrecision,
            CirclePrecision = settings.CirclePrecision,
            TextWidthFactor = settings.TextWidthFactor,
            PointDisplayMode = settings.PointDisplayMode,
            PointDisplaySize = settings.PointDisplaySize,
            QuickTextMode = settings.QuickTextMode,
            FillMode = settings.FillMode,
            PolylineLineTypeGeneration = settings.PolylineLineTypeGeneration,
            MirrorText = settings.MirrorText,
            RenderAttributes = settings.RenderAttributes,
            RenderAttributeDefinitions = settings.RenderAttributeDefinitions,
            EntityTypeVisibilityOverrides = settings.EntityTypeVisibilityOverrides,
            XClipFrameVisibility = settings.XClipFrameVisibility,
            WipeoutFrameVisibility = settings.WipeoutFrameVisibility,
            UnderlayFrameVisibility = settings.UnderlayFrameVisibility,
            IsPaperSpace = settings.IsPaperSpace,
            LayoutName = settings.LayoutName,
            PaperSpaceLineTypeScalingOverride = paperSpaceScaling ?? settings.PaperSpaceLineTypeScalingOverride,
            ViewportScale = viewportScale,
            ModelSpaceLineTypeScaling = settings.ModelSpaceLineTypeScaling,
            AnnotationScaleFactor = annotationScaleFactor,
            StackedTextAlignment = settings.StackedTextAlignment,
            StackedTextSizePercentage = settings.StackedTextSizePercentage,
            IncludeInvisible = settings.IncludeInvisible,
            IncludeOffLayers = settings.IncludeOffLayers,
            IncludeUnsupportedAsPoints = settings.IncludeUnsupportedAsPoints,
            PerformanceBudget = settings.PerformanceBudget
        };
    }

    private static float NormalizeScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            return 1f;
        }

        return (float)scale;
    }
}
