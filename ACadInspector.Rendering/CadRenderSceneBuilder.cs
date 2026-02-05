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
            var paperTransform = ResolvePaperSpaceTransform(layout, settings);
            var paperBounds = ResolvePaperBounds(document, layout, paperTransform, settings);
            var layoutScaling = ResolvePaperSpaceLineTypeScaling(layout);
            var annotationScaleFactor = settings.AnnotationScaleFactor;
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

            BuildPaperSpace(document, layout, paperSettings, context, diagnostics, annotationScaleFactor, paperTransform);
            return BuildScene(
                context,
                stats,
                stopwatch,
                isPaperSpace: true,
                paperBounds: paperBounds,
                paperColor: RenderColor.DefaultPaper);
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
        RenderDiagnostics diagnostics,
        float annotationScaleFactor,
        Transform paperTransform)
    {
        var paperSpace = ResolvePaperSpaceBlock(document, layout);
        if (paperSpace is null)
        {
            return;
        }

        var drawViewportsFirst = layout?.Flags.HasFlag(PlotFlags.DrawViewportsFirst) ?? true;
        if (!drawViewportsFirst)
        {
            AppendEntities(paperSpace.Entities, paperSpace, paperTransform, context);
        }

        var viewports = ResolveViewports(layout, paperSpace);
        foreach (var viewport in viewports)
        {
            if (!ShouldRenderViewport(viewport))
            {
                continue;
            }

            var viewportScale = NormalizeScale(viewport.ScaleFactor);
            var viewportAnnotationScale = ResolveViewportAnnotationScale(viewport, annotationScaleFactor);
            var viewportVisualStyle = ResolveViewportVisualStyle(viewport, settings);
            var viewportPlotStyleTable = ResolveViewportPlotStyleTable(viewport, layout, settings);
            var viewportTone = ResolveViewportTone(viewport, settings);
            var viewportLighting = ResolveViewportLighting(viewport, settings.Lighting);
            var viewportSettings = WithViewportScale(
                settings,
                viewportScale,
                settings.PaperSpaceLineTypeScalingOverride,
                viewportAnnotationScale,
                viewportVisualStyle,
                viewportPlotStyleTable,
                viewportLighting,
                viewportTone.Brightness,
                viewportTone.Contrast);
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

            var viewportTransform = BuildViewportTransform(viewport);
            var transform = RenderTransformUtils.Combine(paperTransform, viewportTransform);
            var viewportBlock = ResolveViewportBlock(document, viewport);
            var viewportEntities = ResolveViewportEntities(document, viewport, viewportTransform);
            var frozenLayers = ResolveViewportFrozenLayers(viewport);
            if (frozenLayers.Count > 0)
            {
                viewportEntities = FilterFrozenLayers(viewportEntities, frozenLayers);
            }

            AppendEntities(viewportEntities, viewportBlock, transform, viewportContext);

            var clipLoops = BuildViewportClip(viewport, settings, paperTransform);
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

        if (drawViewportsFirst)
        {
            AppendEntities(paperSpace.Entities, paperSpace, paperTransform, context);
        }
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
        Stopwatch stopwatch,
        bool isPaperSpace = false,
        RenderBounds? paperBounds = null,
        RenderColor? paperColor = null)
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

        if (paperBounds.HasValue)
        {
            sceneBounds = sceneBounds.Expand(paperBounds.Value);
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
            renderStats,
            isPaperSpace,
            paperBounds,
            paperColor);
    }

    private static RenderBounds? ResolvePaperBounds(
        CadDocument document,
        Layout? layout,
        Transform paperTransform,
        CadRenderSceneSettings settings)
    {
        RenderBounds? bounds = null;
        if (layout is not null)
        {
            if (layout.PaperWidth > 0.0 && layout.PaperHeight > 0.0)
            {
                var min = new Vector2(0f, 0f);
                var width = (float)(layout.PaperWidth / settings.MillimetersPerUnit);
                var height = (float)(layout.PaperHeight / settings.MillimetersPerUnit);
                var max = new Vector2(width, height);
                bounds = Normalize(new RenderBounds(min, max));
                if (IsValid(bounds.Value))
                {
                    return TransformBounds(bounds.Value, paperTransform);
                }
            }

            bounds = ToBounds(layout.MinExtents, layout.MaxExtents);
            if (IsValid(bounds.Value))
            {
                return TransformBounds(bounds.Value, paperTransform);
            }
        }

        if (document.Header is not null)
        {
            bounds = ToBounds(document.Header.PaperSpaceExtMin, document.Header.PaperSpaceExtMax);
            if (IsValid(bounds.Value))
            {
                return TransformBounds(bounds.Value, paperTransform);
            }
        }

        return null;
    }

    private static RenderBounds ToBounds(XYZ min, XYZ max)
    {
        var minVec = new Vector2((float)min.X, (float)min.Y);
        var maxVec = new Vector2((float)max.X, (float)max.Y);
        return Normalize(new RenderBounds(minVec, maxVec));
    }

    private static RenderBounds Normalize(RenderBounds bounds)
    {
        if (bounds.IsEmpty)
        {
            return bounds;
        }

        var min = bounds.Min;
        var max = bounds.Max;
        if (min.X <= max.X && min.Y <= max.Y)
        {
            return bounds;
        }

        var fixedMin = new Vector2(MathF.Min(min.X, max.X), MathF.Min(min.Y, max.Y));
        var fixedMax = new Vector2(MathF.Max(min.X, max.X), MathF.Max(min.Y, max.Y));
        return new RenderBounds(fixedMin, fixedMax);
    }

    private static bool IsValid(RenderBounds bounds)
    {
        if (bounds.IsEmpty)
        {
            return false;
        }

        var size = bounds.Size;
        if (float.IsNaN(size.X) || float.IsNaN(size.Y) || float.IsInfinity(size.X) || float.IsInfinity(size.Y))
        {
            return false;
        }

        return size.X > 0.0001f && size.Y > 0.0001f;
    }

    private static RenderBounds TransformBounds(RenderBounds bounds, Transform transform)
    {
        if (RenderTransformUtils.IsIdentity(transform))
        {
            return bounds;
        }

        var min = bounds.Min;
        var max = bounds.Max;
        var corners = new[]
        {
            new XYZ(min.X, min.Y, 0),
            new XYZ(max.X, min.Y, 0),
            new XYZ(max.X, max.Y, 0),
            new XYZ(min.X, max.Y, 0)
        };

        var minVec = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var maxVec = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in corners)
        {
            var transformed = RenderTransformUtils.Apply(transform, corner);
            minVec = new Vector2(MathF.Min(minVec.X, transformed.X), MathF.Min(minVec.Y, transformed.Y));
            maxVec = new Vector2(MathF.Max(maxVec.X, transformed.X), MathF.Max(maxVec.Y, transformed.Y));
        }

        return new RenderBounds(minVec, maxVec);
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

    private static Transform ResolvePaperSpaceTransform(Layout? layout, CadRenderSceneSettings settings)
    {
        if (layout is null)
        {
            return IdentityTransform;
        }

        var offsetX = (layout.PlotOriginX + layout.PaperImageOriginX) / settings.MillimetersPerUnit;
        var offsetY = (layout.PlotOriginY + layout.PaperImageOriginY) / settings.MillimetersPerUnit;
        var rotationAngle = ResolvePlotRotationAngle(layout.PaperRotation);
        var rotationOffset = ResolveRotationOffset(layout, settings);

        if (Math.Abs(offsetX) < 1e-6 && Math.Abs(offsetY) < 1e-6 &&
            Math.Abs(rotationAngle) < 1e-6 && rotationOffset == Vector2.Zero)
        {
            return IdentityTransform;
        }

        var matrix = Matrix4.Identity;
        if (Math.Abs(rotationAngle) > 1e-6)
        {
            matrix = Matrix4.CreateRotationMatrix(0, 0, rotationAngle) * matrix;
        }

        if (rotationOffset != Vector2.Zero)
        {
            matrix = Matrix4.CreateTranslation(new XYZ(rotationOffset.X, rotationOffset.Y, 0)) * matrix;
        }

        if (Math.Abs(offsetX) > 1e-6 || Math.Abs(offsetY) > 1e-6)
        {
            matrix = Matrix4.CreateTranslation(new XYZ(offsetX, offsetY, 0)) * matrix;
        }

        return new Transform(matrix);
    }

    private static double ResolvePlotRotationAngle(PlotRotation rotation)
    {
        return rotation switch
        {
            PlotRotation.Degrees90 => Math.PI / 2.0,
            PlotRotation.Degrees180 => Math.PI,
            PlotRotation.Degrees270 => -Math.PI / 2.0,
            _ => 0.0
        };
    }

    private static Vector2 ResolveRotationOffset(Layout layout, CadRenderSceneSettings settings)
    {
        if (!TryResolvePaperSize(layout, settings, out var width, out var height))
        {
            return Vector2.Zero;
        }

        return layout.PaperRotation switch
        {
            PlotRotation.Degrees90 => new Vector2(height, 0f),
            PlotRotation.Degrees180 => new Vector2(width, height),
            PlotRotation.Degrees270 => new Vector2(0f, width),
            _ => Vector2.Zero
        };
    }

    private static bool TryResolvePaperSize(Layout layout, CadRenderSceneSettings settings, out float width, out float height)
    {
        width = 0f;
        height = 0f;

        if (layout.PaperWidth > 0.0 && layout.PaperHeight > 0.0)
        {
            width = (float)(layout.PaperWidth / settings.MillimetersPerUnit);
            height = (float)(layout.PaperHeight / settings.MillimetersPerUnit);
            return width > 0f && height > 0f;
        }

        var bounds = ToBounds(layout.MinExtents, layout.MaxExtents);
        if (!IsValid(bounds))
        {
            return false;
        }

        var size = bounds.Size;
        width = size.X;
        height = size.Y;
        return width > 0f && height > 0f;
    }

    private static (float Brightness, float Contrast) ResolveViewportTone(Viewport viewport, CadRenderSceneSettings settings)
    {
        var brightness = (float)viewport.Brightness;
        var contrast = (float)viewport.Contrast;

        var brightnessUnset = IsViewportToneUnset(brightness);
        var contrastUnset = IsViewportToneUnset(contrast);

        if (brightnessUnset && contrastUnset)
        {
            return (settings.ViewportBrightness, settings.ViewportContrast);
        }

        if (brightnessUnset)
        {
            brightness = settings.ViewportBrightness;
        }

        if (contrastUnset)
        {
            contrast = settings.ViewportContrast;
        }

        return (brightness, contrast);
    }

    private static bool IsViewportToneUnset(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) || value <= 0f;
    }

    private static RenderLightingSettings ResolveViewportLighting(Viewport viewport, RenderLightingSettings fallback)
    {
        var lights = fallback.Lights;
        if (viewport.UseDefaultLighting)
        {
            var defaults = RenderLightingSettings.Default.Lights;
            lights = viewport.DefaultLightingType == LightingType.OneDistantLight
                ? TakeLights(defaults, 1)
                : TakeLights(defaults, 2);
        }

        var ambientColor = fallback.AmbientColor;
        var ambient = viewport.AmbientLightColor;
        if ((ambient.R != 0 || ambient.G != 0 || ambient.B != 0) && !ambient.IsByLayer && !ambient.IsByBlock)
        {
            ambientColor = new RenderColor(ambient.R, ambient.G, ambient.B, 255);
        }

        return new RenderLightingSettings(lights, fallback.AmbientIntensity, ambientColor);
    }

    private static IReadOnlyList<RenderLight> TakeLights(IReadOnlyList<RenderLight> lights, int count)
    {
        if (lights.Count <= count)
        {
            return lights;
        }

        var result = new RenderLight[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = lights[i];
        }

        return result;
    }

    private static bool ShouldRenderViewport(Viewport viewport)
    {
        if (viewport.Status.HasFlag(ViewportStatusFlags.ViewportOff))
        {
            return false;
        }

        if (ViewportRenderUtils.IsPaperViewport(viewport))
        {
            return false;
        }

        return viewport.Width > 0 && viewport.Height > 0 && viewport.ViewHeight > 0;
    }

    private static IEnumerable<Entity> ResolveViewportEntities(
        CadDocument document,
        Viewport viewport,
        Transform viewportTransform)
    {
        var targetDocument = viewport.Document ?? document;
        if (!TryGetViewportModelBounds(viewport, viewportTransform, out var viewBounds))
        {
            return targetDocument.Entities;
        }

        return FilterEntitiesByBounds(targetDocument.Entities, viewBounds);
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

    private static bool TryGetViewportModelBounds(
        Viewport viewport,
        Transform viewportTransform,
        out BoundingBox bounds)
    {
        bounds = BoundingBox.Null;
        if (!Matrix4.Inverse(viewportTransform.Matrix, out var inverse))
        {
            return false;
        }

        var halfW = viewport.Width * 0.5;
        var halfH = viewport.Height * 0.5;
        if (halfW <= 0.0 || halfH <= 0.0)
        {
            return false;
        }

        var center = viewport.Center;
        var corners = new[]
        {
            new XYZ(center.X - halfW, center.Y - halfH, 0),
            new XYZ(center.X + halfW, center.Y - halfH, 0),
            new XYZ(center.X + halfW, center.Y + halfH, 0),
            new XYZ(center.X - halfW, center.Y + halfH, 0)
        };

        var min = new XYZ(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var max = new XYZ(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);

        foreach (var corner in corners)
        {
            var model = inverse * corner;
            if (IsInvalid(model))
            {
                return false;
            }

            min = new XYZ(Math.Min(min.X, model.X), Math.Min(min.Y, model.Y), Math.Min(min.Z, model.Z));
            max = new XYZ(Math.Max(max.X, model.X), Math.Max(max.Y, model.Y), Math.Max(max.Z, model.Z));
        }

        bounds = new BoundingBox(min, max);
        return IsValid(bounds);
    }

    private static IEnumerable<Entity> FilterEntitiesByBounds(IEnumerable<Entity> entities, BoundingBox viewBounds)
    {
        foreach (var entity in entities)
        {
            if (entity is null)
            {
                continue;
            }

            if (!TryGetEntityBounds(entity, out var bounds))
            {
                yield return entity;
                continue;
            }

            if (Intersects(viewBounds, bounds))
            {
                yield return entity;
            }
        }
    }

    private static bool TryGetEntityBounds(Entity entity, out BoundingBox bounds)
    {
        bounds = BoundingBox.Null;
        if (entity is Insert insert)
        {
            return TryGetInsertBounds(insert, out bounds);
        }

        bounds = entity.GetBoundingBox();
        return IsValid(bounds);
    }

    private static bool TryGetInsertBounds(Insert insert, out BoundingBox bounds)
    {
        bounds = BoundingBox.Null;
        if (insert.Block is null)
        {
            return false;
        }

        var blockBounds = insert.Block.GetBoundingBox();
        if (!IsValid(blockBounds))
        {
            return false;
        }

        var insertTransform = BuildInsertTransform(insert);
        insertTransform = ApplyBlockBasePoint(insert, insertTransform);

        if (!TryTransformBounds(blockBounds, insertTransform, out var transformed))
        {
            return false;
        }

        var rowCount = insert.RowCount < 1 ? 1 : insert.RowCount;
        var columnCount = insert.ColumnCount < 1 ? 1 : insert.ColumnCount;

        bounds = transformed;
        if (rowCount == 1 && columnCount == 1)
        {
            return true;
        }

        for (var row = 0; row < rowCount; row++)
        {
            for (var column = 0; column < columnCount; column++)
            {
                if (row == 0 && column == 0)
                {
                    continue;
                }

                var offset = new XYZ(column * insert.ColumnSpacing, row * insert.RowSpacing, 0);
                var offsetTransform = new Transform(Matrix4.CreateTranslation(offset));
                if (!TryTransformBounds(transformed, offsetTransform, out var offsetBounds))
                {
                    return true;
                }

                bounds = UnionBounds(bounds, offsetBounds);
            }
        }

        return true;
    }

    private static bool Intersects(BoundingBox left, BoundingBox right)
    {
        return !(left.Min.X > right.Max.X || left.Max.X < right.Min.X ||
                 left.Min.Y > right.Max.Y || left.Max.Y < right.Min.Y ||
                 left.Min.Z > right.Max.Z || left.Max.Z < right.Min.Z);
    }

    private static bool IsValid(BoundingBox bounds)
    {
        if (bounds.Extent == BoundingBoxExtent.Null)
        {
            return false;
        }

        return !(IsInvalid(bounds.Min) || IsInvalid(bounds.Max));
    }

    private static bool IsInvalid(XYZ point)
    {
        return double.IsNaN(point.X) || double.IsNaN(point.Y) || double.IsNaN(point.Z)
            || double.IsInfinity(point.X) || double.IsInfinity(point.Y) || double.IsInfinity(point.Z);
    }

    private static Transform ApplyBlockBasePoint(Insert insert, Transform transform)
    {
        var basePoint = insert.Block?.BlockEntity?.BasePoint ?? XYZ.Zero;
        if (basePoint.Equals(XYZ.Zero))
        {
            return transform;
        }

        var offset = new XYZ(-basePoint.X, -basePoint.Y, -basePoint.Z);
        var baseTransform = new Transform(Matrix4.CreateTranslation(offset));
        return RenderTransformUtils.Combine(transform, baseTransform);
    }

    private static Transform BuildInsertTransform(Insert insert)
    {
        var normal = RenderTransformUtils.NormalizeNormal(insert.Normal);
        var world = Matrix4.GetArbitraryAxis(normal);
        var translation = Matrix4.CreateTranslation(insert.InsertPoint);
        var rotation = Matrix4.CreateFromAxisAngle(XYZ.AxisZ, insert.Rotation);
        var scale = Matrix4.CreateScale(new XYZ(insert.XScale, insert.YScale, insert.ZScale));

        return new Transform(world * translation * rotation * scale);
    }

    private static bool TryTransformBounds(BoundingBox bounds, Transform transform, out BoundingBox transformed)
    {
        transformed = BoundingBox.Null;
        var min = bounds.Min;
        var max = bounds.Max;
        var corners = new[]
        {
            new XYZ(min.X, min.Y, min.Z),
            new XYZ(max.X, min.Y, min.Z),
            new XYZ(max.X, max.Y, min.Z),
            new XYZ(min.X, max.Y, min.Z),
            new XYZ(min.X, min.Y, max.Z),
            new XYZ(max.X, min.Y, max.Z),
            new XYZ(max.X, max.Y, max.Z),
            new XYZ(min.X, max.Y, max.Z)
        };

        var minVec = new XYZ(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var maxVec = new XYZ(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        foreach (var corner in corners)
        {
            var transformedCorner = transform.ApplyTransform(corner);
            if (IsInvalid(transformedCorner))
            {
                return false;
            }

            minVec = new XYZ(
                Math.Min(minVec.X, transformedCorner.X),
                Math.Min(minVec.Y, transformedCorner.Y),
                Math.Min(minVec.Z, transformedCorner.Z));
            maxVec = new XYZ(
                Math.Max(maxVec.X, transformedCorner.X),
                Math.Max(maxVec.Y, transformedCorner.Y),
                Math.Max(maxVec.Z, transformedCorner.Z));
        }

        transformed = new BoundingBox(minVec, maxVec);
        return true;
    }

    private static BoundingBox UnionBounds(BoundingBox left, BoundingBox right)
    {
        var min = new XYZ(
            Math.Min(left.Min.X, right.Min.X),
            Math.Min(left.Min.Y, right.Min.Y),
            Math.Min(left.Min.Z, right.Min.Z));
        var max = new XYZ(
            Math.Max(left.Max.X, right.Max.X),
            Math.Max(left.Max.Y, right.Max.Y),
            Math.Max(left.Max.Z, right.Max.Z));
        return new BoundingBox(min, max);
    }

    private IReadOnlyList<IReadOnlyList<Vector2>> BuildViewportClip(
        Viewport viewport,
        CadRenderSceneSettings settings,
        Transform paperTransform)
    {
        if (viewport.Boundary is not null &&
            viewport.Status.HasFlag(ViewportStatusFlags.NonRectangularClipping))
        {
            var boundaryLoops = BuildBoundaryLoops(viewport.Boundary, settings);
            if (boundaryLoops.Count > 0)
            {
                return TransformLoops(boundaryLoops, paperTransform);
            }
        }

        return TransformLoops(BuildRectangleClip(viewport), paperTransform);
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

    private static IReadOnlyList<IReadOnlyList<Vector2>> TransformLoops(
        IReadOnlyList<IReadOnlyList<Vector2>> loops,
        Transform transform)
    {
        if (loops.Count == 0 || RenderTransformUtils.IsIdentity(transform))
        {
            return loops;
        }

        var transformed = new List<IReadOnlyList<Vector2>>(loops.Count);
        foreach (var loop in loops)
        {
            if (loop.Count == 0)
            {
                transformed.Add(loop);
                continue;
            }

            var result = new List<Vector2>(loop.Count);
            foreach (var point in loop)
            {
                var world = RenderTransformUtils.Apply(transform, new XYZ(point.X, point.Y, 0));
                result.Add(world);
            }

            transformed.Add(result);
        }

        return transformed;
    }

    private static CadRenderSceneSettings WithViewportScale(
        CadRenderSceneSettings settings,
        float viewportScale,
        SpaceLineTypeScaling? paperSpaceScaling,
        float annotationScaleFactor,
        RenderVisualStyle? visualStyle = null,
        RenderPlotStyleTable? plotStyleTable = null,
        RenderLightingSettings? lighting = null,
        float? viewportBrightness = null,
        float? viewportContrast = null)
    {
        return new CadRenderSceneSettings
        {
            SupportPaths = settings.SupportPaths,
            Quality = settings.Quality,
            VisualStyle = visualStyle ?? settings.VisualStyle,
            Lighting = lighting ?? settings.Lighting,
            ViewportBrightness = viewportBrightness ?? settings.ViewportBrightness,
            ViewportContrast = viewportContrast ?? settings.ViewportContrast,
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
            PerformanceBudget = settings.PerformanceBudget,
            PlotStyleTable = plotStyleTable ?? settings.PlotStyleTable
        };
    }

    private static RenderPlotStyleTable? ResolveViewportPlotStyleTable(
        Viewport viewport,
        Layout? layout,
        CadRenderSceneSettings settings)
    {
        if (layout is null || !layout.Flags.HasFlag(PlotFlags.ShowPlotStyles))
        {
            return null;
        }

        var styleSheet = viewport.StyleSheetName;
        if (string.IsNullOrWhiteSpace(styleSheet))
        {
            return null;
        }

        foreach (var path in settings.SupportPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var candidate = System.IO.Path.Combine(path, styleSheet);
            var table = RenderPlotStyleTable.TryLoad(candidate);
            if (table is not null)
            {
                return table;
            }
        }

        return RenderPlotStyleTable.TryLoad(styleSheet);
    }

    private static float NormalizeScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            return 1f;
        }

        return (float)scale;
    }

    private static float ResolveViewportAnnotationScale(Viewport viewport, float fallback)
    {
        var scale = viewport.Scale;
        if (scale is null)
        {
            return fallback;
        }

        var factor = scale.ScaleFactor;
        if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0.0)
        {
            return fallback;
        }

        var annotationScale = 1.0 / factor;
        if (double.IsNaN(annotationScale) || double.IsInfinity(annotationScale) || annotationScale <= 0.0)
        {
            return fallback;
        }

        return (float)annotationScale;
    }

    private static RenderVisualStyle ResolveViewportVisualStyle(Viewport viewport, CadRenderSceneSettings settings)
    {
        var style = MapVisualStyle(viewport.RenderMode);
        return style != RenderVisualStyle.Wireframe ? style : settings.VisualStyle;
    }

    private static RenderVisualStyle MapVisualStyle(RenderMode? mode)
    {
        return mode switch
        {
            RenderMode.HiddenLine => RenderVisualStyle.HiddenLine,
            RenderMode.FlatShaded => RenderVisualStyle.Shaded,
            RenderMode.GouraudShaded => RenderVisualStyle.Shaded,
            RenderMode.FlatShadedWithWireframe => RenderVisualStyle.Shaded,
            RenderMode.GouraudShadedWithWireframe => RenderVisualStyle.Shaded,
            _ => RenderVisualStyle.Wireframe
        };
    }
}
