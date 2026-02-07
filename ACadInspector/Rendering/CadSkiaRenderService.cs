using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Avalonia;
using SkiaSharp;

namespace ACadInspector.Rendering;

public sealed class CadSkiaRenderService
{
    private readonly object _cacheLock = new();
    private readonly Dictionary<PenKey, SKPaint> _strokePaintCache = new();
    private readonly Dictionary<DashPenKey, SKPaint> _dashStrokePaintCache = new();
    private readonly Dictionary<ObscuredPenKey, SKPaint> _obscuredPaintCache = new();
    private readonly Dictionary<RenderObscuredLineType, SKPathEffect> _obscuredEffectCache = new();
    private readonly Dictionary<DashPatternKey, SKPathEffect> _dashEffectCache = new();
    private readonly Dictionary<RenderColor, SKPaint> _fillPaintCache = new();
    private readonly Dictionary<TypefaceKey, SKTypeface> _typefaceCache = new();
    private readonly ConditionalWeakTable<RenderPolyline, SKPath> _polylineGeometryCache = new();
    private readonly ConditionalWeakTable<RenderFill, SKPath> _fillGeometryCache = new();
    private readonly ConditionalWeakTable<RenderTriangle, SKPath> _triangleGeometryCache = new();
    private readonly ConditionalWeakTable<RenderArc, SKPath> _arcGeometryCache = new();
    private readonly ConditionalWeakTable<RenderClipGroup, SKPath> _clipGeometryCache = new();
    private readonly ConditionalWeakTable<RenderHatchFill, SKPath> _hatchFillGeometryCache = new();
    private readonly ConditionalWeakTable<RenderHatchPattern, SKPath> _hatchPatternGeometryCache = new();
    private readonly ConditionalWeakTable<RenderHatchPattern, SKPath> _hatchPatternStrokeCache = new();
    private readonly ConditionalWeakTable<RenderHatchFill, SKPaint> _hatchPaintCache = new();
    private readonly ConditionalWeakTable<RenderText, SKTextBlob> _textCache = new();
    private readonly ConditionalWeakTable<RenderLayer, LayerDrawCache> _layerDrawCache = new();
    private readonly Dictionary<string, SKBitmap?> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly RenderDepthBuffer _hiddenLineDepth = new();
    private readonly List<RenderDepthBuffer> _hiddenLineClipPool = new();
    private readonly Dictionary<RenderClipGroup, RenderDepthBuffer> _hiddenLineClipDepths = new();
    private readonly List<RenderLineSegment> _hiddenLineSegments = new();
    private HiddenLineContext? _hiddenLineCache;
    private RenderScene? _hiddenLineCacheScene;
    private Size _hiddenLineCacheSize;
    private Matrix3x2 _hiddenLineCacheTransform = Matrix3x2.Identity;
    private bool _hiddenLineCacheValid;
    private SKImage? _interactionSnapshot;
    private CadRenderStateSnapshot? _interactionSnapshotState;
    private Size _interactionSnapshotSize;
    private Size _renderSize;

    public void ClearStrokePaintCache()
    {
        lock (_cacheLock)
        {
            _strokePaintCache.Clear();
            _dashStrokePaintCache.Clear();
            _obscuredPaintCache.Clear();
            foreach (var effect in _obscuredEffectCache.Values)
            {
                effect.Dispose();
            }
            _obscuredEffectCache.Clear();
            foreach (var effect in _dashEffectCache.Values)
            {
                effect.Dispose();
            }
            _dashEffectCache.Clear();
        }
    }

    public void InvalidateHiddenLineCache()
    {
        _hiddenLineCacheValid = false;
        _hiddenLineCacheScene = null;
    }

    public void InvalidateInteractionCache()
    {
        _interactionSnapshot?.Dispose();
        _interactionSnapshot = null;
        _interactionSnapshotState = null;
        _interactionSnapshotSize = default;
    }

    public void Render(SKCanvas canvas, Size size, CadRenderStateSnapshot state, bool isInteractive)
    {
        _renderSize = size;
        var scene = state.Scene;
        var background = scene?.Background ?? RenderColor.DefaultBackground;
        // Interaction snapshot caching disabled due to precision issues during pan/zoom.
        const bool allowInteractionCache = false;

        if (isInteractive && allowInteractionCache && TryDrawInteractionSnapshot(canvas, size, state, background))
        {
            DrawOverlays(canvas, state);
            return;
        }

        if (!isInteractive && allowInteractionCache && TryDrawExactSnapshot(canvas, size, state, background))
        {
            DrawOverlays(canvas, state);
            return;
        }

        if (!isInteractive && allowInteractionCache && scene is not null && !scene.Bounds.IsEmpty)
        {
            using var surface = SKSurface.Create(new SKImageInfo(
                (int)Math.Max(1, size.Width),
                (int)Math.Max(1, size.Height),
                SKColorType.Bgra8888,
                SKAlphaType.Premul));

            if (surface is not null)
            {
                RenderScene(surface.Canvas, size, state, isInteractive: false, includeAnnotations: false, includeDebugOverlay: false);
                var snapshot = surface.Snapshot();
                UpdateInteractionSnapshot(snapshot, size, state);
                canvas.DrawImage(snapshot, 0, 0);
                DrawOverlays(canvas, state);
                return;
            }
        }

        RenderScene(canvas, size, state, isInteractive, includeAnnotations: true, includeDebugOverlay: true);
    }

    private void RenderScene(
        SKCanvas canvas,
        Size size,
        CadRenderStateSnapshot state,
        bool isInteractive,
        bool includeAnnotations,
        bool includeDebugOverlay)
    {
        var scene = state.Scene;
        var background = scene?.Background ?? RenderColor.DefaultBackground;
        canvas.Clear(ToSkiaColor(background));

        if (scene is null || scene.Bounds.IsEmpty)
        {
            return;
        }

        var activeScene = scene;
        var renderStyle = isInteractive ? RenderVisualStyle.Wireframe : activeScene.VisualStyle;
        var hiddenLineSettings = activeScene.HiddenLineSettings;
        var hasViewport = TryGetWorldViewport(size, state.ViewTransform, out var viewport);
        if (hasViewport)
        {
            var padding = GetViewportPadding(state);
            viewport = ExpandBounds(viewport, padding);
        }

        var matrix = ToSkiaMatrix(state.ViewTransform);
        canvas.Save();
        canvas.Concat(ref matrix);

        if (activeScene.IsPaperSpace && activeScene.PaperBounds.HasValue)
        {
            DrawPaper(canvas, activeScene.PaperBounds.Value, activeScene.PaperColor ?? RenderColor.DefaultPaper, state);
        }

        if (state.ShowGrid && !isInteractive)
        {
            DrawGrid(canvas, size, state);
        }

        if (state.ShowAxes && !isInteractive)
        {
            DrawAxes(canvas, size, state);
        }

        HiddenLineContext? hiddenLine = null;
        if (!isInteractive && renderStyle == RenderVisualStyle.HiddenLine)
        {
            hiddenLine = ResolveHiddenLineContext(activeScene, size, state.ViewTransform);
        }

        foreach (var layer in activeScene.Layers)
        {
            if (!IsLayerVisible(layer, state.LayerVisibilityOverrides))
            {
                continue;
            }

            DrawLayer(canvas, layer, renderStyle, hiddenLine, hiddenLineSettings, hasViewport, viewport, isInteractive, state);
        }

        if (includeAnnotations)
        {
            DrawAnnotations(canvas, state);
        }

        if (includeDebugOverlay && state.ShowDebugOverlay)
        {
            DrawDebugOverlay(canvas, state);
        }

        canvas.Restore();
    }

    private void DrawOverlays(SKCanvas canvas, CadRenderStateSnapshot state)
    {
        var hasOverlayPrimitives = state.OverlayScene.Primitives.Count > 0;
        var hasDynamicInput = state.DynamicInput is not null;
        if (!state.SelectionAnnotation.HasValue &&
            !state.HoverAnnotation.HasValue &&
            !state.ShowDebugOverlay &&
            !hasOverlayPrimitives &&
            !hasDynamicInput)
        {
            return;
        }

        var matrix = ToSkiaMatrix(state.ViewTransform);
        canvas.Save();
        canvas.Concat(ref matrix);

        DrawAnnotations(canvas, state);
        DrawOverlayScene(canvas, state.OverlayScene);
        DrawDynamicInputWorld(canvas, state.DynamicInput);

        if (state.ShowDebugOverlay)
        {
            DrawDebugOverlay(canvas, state);
        }

        canvas.Restore();

        if (state.DynamicInput is { Anchor: null } dynamicInput)
        {
            DrawDynamicInputScreen(canvas, dynamicInput);
        }
    }

    private static void DrawOverlayScene(SKCanvas canvas, RenderOverlayScene overlayScene)
    {
        if (overlayScene.Primitives.Count == 0)
        {
            return;
        }

        foreach (var primitive in overlayScene.Primitives.OrderBy(static primitive => primitive.Priority))
        {
            switch (primitive.Kind)
            {
                case RenderOverlayPrimitiveKind.PointMarker:
                    using (var paint = new SKPaint
                           {
                               IsAntialias = true,
                               Color = ToSkiaColor(primitive.Color),
                               Style = SKPaintStyle.Stroke,
                               StrokeWidth = Math.Max(1f, primitive.StrokeWidth),
                               PathEffect = CreateOverlayPathEffect(primitive.StrokeStyle)
                           })
                    {
                        canvas.DrawCircle(primitive.Start.X, primitive.Start.Y, Math.Max(2f, primitive.MarkerRadius), paint);
                        canvas.DrawLine(
                            primitive.Start.X - primitive.MarkerRadius,
                            primitive.Start.Y,
                            primitive.Start.X + primitive.MarkerRadius,
                            primitive.Start.Y,
                            paint);
                        canvas.DrawLine(
                            primitive.Start.X,
                            primitive.Start.Y - primitive.MarkerRadius,
                            primitive.Start.X,
                            primitive.Start.Y + primitive.MarkerRadius,
                            paint);
                    }
                    break;
                case RenderOverlayPrimitiveKind.Line:
                    using (var paint = new SKPaint
                           {
                               IsAntialias = true,
                               Color = ToSkiaColor(primitive.Color),
                               Style = SKPaintStyle.Stroke,
                               StrokeWidth = Math.Max(1f, primitive.StrokeWidth),
                               PathEffect = CreateOverlayPathEffect(primitive.StrokeStyle)
                           })
                    {
                        canvas.DrawLine(primitive.Start.X, primitive.Start.Y, primitive.End.X, primitive.End.Y, paint);
                    }
                    break;
                case RenderOverlayPrimitiveKind.Rectangle:
                case RenderOverlayPrimitiveKind.FilledRectangle:
                    using (var paint = new SKPaint
                           {
                               IsAntialias = true,
                               Color = ToSkiaColor(primitive.Color),
                               Style = SKPaintStyle.Stroke,
                               StrokeWidth = Math.Max(1f, primitive.StrokeWidth),
                               PathEffect = CreateOverlayPathEffect(primitive.StrokeStyle)
                           })
                    {
                        var rect = SKRect.Create(
                            Math.Min(primitive.Start.X, primitive.End.X),
                            Math.Min(primitive.Start.Y, primitive.End.Y),
                            Math.Abs(primitive.End.X - primitive.Start.X),
                            Math.Abs(primitive.End.Y - primitive.Start.Y));
                        if (primitive.Kind == RenderOverlayPrimitiveKind.FilledRectangle ||
                            primitive.FillColor is not null)
                        {
                            using var fillPaint = new SKPaint
                            {
                                IsAntialias = true,
                                Color = ToSkiaColor(primitive.FillColor ?? primitive.Color),
                                Style = SKPaintStyle.Fill
                            };
                            canvas.DrawRect(rect, fillPaint);
                        }

                        canvas.DrawRect(rect, paint);
                    }
                    break;
                case RenderOverlayPrimitiveKind.SquareMarker:
                    DrawSquareMarker(canvas, primitive);
                    break;
                case RenderOverlayPrimitiveKind.DiamondMarker:
                    DrawDiamondMarker(canvas, primitive);
                    break;
                case RenderOverlayPrimitiveKind.Text:
                    if (string.IsNullOrWhiteSpace(primitive.Text))
                    {
                        break;
                    }

                    using (var paint = new SKPaint
                           {
                               IsAntialias = true,
                               Color = ToSkiaColor(primitive.Color),
                               Style = SKPaintStyle.Fill,
                               TextSize = 12f
                           })
                    {
                        if (primitive.FillColor is { } backgroundColor)
                        {
                            var bounds = new SKRect();
                            paint.MeasureText(primitive.Text, ref bounds);
                            var rect = new SKRect(
                                primitive.Start.X - 3f,
                                primitive.Start.Y - bounds.Height - 3f,
                                primitive.Start.X + bounds.Width + 4f,
                                primitive.Start.Y + 2f);
                            using var background = new SKPaint
                            {
                                IsAntialias = true,
                                Color = ToSkiaColor(backgroundColor),
                                Style = SKPaintStyle.Fill
                            };
                            canvas.DrawRoundRect(rect, 3f, 3f, background);
                        }

                        canvas.DrawText(primitive.Text, primitive.Start.X, primitive.Start.Y, paint);
                    }
                    break;
            }
        }
    }

    private static SKPathEffect? CreateOverlayPathEffect(RenderOverlayStrokeStyle strokeStyle)
    {
        return strokeStyle switch
        {
            RenderOverlayStrokeStyle.Dashed => SKPathEffect.CreateDash([8f, 6f], 0f),
            RenderOverlayStrokeStyle.Dotted => SKPathEffect.CreateDash([2f, 5f], 0f),
            _ => null
        };
    }

    private static void DrawSquareMarker(SKCanvas canvas, RenderOverlayPrimitive primitive)
    {
        var radius = Math.Max(2f, primitive.MarkerRadius);
        var rect = SKRect.Create(
            primitive.Start.X - radius,
            primitive.Start.Y - radius,
            radius * 2f,
            radius * 2f);

        if (primitive.FillColor is { } fillColor)
        {
            using var fillPaint = new SKPaint
            {
                IsAntialias = true,
                Color = ToSkiaColor(fillColor),
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRect(rect, fillPaint);
        }

        using var strokePaint = new SKPaint
        {
            IsAntialias = true,
            Color = ToSkiaColor(primitive.Color),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1f, primitive.StrokeWidth),
            PathEffect = CreateOverlayPathEffect(primitive.StrokeStyle)
        };
        canvas.DrawRect(rect, strokePaint);
    }

    private static void DrawDiamondMarker(SKCanvas canvas, RenderOverlayPrimitive primitive)
    {
        var radius = Math.Max(2f, primitive.MarkerRadius);
        using var path = new SKPath();
        path.MoveTo(primitive.Start.X, primitive.Start.Y - radius);
        path.LineTo(primitive.Start.X + radius, primitive.Start.Y);
        path.LineTo(primitive.Start.X, primitive.Start.Y + radius);
        path.LineTo(primitive.Start.X - radius, primitive.Start.Y);
        path.Close();

        if (primitive.FillColor is { } fillColor)
        {
            using var fillPaint = new SKPaint
            {
                IsAntialias = true,
                Color = ToSkiaColor(fillColor),
                Style = SKPaintStyle.Fill
            };
            canvas.DrawPath(path, fillPaint);
        }

        using var strokePaint = new SKPaint
        {
            IsAntialias = true,
            Color = ToSkiaColor(primitive.Color),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1f, primitive.StrokeWidth),
            PathEffect = CreateOverlayPathEffect(primitive.StrokeStyle)
        };
        canvas.DrawPath(path, strokePaint);
    }

    private static void DrawDynamicInputWorld(SKCanvas canvas, CadDynamicInputPayload? dynamicInput)
    {
        if (dynamicInput?.Anchor is not { } anchor)
        {
            return;
        }

        var content = dynamicInput.Value;
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        using var background = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(16, 18, 24, 230),
            Style = SKPaintStyle.Fill
        };
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            Style = SKPaintStyle.Fill,
            TextSize = 11f
        };

        var bounds = new SKRect();
        textPaint.MeasureText(content, ref bounds);
        var rect = new SKRect(
            anchor.X + 4f,
            anchor.Y - bounds.Height - 8f,
            anchor.X + 4f + bounds.Width + 10f,
            anchor.Y + 2f);
        canvas.DrawRoundRect(rect, 3f, 3f, background);
        canvas.DrawText(content, rect.Left + 5f, rect.Bottom - 4f, textPaint);
    }

    private static void DrawDynamicInputScreen(SKCanvas canvas, CadDynamicInputPayload dynamicInput)
    {
        var content = dynamicInput.Value;
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        using var background = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(16, 18, 24, 220),
            Style = SKPaintStyle.Fill
        };
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            Style = SKPaintStyle.Fill,
            TextSize = 12f
        };

        var bounds = new SKRect();
        textPaint.MeasureText(content, ref bounds);
        var rect = new SKRect(12f, 12f, 12f + bounds.Width + 12f, 30f);
        canvas.DrawRoundRect(rect, 4f, 4f, background);
        canvas.DrawText(content, rect.Left + 6f, rect.Bottom - 8f, textPaint);
    }

    private bool TryDrawInteractionSnapshot(
        SKCanvas canvas,
        Size size,
        CadRenderStateSnapshot state,
        RenderColor background)
    {
        if (_interactionSnapshot is null || _interactionSnapshotState is null)
        {
            return false;
        }

        if (!IsSnapshotCompatible(state, size, requireViewTransformMatch: false))
        {
            return false;
        }

        if (!Matrix3x2.Invert(_interactionSnapshotState.ViewTransform, out var inverse))
        {
            return false;
        }

        // System.Numerics uses row-vector multiplication, so compose as old^-1 * new.
        var delta = inverse * state.ViewTransform;
        var deltaMatrix = ToSkiaMatrix(delta);
        canvas.Clear(ToSkiaColor(background));
        canvas.Save();
        canvas.Concat(ref deltaMatrix);
        canvas.DrawImage(_interactionSnapshot, 0, 0);
        canvas.Restore();
        return true;
    }

    private bool TryDrawExactSnapshot(
        SKCanvas canvas,
        Size size,
        CadRenderStateSnapshot state,
        RenderColor background)
    {
        if (_interactionSnapshot is null || _interactionSnapshotState is null)
        {
            return false;
        }

        if (!IsSnapshotCompatible(state, size, requireViewTransformMatch: true))
        {
            return false;
        }

        canvas.Clear(ToSkiaColor(background));
        canvas.DrawImage(_interactionSnapshot, 0, 0);
        return true;
    }

    private bool IsSnapshotCompatible(
        CadRenderStateSnapshot state,
        Size size,
        bool requireViewTransformMatch)
    {
        if (_interactionSnapshotState is null || _interactionSnapshot is null)
        {
            return false;
        }

        if (!ReferenceEquals(_interactionSnapshotState.Scene, state.Scene))
        {
            return false;
        }

        if (Math.Abs(_interactionSnapshotSize.Width - size.Width) > 0.1 ||
            Math.Abs(_interactionSnapshotSize.Height - size.Height) > 0.1)
        {
            return false;
        }

        if (_interactionSnapshotState.ShowGrid != state.ShowGrid ||
            _interactionSnapshotState.ShowAxes != state.ShowAxes ||
            !ReferenceEquals(_interactionSnapshotState.LayerVisibilityOverrides, state.LayerVisibilityOverrides) ||
            !ReferenceEquals(_interactionSnapshotState.EntityTypeVisibilityOverrides, state.EntityTypeVisibilityOverrides))
        {
            return false;
        }

        if (requireViewTransformMatch && !MatrixEquals(_interactionSnapshotState.ViewTransform, state.ViewTransform))
        {
            return false;
        }

        return true;
    }

    private void UpdateInteractionSnapshot(SKImage snapshot, Size size, CadRenderStateSnapshot state)
    {
        _interactionSnapshot?.Dispose();
        _interactionSnapshot = snapshot;
        _interactionSnapshotState = state;
        _interactionSnapshotSize = size;
    }

    private void DrawLayer(
        SKCanvas canvas,
        RenderLayer layer,
        RenderVisualStyle style,
        HiddenLineContext? hiddenLine,
        RenderHiddenLineSettings hiddenLineSettings,
        bool hasViewport,
        RenderBounds viewport,
        bool isInteractive,
        CadRenderStateSnapshot state)
    {
        if (hasViewport && !BoundsIntersects(layer.Bounds, viewport))
        {
            return;
        }

        if (style == RenderVisualStyle.HiddenLine || state.EntityTypeVisibilityOverrides is not null)
        {
            foreach (var primitive in layer.Primitives)
            {
                DrawPrimitive(canvas, primitive, style, hiddenLine, hiddenLineSettings, layer.Color, hasViewport, viewport, isInteractive, state);
            }

            return;
        }

        var drawCache = GetLayerDrawCache(layer);
        foreach (var op in drawCache.Ops)
        {
            if (op.LineBatch is not null)
            {
                DrawLineBatch(canvas, op.LineBatch, hasViewport, viewport, state);
                continue;
            }

            if (op.PolylineBatch is not null)
            {
                DrawPolylineBatch(canvas, op.PolylineBatch, hasViewport, viewport, state);
                continue;
            }

            if (op.ArcBatch is not null)
            {
                DrawArcBatch(canvas, op.ArcBatch, hasViewport, viewport, state);
                continue;
            }

            if (op.CircleBatch is not null)
            {
                DrawCircleBatch(canvas, op.CircleBatch, hasViewport, viewport, state);
                continue;
            }

            if (op.Primitive is not null)
            {
                DrawPrimitive(canvas, op.Primitive, style, hiddenLine, hiddenLineSettings, layer.Color, hasViewport, viewport, isInteractive, state);
            }
        }
    }

    private void DrawObscuredSegments(
        SKCanvas canvas,
        Vector2 start,
        Vector2 end,
        float depthStart,
        float depthEnd,
        RenderColor primitiveColor,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        HiddenLineContext hidden,
        RenderHiddenLineSettings hiddenLineSettings,
        RenderColor layerColor,
        CadRenderStateSnapshot state)
    {
        if (!hiddenLineSettings.IsEnabled)
        {
            return;
        }

        _hiddenLineSegments.Clear();
        RenderHiddenLineUtils.AppendHiddenSegments(
            hidden.DepthBuffer,
            hidden.WorldToScreen,
            start,
            end,
            depthStart,
            depthEnd,
            _hiddenLineSegments,
            hidden.DepthEpsilon);

        if (_hiddenLineSegments.Count == 0)
        {
            return;
        }

        var obscuredColor = ResolveObscuredColor(hiddenLineSettings, primitiveColor, layerColor);
        var paint = GetObscuredPaint(obscuredColor, thickness, lineCap, lineJoin, hiddenLineSettings.LineType, state);
        foreach (var segment in _hiddenLineSegments)
        {
            canvas.DrawLine(segment.Start.X, segment.Start.Y, segment.End.X, segment.End.Y, paint);
        }
    }

    private static RenderColor ResolveObscuredColor(
        RenderHiddenLineSettings settings,
        RenderColor primitiveColor,
        RenderColor layerColor)
    {
        var alpha = primitiveColor.A;
        return settings.ColorMode switch
        {
            RenderHiddenLineColorMode.Layer => new RenderColor(layerColor.R, layerColor.G, layerColor.B, alpha),
            RenderHiddenLineColorMode.Fixed => new RenderColor(settings.Color.R, settings.Color.G, settings.Color.B, alpha),
            _ => primitiveColor
        };
    }

    private SKPaint GetObscuredPaint(
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        RenderObscuredLineType lineType,
        CadRenderStateSnapshot state)
    {
        if (lineType == RenderObscuredLineType.Solid)
        {
            return GetStrokePaint(color, thickness, lineCap, lineJoin, state);
        }

        var worldThickness = ResolveWorldThickness(thickness, state);
        var key = new ObscuredPenKey(color, worldThickness, lineCap, lineJoin, lineType);
        lock (_cacheLock)
        {
            if (_obscuredPaintCache.TryGetValue(key, out var cached) && cached is not null)
            {
                return cached;
            }

            var effect = ResolveObscuredPathEffect(lineType);
            var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = worldThickness,
                StrokeCap = ToSkiaLineCap(lineCap),
                StrokeJoin = ToSkiaLineJoin(lineJoin),
                Color = ToSkiaColor(color),
                PathEffect = effect
            };

            _obscuredPaintCache[key] = paint;
            return paint;
        }
    }

    private SKPathEffect? ResolveObscuredPathEffect(RenderObscuredLineType lineType)
    {
        lock (_cacheLock)
        {
            if (_obscuredEffectCache.TryGetValue(lineType, out var cached))
            {
                return cached;
            }

            var intervals = ResolveObscuredIntervals(lineType);
            if (intervals is null)
            {
                return null;
            }

            var effect = SKPathEffect.CreateDash(intervals, 0);
            _obscuredEffectCache[lineType] = effect;
            return effect;
        }
    }

    private static float[]? ResolveObscuredIntervals(RenderObscuredLineType lineType)
    {
        const float unit = 4f;
        return lineType switch
        {
            RenderObscuredLineType.Dotted => new[] { unit * 0.5f, unit },
            RenderObscuredLineType.Dashed => new[] { unit * 2f, unit },
            RenderObscuredLineType.ShortDash => new[] { unit * 1.5f, unit },
            RenderObscuredLineType.MediumDash => new[] { unit * 3f, unit },
            RenderObscuredLineType.LongDash => new[] { unit * 4.5f, unit },
            RenderObscuredLineType.DoubleShortDash => new[] { unit * 1.5f, unit * 0.5f, unit * 1.5f, unit },
            RenderObscuredLineType.DoubleMediumDash => new[] { unit * 3f, unit * 0.5f, unit * 3f, unit },
            RenderObscuredLineType.DoubleLongDash => new[] { unit * 4.5f, unit * 0.5f, unit * 4.5f, unit },
            RenderObscuredLineType.MediumDashShortDashShortDash => new[] { unit * 3f, unit, unit * 1.5f, unit, unit * 1.5f, unit },
            RenderObscuredLineType.LongDashShortDashShortDash => new[] { unit * 4.5f, unit, unit * 1.5f, unit, unit * 1.5f, unit },
            _ => null
        };
    }

    private static bool IsLayerVisible(RenderLayer layer, IReadOnlyDictionary<string, bool>? overrides)
    {
        if (overrides is not null && overrides.TryGetValue(layer.Name, out var isVisible))
        {
            return isVisible;
        }

        return layer.IsVisible;
    }

    private static bool IsPrimitiveVisible(
        RenderScene scene,
        IRenderPrimitive primitive,
        IReadOnlyDictionary<string, bool> overrides)
    {
        if (!scene.PrimitiveMetadata.TryGetValue(primitive, out var metadata))
        {
            return true;
        }

        var entity = metadata.OwnerEntity ?? metadata.SourceEntity;
        if (entity is null)
        {
            return true;
        }

        return !(overrides.TryGetValue(entity.GetType().Name, out var isVisible) && !isVisible);
    }

    private void DrawPrimitive(
        SKCanvas canvas,
        IRenderPrimitive primitive,
        RenderVisualStyle style,
        HiddenLineContext? hiddenLine,
        RenderHiddenLineSettings hiddenLineSettings,
        RenderColor layerColor,
        bool hasViewport,
        RenderBounds viewport,
        bool isInteractive,
        CadRenderStateSnapshot state)
    {
        if (hasViewport && !BoundsIntersects(primitive.Bounds, viewport))
        {
            return;
        }

        var entityOverrides = state.EntityTypeVisibilityOverrides;
        if (entityOverrides is not null && state.Scene is not null)
        {
            if (!IsPrimitiveVisible(state.Scene, primitive, entityOverrides))
            {
                return;
            }
        }

        if (primitive is RenderClipGroup clipGroup)
        {
            DrawClipGroup(canvas, clipGroup, style, hiddenLine, hiddenLineSettings, layerColor, hasViewport, viewport, isInteractive, state);
            return;
        }

        if (!ShouldRenderPrimitive(style, primitive, isInteractive))
        {
            return;
        }

        switch (primitive)
        {
            case RenderLine line:
                {
                    var paint = GetStrokePaint(line.Color, line.Thickness, line.LineCap, line.LineJoin, line.DashPattern, line.DashPhase, state);
                    if (style == RenderVisualStyle.HiddenLine && hiddenLine.HasValue)
                    {
                        DrawHiddenLine(canvas, paint, line, hiddenLine.Value, hiddenLineSettings, layerColor, state);
                    }
                    else
                    {
                        canvas.DrawLine(line.Start.X, line.Start.Y, line.End.X, line.End.Y, paint);
                    }
                    break;
                }
            case RenderPolyline polyline:
                {
                    var paint = GetStrokePaint(polyline.Color, polyline.Thickness, polyline.LineCap, polyline.LineJoin, polyline.DashPattern, polyline.DashPhase, state);
                    if (style == RenderVisualStyle.HiddenLine && hiddenLine.HasValue)
                    {
                        DrawHiddenPolyline(canvas, paint, polyline, hiddenLine.Value, hiddenLineSettings, layerColor, state);
                    }
                    else
                    {
                        DrawPolyline(canvas, paint, polyline);
                    }
                    break;
                }
            case RenderFill fill:
                DrawFill(canvas, fill);
                break;
            case RenderTriangle triangle:
                DrawTriangle(canvas, triangle);
                break;
            case RenderHatchFill hatchFill:
                DrawHatchFill(canvas, hatchFill);
                break;
            case RenderHatchPattern hatchPattern:
                {
                    DrawHatchPattern(canvas, hatchPattern, state);
                    break;
                }
            case RenderImage image:
                DrawImage(canvas, image, state);
                break;
            case RenderCircle circle:
                {
                    var paint = GetStrokePaint(circle.Color, circle.Thickness, circle.LineCap, circle.LineJoin, state);
                    canvas.DrawCircle(circle.Center.X, circle.Center.Y, circle.Radius, paint);
                    break;
                }
            case RenderArc arc:
                {
                    var paint = GetStrokePaint(arc.Color, arc.Thickness, arc.LineCap, arc.LineJoin, state);
                    DrawArc(canvas, paint, arc);
                    break;
                }
            case RenderPoint point:
                {
                    var paint = GetStrokePaint(point.Color, point.Thickness, point.LineCap, point.LineJoin, state);
                    DrawPoint(canvas, paint, point, state);
                    break;
                }
            case RenderText text:
                DrawText(canvas, text);
                break;
        }
    }

    private void DrawLineBatch(
        SKCanvas canvas,
        RenderLineBatch batch,
        bool hasViewport,
        RenderBounds viewport,
        CadRenderStateSnapshot state)
    {
        if (hasViewport && !BoundsIntersects(batch.Bounds, viewport))
        {
            return;
        }

        var key = batch.Key;
        var paint = GetStrokePaint(key.Color, key.Thickness, key.LineCap, key.LineJoin, state);
        canvas.DrawPath(batch.Geometry, paint);
    }

    private void DrawPolylineBatch(
        SKCanvas canvas,
        RenderPolylineBatch batch,
        bool hasViewport,
        RenderBounds viewport,
        CadRenderStateSnapshot state)
    {
        if (hasViewport && !BoundsIntersects(batch.Bounds, viewport))
        {
            return;
        }

        var key = batch.Key;
        var paint = GetStrokePaint(key.Color, key.Thickness, key.LineCap, key.LineJoin, state);
        canvas.DrawPath(batch.Geometry, paint);
    }

    private void DrawArcBatch(
        SKCanvas canvas,
        RenderArcBatch batch,
        bool hasViewport,
        RenderBounds viewport,
        CadRenderStateSnapshot state)
    {
        if (hasViewport && !BoundsIntersects(batch.Bounds, viewport))
        {
            return;
        }

        var key = batch.Key;
        var paint = GetStrokePaint(key.Color, key.Thickness, key.LineCap, key.LineJoin, state);
        canvas.DrawPath(batch.Geometry, paint);
    }

    private void DrawCircleBatch(
        SKCanvas canvas,
        RenderCircleBatch batch,
        bool hasViewport,
        RenderBounds viewport,
        CadRenderStateSnapshot state)
    {
        if (hasViewport && !BoundsIntersects(batch.Bounds, viewport))
        {
            return;
        }

        var key = batch.Key;
        var paint = GetStrokePaint(key.Color, key.Thickness, key.LineCap, key.LineJoin, state);
        canvas.DrawPath(batch.Geometry, paint);
    }

    private void DrawClipGroup(
        SKCanvas canvas,
        RenderClipGroup clipGroup,
        RenderVisualStyle style,
        HiddenLineContext? hiddenLine,
        RenderHiddenLineSettings hiddenLineSettings,
        RenderColor layerColor,
        bool hasViewport,
        RenderBounds viewport,
        bool isInteractive,
        CadRenderStateSnapshot state)
    {
        HiddenLineContext? clipHidden = hiddenLine;
        if (style == RenderVisualStyle.HiddenLine &&
            hiddenLine.HasValue &&
            hiddenLine.Value.TryGetClipDepth(clipGroup, out var clipDepth))
        {
            clipHidden = hiddenLine.Value.WithDepthBuffer(clipDepth);
        }

        if (clipGroup.Loops.Count == 0)
        {
            foreach (var child in clipGroup.Primitives)
            {
                DrawPrimitive(canvas, child, style, clipHidden, hiddenLineSettings, layerColor, hasViewport, viewport, isInteractive, state);
            }
            return;
        }

        var geometry = GetClipGeometry(clipGroup);
        if (geometry is null)
        {
            foreach (var child in clipGroup.Primitives)
            {
                DrawPrimitive(canvas, child, style, clipHidden, hiddenLineSettings, layerColor, hasViewport, viewport, isInteractive, state);
            }
            return;
        }

        canvas.Save();
        canvas.ClipPath(geometry, SKClipOperation.Intersect, antialias: true);
        foreach (var child in clipGroup.Primitives)
        {
            DrawPrimitive(canvas, child, style, clipHidden, hiddenLineSettings, layerColor, hasViewport, viewport, isInteractive, state);
        }
        canvas.Restore();
    }

    private static bool ShouldRenderPrimitive(
        RenderVisualStyle style,
        IRenderPrimitive primitive,
        bool isInteractive)
    {
        if (isInteractive)
        {
            return primitive is RenderLine
                || primitive is RenderPolyline
                || primitive is RenderArc
                || primitive is RenderCircle
                || primitive is RenderPoint;
        }

        if (style == RenderVisualStyle.Wireframe)
        {
            return primitive is not RenderTriangle;
        }

        if (style == RenderVisualStyle.HiddenLine)
        {
            return primitive is not (RenderFill or RenderHatchFill or RenderHatchPattern or RenderImage or RenderTriangle);
        }

        return true;
    }

    private void DrawHiddenLine(
        SKCanvas canvas,
        SKPaint paint,
        RenderLine line,
        HiddenLineContext hidden,
        RenderHiddenLineSettings hiddenLineSettings,
        RenderColor layerColor,
        CadRenderStateSnapshot state)
    {
        if (!line.HasDepth)
        {
            canvas.DrawLine(line.Start.X, line.Start.Y, line.End.X, line.End.Y, paint);
            return;
        }

        _hiddenLineSegments.Clear();
        RenderHiddenLineUtils.AppendVisibleSegments(
            hidden.DepthBuffer,
            hidden.WorldToScreen,
            line.Start,
            line.End,
            line.StartDepth!.Value,
            line.EndDepth!.Value,
            _hiddenLineSegments,
            hidden.DepthEpsilon);

        foreach (var segment in _hiddenLineSegments)
        {
            canvas.DrawLine(segment.Start.X, segment.Start.Y, segment.End.X, segment.End.Y, paint);
        }

        DrawObscuredSegments(
            canvas,
            line.Start,
            line.End,
            line.StartDepth!.Value,
            line.EndDepth!.Value,
            line.Color,
            line.Thickness,
            line.LineCap,
            line.LineJoin,
            hidden,
            hiddenLineSettings,
            layerColor,
            state);
    }

    private void DrawHiddenPolyline(
        SKCanvas canvas,
        SKPaint paint,
        RenderPolyline polyline,
        HiddenLineContext hidden,
        RenderHiddenLineSettings hiddenLineSettings,
        RenderColor layerColor,
        CadRenderStateSnapshot state)
    {
        if (!polyline.HasDepths)
        {
            DrawPolyline(canvas, paint, polyline);
            return;
        }

        var points = polyline.Points;
        var depths = polyline.Depths!;
        var segmentCount = polyline.IsClosed ? points.Count : points.Count - 1;
        for (var i = 0; i < segmentCount; i++)
        {
            var start = points[i];
            var endIndex = i == points.Count - 1 ? 0 : i + 1;
            var end = points[endIndex];
            var depthStart = depths[i];
            var depthEnd = depths[endIndex];

            _hiddenLineSegments.Clear();
            RenderHiddenLineUtils.AppendVisibleSegments(
                hidden.DepthBuffer,
                hidden.WorldToScreen,
                start,
                end,
                depthStart,
                depthEnd,
                _hiddenLineSegments,
                hidden.DepthEpsilon);

            foreach (var segment in _hiddenLineSegments)
            {
                canvas.DrawLine(segment.Start.X, segment.Start.Y, segment.End.X, segment.End.Y, paint);
            }

            DrawObscuredSegments(
                canvas,
                start,
                end,
                depthStart,
                depthEnd,
                polyline.Color,
                polyline.Thickness,
                polyline.LineCap,
                polyline.LineJoin,
                hidden,
                hiddenLineSettings,
                layerColor,
                state);
        }
    }

    private HiddenLineContext? BuildHiddenLineContext(RenderScene scene, Size size, Matrix3x2 viewTransform)
    {
        var width = (int)Math.Ceiling(size.Width);
        var height = (int)Math.Ceiling(size.Height);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var depthPrimitives = new List<IRenderPrimitive>();
        var clipGroups = new List<RenderClipGroup>();
        foreach (var layer in scene.Layers)
        {
            if (!layer.IsVisible)
            {
                continue;
            }

            CollectDepthPrimitives(layer.Primitives, depthPrimitives, clipGroups);
        }

        var hasDepth = RenderHiddenLineUtils.TryBuildDepthBuffer(
            depthPrimitives,
            viewTransform,
            _hiddenLineDepth,
            width,
            height);

        _hiddenLineClipDepths.Clear();
        var clipIndex = 0;
        foreach (var clipGroup in clipGroups)
        {
            if (clipGroup.Primitives.Count == 0)
            {
                continue;
            }

            var buffer = ResolveClipDepthBuffer(clipIndex++);
            var hasClipDepth = RenderHiddenLineUtils.TryBuildDepthBuffer(
                clipGroup.Primitives,
                viewTransform,
                buffer,
                width,
                height,
                clipGroup.Loops,
                includeClipGroups: true);
            if (hasClipDepth)
            {
                _hiddenLineClipDepths[clipGroup] = buffer;
            }
        }

        if (!hasDepth && _hiddenLineClipDepths.Count == 0)
        {
            return null;
        }

        return new HiddenLineContext(
            _hiddenLineDepth,
            viewTransform,
            RenderHiddenLineUtils.DefaultDepthEpsilon,
            _hiddenLineClipDepths);
    }

    private HiddenLineContext? ResolveHiddenLineContext(RenderScene scene, Size size, Matrix3x2 viewTransform)
    {
        if (_hiddenLineCacheValid &&
            ReferenceEquals(_hiddenLineCacheScene, scene) &&
            SizeEquals(_hiddenLineCacheSize, size) &&
            MatrixEquals(_hiddenLineCacheTransform, viewTransform))
        {
            return _hiddenLineCache;
        }

        _hiddenLineCache = BuildHiddenLineContext(scene, size, viewTransform);
        _hiddenLineCacheScene = scene;
        _hiddenLineCacheSize = size;
        _hiddenLineCacheTransform = viewTransform;
        _hiddenLineCacheValid = true;
        return _hiddenLineCache;
    }

    private void DrawPolyline(SKCanvas canvas, SKPaint paint, RenderPolyline polyline)
    {
        if (polyline.Points.Count < 2)
        {
            return;
        }

        var geometry = GetPolylineGeometry(polyline);
        canvas.DrawPath(geometry, paint);
    }

    private void DrawFill(SKCanvas canvas, RenderFill fill)
    {
        if (fill.Points.Count < 3)
        {
            return;
        }

        var geometry = GetFillGeometry(fill);
        var paint = GetFillPaint(fill.Color);
        canvas.DrawPath(geometry, paint);
    }

    private void DrawTriangle(SKCanvas canvas, RenderTriangle triangle)
    {
        var geometry = GetTriangleGeometry(triangle);
        var shaded = ApplyShade(triangle.Color, triangle.Shade);
        var paint = GetFillPaint(shaded);
        canvas.DrawPath(geometry, paint);
    }

    private static RenderColor ApplyShade(RenderColor color, float shade)
    {
        if (float.IsNaN(shade) || float.IsInfinity(shade))
        {
            shade = 1f;
        }

        shade = Math.Clamp(shade, 0f, 1f);
        var r = (byte)Math.Clamp((int)Math.Round(color.R * shade), 0, 255);
        var g = (byte)Math.Clamp((int)Math.Round(color.G * shade), 0, 255);
        var b = (byte)Math.Clamp((int)Math.Round(color.B * shade), 0, 255);
        return new RenderColor(r, g, b, color.A);
    }

    private void DrawHatchFill(SKCanvas canvas, RenderHatchFill fill)
    {
        if (fill.Loops.Count == 0)
        {
            return;
        }

        var geometry = GetHatchGeometry(fill);
        var paint = GetHatchPaint(fill);
        if (paint is null)
        {
            return;
        }

        canvas.DrawPath(geometry, paint);
    }

    private void DrawHatchPattern(SKCanvas canvas, RenderHatchPattern pattern, CadRenderStateSnapshot state)
    {
        if (pattern.Loops.Count == 0 || pattern.Segments.Count == 0)
        {
            return;
        }

        var clipGeometry = GetHatchGeometry(pattern);
        var strokeGeometry = GetHatchPatternStrokeGeometry(pattern);
        canvas.Save();
        canvas.ClipPath(clipGeometry, SKClipOperation.Intersect, antialias: true);
        var paint = GetStrokePaint(pattern.Color, pattern.Thickness, pattern.LineCap, pattern.LineJoin, state);
        canvas.DrawPath(strokeGeometry, paint);
        canvas.Restore();
    }

    private void DrawImage(SKCanvas canvas, RenderImage image, CadRenderStateSnapshot state)
    {
        if (image.Size.X <= 0 || image.Size.Y <= 0)
        {
            return;
        }

        var bitmap = ResolveBitmap(image.SourcePath);
        if (bitmap is null)
        {
            DrawImagePlaceholder(canvas, image, state);
            return;
        }

        var alpha = (byte)Math.Clamp((int)Math.Round(255f * Math.Clamp(image.Opacity, 0f, 1f)), 0, 255);
        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, alpha),
            IsAntialias = true
        };

        var matrix = new SKMatrix
        {
            ScaleX = image.UVector.X,
            SkewY = image.UVector.Y,
            SkewX = image.VVector.X,
            ScaleY = image.VVector.Y,
            TransX = image.Origin.X,
            TransY = image.Origin.Y
        };

        canvas.Save();
        canvas.Concat(ref matrix);
        var dest = new SKRect(0, 0, image.Size.X, image.Size.Y);
        canvas.DrawBitmap(bitmap, dest, paint);
        canvas.Restore();
    }

    private void DrawImagePlaceholder(SKCanvas canvas, RenderImage image, CadRenderStateSnapshot state)
    {
        var corners = GetImageCorners(image);
        if (corners.Length != 4)
        {
            return;
        }

        var paint = GetStrokePaint(image.Color, 0f, RenderLineCap.Round, RenderLineJoin.Round, state);
        var alpha = (byte)Math.Clamp((int)Math.Round(255f * Math.Clamp(image.Opacity, 0f, 1f)), 0, 255);
        var prevColor = paint.Color;
        paint.Color = new SKColor(prevColor.Red, prevColor.Green, prevColor.Blue, alpha);

        canvas.DrawLine(corners[0].X, corners[0].Y, corners[1].X, corners[1].Y, paint);
        canvas.DrawLine(corners[1].X, corners[1].Y, corners[2].X, corners[2].Y, paint);
        canvas.DrawLine(corners[2].X, corners[2].Y, corners[3].X, corners[3].Y, paint);
        canvas.DrawLine(corners[3].X, corners[3].Y, corners[0].X, corners[0].Y, paint);
        canvas.DrawLine(corners[0].X, corners[0].Y, corners[2].X, corners[2].Y, paint);
        canvas.DrawLine(corners[1].X, corners[1].Y, corners[3].X, corners[3].Y, paint);

        paint.Color = prevColor;
    }

    private static Vector2[] GetImageCorners(RenderImage image)
    {
        var origin = image.Origin;
        var u = image.UVector * image.Size.X;
        var v = image.VVector * image.Size.Y;
        return new[]
        {
            origin,
            origin + u,
            origin + u + v,
            origin + v
        };
    }

    private void DrawArc(SKCanvas canvas, SKPaint paint, RenderArc arc)
    {
        if (arc.Radius <= 0f)
        {
            return;
        }

        var geometry = GetArcGeometry(arc);
        canvas.DrawPath(geometry, paint);
    }

    private void DrawPoint(SKCanvas canvas, SKPaint paint, RenderPoint point, CadRenderStateSnapshot state)
    {
        var sizeWorld = ResolvePointSize(point, state);
        if (sizeWorld <= 0f)
        {
            return;
        }

        var center = point.Point;
        var mode = point.DisplayMode;
        var baseMode = (short)(mode & 0x1F);
        var addCircle = (mode & 32) != 0;
        var addSquare = (mode & 64) != 0;

        var half = sizeWorld * 0.5f;

        if (baseMode != 1)
        {
            switch (baseMode)
            {
                case 0:
                    {
                        var dotRadius = MathF.Max(half * 0.2f, paint.StrokeWidth * 0.5f);
                        var fillPaint = GetFillPaint(point.Color);
                        canvas.DrawCircle(center.X, center.Y, dotRadius, fillPaint);
                        break;
                    }
                case 2:
                    canvas.DrawLine(center.X - half, center.Y, center.X + half, center.Y, paint);
                    canvas.DrawLine(center.X, center.Y - half, center.X, center.Y + half, paint);
                    break;
                case 3:
                    canvas.DrawLine(center.X - half, center.Y - half, center.X + half, center.Y + half, paint);
                    canvas.DrawLine(center.X - half, center.Y + half, center.X + half, center.Y - half, paint);
                    break;
                case 4:
                    canvas.DrawLine(center.X, center.Y - half, center.X, center.Y + half, paint);
                    break;
            }
        }

        if (addCircle)
        {
            canvas.DrawCircle(center.X, center.Y, half, paint);
        }

        if (addSquare)
        {
            canvas.DrawRect(center.X - half, center.Y - half, sizeWorld, sizeWorld, paint);
        }
    }

    private float ResolvePointSize(RenderPoint point, CadRenderStateSnapshot state)
    {
        var scale = (float)(state.BaseScale * state.Zoom);
        if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
        {
            return 0f;
        }

        var sizeSetting = point.DisplaySize;
        if (sizeSetting > 0)
        {
            return (float)sizeSetting;
        }

        var viewportHeight = (float)_renderSize.Height;
        if (viewportHeight <= 0f)
        {
            return 0f;
        }

        var percent = sizeSetting < 0 ? (float)Math.Abs(sizeSetting) : 5f;
        var sizePx = viewportHeight * (percent / 100f);
        return sizePx / scale;
    }

    private void DrawText(SKCanvas canvas, RenderText text)
    {
        if (string.IsNullOrWhiteSpace(text.Text) || text.FontSize <= 0)
        {
            return;
        }

        var paint = GetTextPaint(text);
        var blob = GetTextBlob(text, paint);
        if (blob is null)
        {
            return;
        }

        var scaleX = text.WidthFactor * (text.MirrorX ? -1f : 1f);
        var scaleY = text.MirrorY ? 1f : -1f;
        var skewX = MathF.Tan(text.ObliqueAngle);

        canvas.Save();
        canvas.Translate(text.Anchor.X, text.Anchor.Y);
        if (MathF.Abs(text.Rotation) > 0.0001f)
        {
            canvas.RotateRadians(text.Rotation);
        }
        if (MathF.Abs(skewX) > 0.0001f)
        {
            canvas.Skew(skewX, 0f);
        }
        if (MathF.Abs(scaleX - 1f) > 0.0001f || MathF.Abs(scaleY - 1f) > 0.0001f)
        {
            canvas.Scale(scaleX, scaleY);
        }

        var metrics = paint.FontMetrics;
        var baseline = text.Offset.Y - metrics.Ascent;
        canvas.DrawText(blob, text.Offset.X, baseline, paint);

        if (text.Decorations != RenderTextDecoration.None)
        {
            DrawTextDecorations(canvas, text, metrics, baseline);
        }
        canvas.Restore();
    }

    private static void DrawTextDecorations(SKCanvas canvas, RenderText text, SKFontMetrics metrics, float baseline)
    {
        var thickness = ResolveDecorationThickness(metrics.UnderlineThickness, text.FontSize);
        var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            Color = ToSkiaColor(text.Color)
        };

        var startX = text.Offset.X;
        var endX = text.Offset.X + text.LayoutWidth;

        if (text.Decorations.HasFlag(RenderTextDecoration.Underline))
        {
            var underlinePos = ResolveDecorationPosition(metrics.UnderlinePosition, baseline, thickness * 0.5f);
            canvas.DrawLine(startX, underlinePos, endX, underlinePos, paint);
        }

        if (text.Decorations.HasFlag(RenderTextDecoration.StrikeThrough))
        {
            var strikePos = ResolveStrikePosition(metrics.StrikeoutPosition, baseline, thickness * 0.5f);
            canvas.DrawLine(startX, strikePos, endX, strikePos, paint);
        }

        if (text.Decorations.HasFlag(RenderTextDecoration.Overline))
        {
            var overlinePos = baseline + metrics.Ascent + thickness * 0.5f;
            canvas.DrawLine(startX, overlinePos, endX, overlinePos, paint);
        }
    }

    private static float ResolveDecorationThickness(float? metricThickness, float fontSize)
    {
        if (metricThickness.HasValue &&
            metricThickness.Value > 0f &&
            !float.IsNaN(metricThickness.Value) &&
            !float.IsInfinity(metricThickness.Value))
        {
            return metricThickness.Value;
        }

        return MathF.Max(fontSize * 0.05f, 0.5f);
    }

    private static float ResolveDecorationPosition(float? metricPosition, float baseline, float offset)
    {
        if (metricPosition.HasValue &&
            !float.IsNaN(metricPosition.Value) &&
            !float.IsInfinity(metricPosition.Value))
        {
            return baseline + metricPosition.Value + offset;
        }

        return baseline + offset;
    }

    private static float ResolveStrikePosition(float? metricPosition, float baseline, float offset)
    {
        if (metricPosition.HasValue &&
            !float.IsNaN(metricPosition.Value) &&
            !float.IsInfinity(metricPosition.Value))
        {
            return baseline + metricPosition.Value - offset;
        }

        return baseline - offset;
    }

    private LayerDrawCache GetLayerDrawCache(RenderLayer layer)
    {
        return _layerDrawCache.GetValue(layer, BuildLayerDrawCache);
    }

    private LayerDrawCache BuildLayerDrawCache(RenderLayer layer)
    {
        var ops = new List<LayerDrawOp>(layer.Primitives.Count);
        var runLines = new List<RenderLine>();
        var runPolylines = new List<RenderPolyline>();
        var runArcs = new List<RenderArc>();
        var runCircles = new List<RenderCircle>();
        LineBatchKey? runKey = null;
        LineBatchKey? runPolylineKey = null;
        LineBatchKey? runArcKey = null;
        LineBatchKey? runCircleKey = null;
        const int MaxBatchSize = 512;

        void FlushRun()
        {
            if (runLines.Count == 0 || runKey is null)
            {
                return;
            }

            var batch = BuildLineBatch(runKey.Value, runLines);
            ops.Add(new LayerDrawOp(batch));
            runLines.Clear();
            runKey = null;
        }

        void FlushPolylineRun()
        {
            if (runPolylines.Count == 0 || runPolylineKey is null)
            {
                return;
            }

            var batch = BuildPolylineBatch(runPolylineKey.Value, runPolylines);
            ops.Add(new LayerDrawOp(batch));
            runPolylines.Clear();
            runPolylineKey = null;
        }

        void FlushArcRun()
        {
            if (runArcs.Count == 0 || runArcKey is null)
            {
                return;
            }

            var batch = BuildArcBatch(runArcKey.Value, runArcs);
            ops.Add(new LayerDrawOp(batch));
            runArcs.Clear();
            runArcKey = null;
        }

        void FlushCircleRun()
        {
            if (runCircles.Count == 0 || runCircleKey is null)
            {
                return;
            }

            var batch = BuildCircleBatch(runCircleKey.Value, runCircles);
            ops.Add(new LayerDrawOp(batch));
            runCircles.Clear();
            runCircleKey = null;
        }

        void FlushRuns()
        {
            FlushRun();
            FlushPolylineRun();
            FlushArcRun();
            FlushCircleRun();
        }

        foreach (var primitive in layer.Primitives)
        {
            if (primitive is RenderLine line)
            {
                if (line.HasDashPattern)
                {
                    FlushRuns();
                    ops.Add(new LayerDrawOp(line));
                    continue;
                }

                FlushPolylineRun();
                FlushArcRun();
                FlushCircleRun();
                var key = new LineBatchKey(line.Color, line.Thickness, line.LineCap, line.LineJoin);
                if (runKey.HasValue && runKey.Value.Equals(key) && runLines.Count < MaxBatchSize)
                {
                    runLines.Add(line);
                }
                else
                {
                    FlushRun();
                    runKey = key;
                    runLines.Add(line);
                }

                continue;
            }

            if (primitive is RenderPolyline polyline)
            {
                if (polyline.HasDashPattern)
                {
                    FlushRuns();
                    ops.Add(new LayerDrawOp(polyline));
                    continue;
                }

                FlushRun();
                FlushArcRun();
                FlushCircleRun();
                var key = new LineBatchKey(polyline.Color, polyline.Thickness, polyline.LineCap, polyline.LineJoin);
                if (runPolylineKey.HasValue && runPolylineKey.Value.Equals(key) && runPolylines.Count < MaxBatchSize)
                {
                    runPolylines.Add(polyline);
                }
                else
                {
                    FlushPolylineRun();
                    runPolylineKey = key;
                    runPolylines.Add(polyline);
                }

                continue;
            }

            if (primitive is RenderArc arc)
            {
                FlushRun();
                FlushPolylineRun();
                FlushCircleRun();
                var key = new LineBatchKey(arc.Color, arc.Thickness, arc.LineCap, arc.LineJoin);
                if (runArcKey.HasValue && runArcKey.Value.Equals(key) && runArcs.Count < MaxBatchSize)
                {
                    runArcs.Add(arc);
                }
                else
                {
                    FlushArcRun();
                    runArcKey = key;
                    runArcs.Add(arc);
                }

                continue;
            }

            if (primitive is RenderCircle circle)
            {
                FlushRun();
                FlushPolylineRun();
                FlushArcRun();
                var key = new LineBatchKey(circle.Color, circle.Thickness, circle.LineCap, circle.LineJoin);
                if (runCircleKey.HasValue && runCircleKey.Value.Equals(key) && runCircles.Count < MaxBatchSize)
                {
                    runCircles.Add(circle);
                }
                else
                {
                    FlushCircleRun();
                    runCircleKey = key;
                    runCircles.Add(circle);
                }

                continue;
            }

            FlushRuns();
            ops.Add(new LayerDrawOp(primitive));
        }

        FlushRuns();
        return new LayerDrawCache(ops);
    }

    private static RenderLineBatch BuildLineBatch(LineBatchKey key, List<RenderLine> lines)
    {
        var geometry = new SKPath();
        var bounds = RenderBounds.Empty;
        foreach (var line in lines)
        {
            geometry.MoveTo(line.Start.X, line.Start.Y);
            geometry.LineTo(line.End.X, line.End.Y);
            bounds = bounds.Expand(line.Start).Expand(line.End);
        }

        return new RenderLineBatch(key, geometry, bounds);
    }

    private static RenderPolylineBatch BuildPolylineBatch(LineBatchKey key, List<RenderPolyline> polylines)
    {
        var geometry = new SKPath();
        var bounds = RenderBounds.Empty;
        foreach (var polyline in polylines)
        {
            if (polyline.Points.Count < 2)
            {
                continue;
            }

            geometry.MoveTo(polyline.Points[0].X, polyline.Points[0].Y);
            for (var i = 1; i < polyline.Points.Count; i++)
            {
                geometry.LineTo(polyline.Points[i].X, polyline.Points[i].Y);
            }
            if (polyline.IsClosed)
            {
                geometry.Close();
            }

            foreach (var point in polyline.Points)
            {
                bounds = bounds.Expand(point);
            }
        }

        return new RenderPolylineBatch(key, geometry, bounds);
    }

    private static RenderArcBatch BuildArcBatch(LineBatchKey key, List<RenderArc> arcs)
    {
        var geometry = new SKPath();
        var bounds = RenderBounds.Empty;
        foreach (var arc in arcs)
        {
            if (TryAppendArcFigure(geometry, arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle))
            {
                bounds = bounds.Expand(arc.Bounds);
            }
        }

        return new RenderArcBatch(key, geometry, bounds);
    }

    private static RenderCircleBatch BuildCircleBatch(LineBatchKey key, List<RenderCircle> circles)
    {
        var geometry = new SKPath();
        var bounds = RenderBounds.Empty;
        foreach (var circle in circles)
        {
            if (TryAppendCircleFigure(geometry, circle.Center, circle.Radius))
            {
                bounds = bounds.Expand(circle.Bounds);
            }
        }

        return new RenderCircleBatch(key, geometry, bounds);
    }

    private SKPath GetPolylineGeometry(RenderPolyline polyline)
    {
        return _polylineGeometryCache.GetValue(polyline, BuildPolylineGeometry);
    }

    private SKPath GetFillGeometry(RenderFill fill)
    {
        return _fillGeometryCache.GetValue(fill, BuildFillGeometry);
    }

    private SKPath GetTriangleGeometry(RenderTriangle triangle)
    {
        return _triangleGeometryCache.GetValue(triangle, BuildTriangleGeometry);
    }

    private SKPath GetArcGeometry(RenderArc arc)
    {
        return _arcGeometryCache.GetValue(arc, BuildArcGeometry);
    }

    private SKPath? GetClipGeometry(RenderClipGroup clipGroup)
    {
        if (clipGroup.Loops.Count == 0)
        {
            return null;
        }

        return _clipGeometryCache.GetValue(clipGroup, BuildClipGeometry);
    }

    private SKPath GetHatchGeometry(RenderHatchFill fill)
    {
        return _hatchFillGeometryCache.GetValue(fill, BuildHatchFillGeometry);
    }

    private SKPath GetHatchGeometry(RenderHatchPattern pattern)
    {
        return _hatchPatternGeometryCache.GetValue(pattern, BuildHatchPatternGeometry);
    }

    private SKPath GetHatchPatternStrokeGeometry(RenderHatchPattern pattern)
    {
        return _hatchPatternStrokeCache.GetValue(pattern, BuildHatchPatternStrokeGeometry);
    }

    private SKTextBlob? GetTextBlob(RenderText text, SKPaint paint)
    {
        return _textCache.GetValue(text, _ =>
        {
            using var font = new SKFont(paint.Typeface, paint.TextSize);
            if (text.TrackingFactor <= 0f ||
                MathF.Abs(text.TrackingFactor - 1f) < 0.001f ||
                text.Text.Length < 2)
            {
                return SKTextBlob.Create(text.Text, font);
            }

            var glyphs = paint.GetGlyphs(text.Text);
            var count = glyphs.Length;
            if (count <= 0)
            {
                return SKTextBlob.Create(text.Text, font);
            }

            var widths = new float[count];
            font.GetGlyphWidths(glyphs.AsSpan(0, count), widths, null, paint);

            var positions = new float[count];
            var x = 0f;
            var tracking = text.TrackingFactor;
            for (var i = 0; i < count; i++)
            {
                positions[i] = x;
                x += widths[i] * tracking;
            }

            using var builder = new SKTextBlobBuilder();
            builder.AddHorizontalRun(glyphs.AsSpan(0, count), font, positions.AsSpan(0, count), 0f);
            return builder.Build();
        });
    }

    private SKPaint? GetHatchPaint(RenderHatchFill fill)
    {
        if (fill.Gradient is null)
        {
            return GetFillPaint(fill.Color);
        }

        if (_hatchPaintCache.TryGetValue(fill, out var paint))
        {
            return paint;
        }

        var created = CreateHatchPaint(fill);
        if (created is null)
        {
            return null;
        }

        _hatchPaintCache.Add(fill, created);
        return created;
    }

    private void DrawGrid(SKCanvas canvas, Size size, CadRenderStateSnapshot state)
    {
        if (!TryGetWorldViewport(size, state.ViewTransform, out var viewport))
        {
            return;
        }

        var scale = (float)(state.BaseScale * state.Zoom);
        if (scale <= 0)
        {
            return;
        }

        var targetPixel = 90f;
        var targetWorld = targetPixel / scale;
        var step = CalculateGridStep(targetWorld);
        if (step <= 0)
        {
            return;
        }

        var gridColor = new RenderColor(90, 96, 110, 80);
        var paint = GetStrokePaint(gridColor, (float)(1.0 / scale), RenderLineCap.Round, RenderLineJoin.Round, state);

        var startX = MathF.Floor(viewport.Min.X / step) * step;
        var endX = viewport.Max.X;
        for (var x = startX; x <= endX; x += step)
        {
            canvas.DrawLine(x, viewport.Min.Y, x, viewport.Max.Y, paint);
        }

        var startY = MathF.Floor(viewport.Min.Y / step) * step;
        var endY = viewport.Max.Y;
        for (var y = startY; y <= endY; y += step)
        {
            canvas.DrawLine(viewport.Min.X, y, viewport.Max.X, y, paint);
        }
    }

    private void DrawAxes(SKCanvas canvas, Size size, CadRenderStateSnapshot state)
    {
        if (!TryGetWorldViewport(size, state.ViewTransform, out var viewport))
        {
            return;
        }

        var scale = (float)(state.BaseScale * state.Zoom);
        var paint = GetStrokePaint(
            new RenderColor(140, 150, 170, 160),
            (float)(1.2 / Math.Max(scale, 0.0001f)),
            RenderLineCap.Round,
            RenderLineJoin.Round,
            state);

        if (viewport.Min.X <= 0 && viewport.Max.X >= 0)
        {
            canvas.DrawLine(0, viewport.Min.Y, 0, viewport.Max.Y, paint);
        }

        if (viewport.Min.Y <= 0 && viewport.Max.Y >= 0)
        {
            canvas.DrawLine(viewport.Min.X, 0, viewport.Max.X, 0, paint);
        }
    }

    private static bool TryGetWorldViewport(Size size, Matrix3x2 viewTransform, out RenderBounds viewport)
    {
        if (!Matrix3x2.Invert(viewTransform, out var inverse))
        {
            viewport = RenderBounds.Empty;
            return false;
        }

        var corners = new[]
        {
            Vector2.Transform(Vector2.Zero, inverse),
            Vector2.Transform(new Vector2((float)size.Width, 0), inverse),
            Vector2.Transform(new Vector2(0, (float)size.Height), inverse),
            Vector2.Transform(new Vector2((float)size.Width, (float)size.Height), inverse)
        };

        var bounds = RenderBounds.Empty;
        foreach (var corner in corners)
        {
            bounds = bounds.Expand(corner);
        }

        viewport = bounds;
        return true;
    }

    private SKPaint GetStrokePaint(
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        CadRenderStateSnapshot state)
    {
        var worldThickness = ResolveWorldThickness(thickness, state);
        var key = new PenKey(color, worldThickness, lineCap, lineJoin);
        lock (_cacheLock)
        {
            if (_strokePaintCache.TryGetValue(key, out var paint) && paint is not null)
            {
                return paint;
            }

            paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = worldThickness,
                StrokeCap = ToSkiaLineCap(lineCap),
                StrokeJoin = ToSkiaLineJoin(lineJoin),
                Color = ToSkiaColor(color)
            };
            _strokePaintCache[key] = paint;
            return paint;
        }
    }

    private SKPaint GetStrokePaint(
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        float[]? dashPattern,
        float dashPhase,
        CadRenderStateSnapshot state)
    {
        if (dashPattern is null || dashPattern.Length == 0)
        {
            return GetStrokePaint(color, thickness, lineCap, lineJoin, state);
        }

        var worldThickness = ResolveWorldThickness(thickness, state);
        var dashKey = new DashPatternKey(dashPattern, dashPhase);
        var key = new DashPenKey(color, worldThickness, lineCap, lineJoin, dashKey);
        lock (_cacheLock)
        {
            if (_dashStrokePaintCache.TryGetValue(key, out var paint) && paint is not null)
            {
                return paint;
            }

            paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = worldThickness,
                StrokeCap = ToSkiaLineCap(lineCap),
                StrokeJoin = ToSkiaLineJoin(lineJoin),
                Color = ToSkiaColor(color),
                PathEffect = ResolveDashEffect(dashKey)
            };
            _dashStrokePaintCache[key] = paint;
            return paint;
        }
    }

    private SKPathEffect ResolveDashEffect(DashPatternKey key)
    {
        lock (_cacheLock)
        {
            if (_dashEffectCache.TryGetValue(key, out var effect))
            {
                return effect;
            }

            effect = SKPathEffect.CreateDash(key.Pattern, key.Phase);
            _dashEffectCache[key] = effect;
            return effect;
        }
    }

    private static float ResolveWorldThickness(float thickness, CadRenderStateSnapshot state)
    {
        var scale = (float)(state.BaseScale * state.Zoom);
        var minWorld = (float)(state.MinPixelThickness / Math.Max(scale, 0.0001f));
        return MathF.Max(thickness, minWorld);
    }

    private SKBitmap? ResolveBitmap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        lock (_cacheLock)
        {
            if (_imageCache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            if (!File.Exists(path))
            {
                _imageCache[path] = null;
                return null;
            }

            try
            {
                var bitmap = SKBitmap.Decode(path);
                _imageCache[path] = bitmap;
                return bitmap;
            }
            catch
            {
                _imageCache[path] = null;
                return null;
            }
        }
    }

    public void ClearImageCache()
    {
        lock (_cacheLock)
        {
            foreach (var entry in _imageCache.Values)
            {
                entry?.Dispose();
            }

            _imageCache.Clear();
        }
    }

    private static float CalculateGridStep(float targetWorld)
    {
        if (targetWorld <= 0)
        {
            return 0;
        }

        var magnitude = MathF.Pow(10f, MathF.Floor(MathF.Log10(targetWorld)));
        var normalized = targetWorld / magnitude;
        if (normalized >= 5)
        {
            return 10f * magnitude;
        }
        if (normalized >= 2)
        {
            return 5f * magnitude;
        }
        if (normalized >= 1)
        {
            return 2f * magnitude;
        }
        return magnitude;
    }

    private static SKColor ToSkiaColor(RenderColor color)
    {
        return new SKColor(color.R, color.G, color.B, color.A);
    }

    private static float GetViewportPadding(CadRenderStateSnapshot state)
    {
        var scale = (float)(state.BaseScale * state.Zoom);
        if (scale <= 0)
        {
            return 0f;
        }

        var pixelPadding = 4f;
        return pixelPadding / MathF.Max(scale, 0.0001f);
    }

    private static RenderBounds ExpandBounds(RenderBounds bounds, float padding)
    {
        if (bounds.IsEmpty || padding <= 0f)
        {
            return bounds;
        }

        var delta = new Vector2(padding, padding);
        return new RenderBounds(bounds.Min - delta, bounds.Max + delta);
    }

    private static bool BoundsIntersects(RenderBounds a, RenderBounds b)
    {
        if (a.IsEmpty || b.IsEmpty)
        {
            return false;
        }

        return a.Min.X <= b.Max.X &&
               a.Max.X >= b.Min.X &&
               a.Min.Y <= b.Max.Y &&
               a.Max.Y >= b.Min.Y;
    }

    private static bool SizeEquals(Size left, Size right)
    {
        const double epsilon = 0.01;
        return Math.Abs(left.Width - right.Width) <= epsilon &&
               Math.Abs(left.Height - right.Height) <= epsilon;
    }

    private static bool MatrixEquals(Matrix3x2 left, Matrix3x2 right)
    {
        const float epsilon = 0.0001f;
        return MathF.Abs(left.M11 - right.M11) <= epsilon
            && MathF.Abs(left.M12 - right.M12) <= epsilon
            && MathF.Abs(left.M21 - right.M21) <= epsilon
            && MathF.Abs(left.M22 - right.M22) <= epsilon
            && MathF.Abs(left.M31 - right.M31) <= epsilon
            && MathF.Abs(left.M32 - right.M32) <= epsilon;
    }

    private SKPaint GetFillPaint(RenderColor color)
    {
        lock (_cacheLock)
        {
            if (_fillPaintCache.TryGetValue(color, out var paint) && paint is not null)
            {
                return paint;
            }

            paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = ToSkiaColor(color)
            };
            _fillPaintCache[color] = paint;
            return paint;
        }
    }

    private SKPaint GetTextPaint(RenderText text)
    {
        var typeface = ResolveTypeface(text.FontFamily, text.IsItalic, text.IsBold);
        return new SKPaint
        {
            IsAntialias = true,
            TextSize = text.FontSize,
            Typeface = typeface,
            Color = ToSkiaColor(text.Color),
            IsStroke = false
        };
    }

    private static SKStrokeCap ToSkiaLineCap(RenderLineCap lineCap)
    {
        return lineCap switch
        {
            RenderLineCap.Round => SKStrokeCap.Round,
            RenderLineCap.Square => SKStrokeCap.Square,
            _ => SKStrokeCap.Butt
        };
    }

    private static SKStrokeJoin ToSkiaLineJoin(RenderLineJoin lineJoin)
    {
        return lineJoin switch
        {
            RenderLineJoin.Round => SKStrokeJoin.Round,
            RenderLineJoin.Bevel => SKStrokeJoin.Bevel,
            _ => SKStrokeJoin.Miter
        };
    }

    private static SKPath BuildPolylineGeometry(RenderPolyline polyline)
    {
        var geometry = new SKPath();
        if (polyline.Points.Count == 0)
        {
            return geometry;
        }

        geometry.MoveTo(polyline.Points[0].X, polyline.Points[0].Y);
        for (var i = 1; i < polyline.Points.Count; i++)
        {
            geometry.LineTo(polyline.Points[i].X, polyline.Points[i].Y);
        }
        if (polyline.IsClosed)
        {
            geometry.Close();
        }

        return geometry;
    }

    private static SKPath BuildFillGeometry(RenderFill fill)
    {
        var geometry = new SKPath();
        if (fill.Points.Count == 0)
        {
            return geometry;
        }

        geometry.MoveTo(fill.Points[0].X, fill.Points[0].Y);
        for (var i = 1; i < fill.Points.Count; i++)
        {
            geometry.LineTo(fill.Points[i].X, fill.Points[i].Y);
        }
        geometry.Close();
        geometry.FillType = SKPathFillType.EvenOdd;
        return geometry;
    }

    private static SKPath BuildTriangleGeometry(RenderTriangle triangle)
    {
        var geometry = new SKPath();
        geometry.MoveTo(triangle.A.X, triangle.A.Y);
        geometry.LineTo(triangle.B.X, triangle.B.Y);
        geometry.LineTo(triangle.C.X, triangle.C.Y);
        geometry.Close();
        geometry.FillType = SKPathFillType.Winding;
        return geometry;
    }

    private static SKPath BuildArcGeometry(RenderArc arc)
    {
        var geometry = new SKPath();
        TryAppendArcFigure(geometry, arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle);
        return geometry;
    }

    private static bool TryAppendArcFigure(
        SKPath geo,
        Vector2 center,
        float radius,
        float startAngle,
        float endAngle)
    {
        if (radius <= 0f ||
            float.IsNaN(radius) ||
            float.IsInfinity(radius) ||
            float.IsNaN(startAngle) ||
            float.IsInfinity(startAngle) ||
            float.IsNaN(endAngle) ||
            float.IsInfinity(endAngle))
        {
            return false;
        }

        var start = startAngle;
        var end = endAngle;
        if (end < start)
        {
            end += MathF.PI * 2f;
        }

        var sweep = end - start;
        if (sweep <= 0.0001f)
        {
            return false;
        }
        var rect = new SKRect(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius);
        var startDegrees = start * (180f / MathF.PI);
        var sweepDegrees = sweep * (180f / MathF.PI);
        geo.AddArc(rect, startDegrees, sweepDegrees);
        return true;
    }

    private static bool TryAppendCircleFigure(
        SKPath geo,
        Vector2 center,
        float radius)
    {
        if (radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius))
        {
            return false;
        }
        geo.AddCircle(center.X, center.Y, radius);
        return true;
    }

    private static SKPath BuildClipGeometry(RenderClipGroup clipGroup)
    {
        return BuildLoopGeometry(clipGroup.Loops, clipGroup.FillMode);
    }

    private static SKPath BuildHatchFillGeometry(RenderHatchFill fill)
    {
        return BuildLoopGeometry(fill.Loops, fill.FillMode);
    }

    private static SKPath BuildHatchPatternGeometry(RenderHatchPattern pattern)
    {
        return BuildLoopGeometry(pattern.Loops, pattern.FillMode);
    }

    private static SKPath BuildHatchPatternStrokeGeometry(RenderHatchPattern pattern)
    {
        var geometry = new SKPath();
        foreach (var segment in pattern.Segments)
        {
            geometry.MoveTo(segment.Start.X, segment.Start.Y);
            geometry.LineTo(segment.End.X, segment.End.Y);
        }

        return geometry;
    }

    private static SKPath BuildLoopGeometry(IReadOnlyList<IReadOnlyList<Vector2>> loops, RenderLoopFillMode fillMode)
    {
        var prepared = RenderLoopUtils.NormalizeLoopsForFill(loops, fillMode);
        var fillType = fillMode == RenderLoopFillMode.EvenOdd
            ? SKPathFillType.EvenOdd
            : SKPathFillType.Winding;
        var geometry = new SKPath { FillType = fillType };

        foreach (var loop in prepared)
        {
            if (loop.Count < 3)
            {
                continue;
            }

            geometry.MoveTo(loop[0].X, loop[0].Y);
            for (var i = 1; i < loop.Count; i++)
            {
                geometry.LineTo(loop[i].X, loop[i].Y);
            }
            geometry.Close();
        }

        return geometry;
    }

    private void DrawDebugOverlay(SKCanvas canvas, CadRenderStateSnapshot state)
    {
        var worldThickness = ResolveWorldThickness(0f, state);

        if (state.DebugBvhBounds is { Count: > 0 })
        {
            var bvhPaint = new SKPaint
            {
                IsAntialias = false,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = worldThickness,
                Color = new SKColor(180, 80, 200, 160)
            };

            foreach (var bounds in state.DebugBvhBounds)
            {
                if (bounds.IsEmpty)
                {
                    continue;
                }

                DrawBounds(canvas, bounds, bvhPaint);
            }
        }

        if (state.SelectionBounds.HasValue && !state.SelectionBounds.Value.IsEmpty)
        {
            var selectionPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = worldThickness,
                Color = new SKColor(0, 200, 255, 220)
            };
            DrawBounds(canvas, state.SelectionBounds.Value, selectionPaint);
        }

        if (state.HoverBounds.HasValue && !state.HoverBounds.Value.IsEmpty)
        {
            var hoverPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = worldThickness,
                Color = new SKColor(255, 200, 40, 220)
            };
            DrawBounds(canvas, state.HoverBounds.Value, hoverPaint);
        }
    }

    private void DrawAnnotations(SKCanvas canvas, CadRenderStateSnapshot state)
    {
        if (state.SelectionAnnotation.HasValue)
        {
            DrawAnnotation(canvas, state.SelectionAnnotation.Value, state);
        }

        if (state.HoverAnnotation.HasValue)
        {
            DrawAnnotation(canvas, state.HoverAnnotation.Value, state);
        }
    }

    private void DrawAnnotation(SKCanvas canvas, RenderAnnotation annotation, CadRenderStateSnapshot state)
    {
        var bounds = annotation.Bounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        var style = annotation.Style;
        var minSize = PixelsToWorld(6f, state);
        var size = bounds.Size;
        if (size.X <= 0f || size.Y <= 0f)
        {
            bounds = bounds.Inflate(minSize);
        }
        if (annotation.HasGeometry)
        {
            DrawAnnotationGeometry(canvas, annotation.Geometry!, style, state);
        }
        else
        {
            var strokeWidth = PixelsToWorld(style.StrokeWidthPixels, state);

            if (style.FillColor.HasValue)
            {
                using var fillPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = ToSkiaColor(style.FillColor.Value)
                };
                DrawBounds(canvas, bounds, fillPaint);
            }

            using (var strokePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = strokeWidth,
                Color = ToSkiaColor(style.StrokeColor)
            })
            {
                DrawBounds(canvas, bounds, strokePaint);
            }
        }

        if (!annotation.HasLabel)
        {
            return;
        }

        var textSize = PixelsToWorld(style.LabelTextSizePixels, state);
        var padding = PixelsToWorld(style.LabelPaddingPixels, state);
        var gap = PixelsToWorld(style.LabelGapPixels, state);

        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            TextSize = textSize,
            Color = ToSkiaColor(style.LabelTextColor)
        };

        var label = annotation.Label;
        var textWidth = textPaint.MeasureText(label);
        var metrics = textPaint.FontMetrics;
        var textHeight = metrics.Descent - metrics.Ascent;

        var rectWidth = textWidth + 2f * padding;
        var rectHeight = textHeight + 2f * padding;
        var rectLeft = bounds.Min.X;
        var rectBottom = bounds.Max.Y + gap;

        var rect = new SKRect(rectLeft, rectBottom, rectLeft + rectWidth, rectBottom + rectHeight);

        using (var backgroundPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = ToSkiaColor(style.LabelBackgroundColor)
        })
        {
            canvas.DrawRect(rect, backgroundPaint);
        }

        var baselineY = rect.Bottom - padding - metrics.Descent;
        canvas.Save();
        canvas.Translate(0f, baselineY);
        canvas.Scale(1f, -1f);
        canvas.Translate(0f, -baselineY);
        canvas.DrawText(label, rect.Left + padding, baselineY, textPaint);
        canvas.Restore();
    }

    private void DrawAnnotationGeometry(
        SKCanvas canvas,
        IReadOnlyList<IRenderPrimitive> geometry,
        RenderAnnotationStyle style,
        CadRenderStateSnapshot state)
    {
        if (geometry.Count == 0)
        {
            return;
        }

        var strokeWidth = PixelsToWorld(style.StrokeWidthPixels, state);
        using var strokePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = ToSkiaColor(style.StrokeColor)
        };

        SKPaint? fillPaint = null;
        if (style.FillColor.HasValue)
        {
            fillPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = ToSkiaColor(style.FillColor.Value)
            };
        }

        try
        {
            for (var i = 0; i < geometry.Count; i++)
            {
                switch (geometry[i])
                {
                    case RenderLine line:
                        strokePaint.StrokeCap = ToSkiaLineCap(line.LineCap);
                        strokePaint.StrokeJoin = ToSkiaLineJoin(line.LineJoin);
                        canvas.DrawLine(line.Start.X, line.Start.Y, line.End.X, line.End.Y, strokePaint);
                        break;
                    case RenderPolyline polyline:
                        strokePaint.StrokeCap = ToSkiaLineCap(polyline.LineCap);
                        strokePaint.StrokeJoin = ToSkiaLineJoin(polyline.LineJoin);
                        canvas.DrawPath(GetPolylineGeometry(polyline), strokePaint);
                        break;
                    case RenderCircle circle:
                        strokePaint.StrokeCap = ToSkiaLineCap(circle.LineCap);
                        strokePaint.StrokeJoin = ToSkiaLineJoin(circle.LineJoin);
                        canvas.DrawCircle(circle.Center.X, circle.Center.Y, circle.Radius, strokePaint);
                        break;
                    case RenderArc arc:
                        strokePaint.StrokeCap = ToSkiaLineCap(arc.LineCap);
                        strokePaint.StrokeJoin = ToSkiaLineJoin(arc.LineJoin);
                        DrawArc(canvas, strokePaint, arc);
                        break;
                    case RenderPoint point:
                        strokePaint.StrokeCap = ToSkiaLineCap(point.LineCap);
                        strokePaint.StrokeJoin = ToSkiaLineJoin(point.LineJoin);
                        DrawPoint(canvas, strokePaint, point, state);
                        break;
                    case RenderFill fill:
                        DrawAnnotationPath(canvas, GetFillGeometry(fill), strokePaint, fillPaint);
                        break;
                    case RenderTriangle triangle:
                        DrawAnnotationPath(canvas, GetTriangleGeometry(triangle), strokePaint, fillPaint);
                        break;
                    case RenderHatchFill hatchFill:
                        DrawAnnotationPath(canvas, GetHatchGeometry(hatchFill), strokePaint, fillPaint);
                        break;
                    case RenderHatchPattern hatchPattern:
                        DrawAnnotationPath(canvas, GetHatchGeometry(hatchPattern), strokePaint, fillPaint);
                        break;
                    case RenderClipGroup clipGroup:
                        var clipPath = GetClipGeometry(clipGroup);
                        if (clipPath is not null)
                        {
                            DrawAnnotationPath(canvas, clipPath, strokePaint, fillPaint);
                        }
                        break;
                    case RenderText text:
                        DrawAnnotationLoop(canvas,
                            RenderTextUtils.BuildTextQuad(
                                text.Anchor,
                                text.Offset,
                                text.LayoutWidth,
                                text.LayoutHeight,
                                text.WidthFactor,
                                text.Rotation,
                                text.ObliqueAngle,
                                text.MirrorX,
                                text.MirrorY),
                            strokePaint,
                            fillPaint);
                        break;
                    case RenderImage image:
                        DrawAnnotationLoop(canvas, GetImageCorners(image), strokePaint, fillPaint);
                        break;
                }
            }
        }
        finally
        {
            fillPaint?.Dispose();
        }
    }

    private static void DrawAnnotationPath(SKCanvas canvas, SKPath path, SKPaint strokePaint, SKPaint? fillPaint)
    {
        if (fillPaint is not null)
        {
            canvas.DrawPath(path, fillPaint);
        }

        canvas.DrawPath(path, strokePaint);
    }

    private static void DrawAnnotationLoop(
        SKCanvas canvas,
        IReadOnlyList<Vector2> loop,
        SKPaint strokePaint,
        SKPaint? fillPaint)
    {
        if (loop is null || loop.Count < 2)
        {
            return;
        }

        using var path = new SKPath();
        path.MoveTo(loop[0].X, loop[0].Y);
        for (var i = 1; i < loop.Count; i++)
        {
            path.LineTo(loop[i].X, loop[i].Y);
        }
        path.Close();
        DrawAnnotationPath(canvas, path, strokePaint, fillPaint);
    }


    private static float PixelsToWorld(float pixels, CadRenderStateSnapshot state)
    {
        var scale = (float)(state.BaseScale * state.Zoom);
        if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
        {
            return pixels;
        }

        return pixels / scale;
    }

    private static void DrawBounds(SKCanvas canvas, RenderBounds bounds, SKPaint paint)
    {
        var size = bounds.Size;
        if (size.X <= 0f || size.Y <= 0f)
        {
            return;
        }

        canvas.DrawRect(bounds.Min.X, bounds.Min.Y, size.X, size.Y, paint);
    }

    private static void DrawPaper(
        SKCanvas canvas,
        RenderBounds bounds,
        RenderColor fillColor,
        CadRenderStateSnapshot state)
    {
        var size = bounds.Size;
        if (size.X <= 0f || size.Y <= 0f)
        {
            return;
        }

        var rect = new SKRect(bounds.Min.X, bounds.Min.Y, bounds.Max.X, bounds.Max.Y);
        using (var fillPaint = new SKPaint
        {
            IsAntialias = false,
            Style = SKPaintStyle.Fill,
            Color = ToSkiaColor(fillColor)
        })
        {
            canvas.DrawRect(rect, fillPaint);
        }

        var strokeWidth = PixelsToWorld(1f, state);
        using (var strokePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            Color = ToSkiaColor(RenderColor.DefaultPaperOutline)
        })
        {
            canvas.DrawRect(rect, strokePaint);
        }
    }

    private static SKPaint? CreateHatchPaint(RenderHatchFill fill)
    {
        if (fill.Gradient is null)
        {
            return new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = ToSkiaColor(fill.Color)
            };
        }

        return CreateLinearGradientPaint(fill);
    }

    private static SKPaint CreateLinearGradientPaint(RenderHatchFill fill)
    {
        var bounds = fill.Bounds;
        var center = new Vector2((bounds.Min.X + bounds.Max.X) * 0.5f, (bounds.Min.Y + bounds.Max.Y) * 0.5f);
        var direction = new Vector2(MathF.Cos(fill.Gradient!.Angle), MathF.Sin(fill.Gradient.Angle));
        var half = MathF.Max(bounds.Size.X, bounds.Size.Y) * 0.5f;
        center += direction * (fill.Gradient.Shift * half);
        var start = center - direction * half;
        var end = center + direction * half;

        var colors = new SKColor[fill.Gradient.Stops.Count];
        var positions = new float[fill.Gradient.Stops.Count];
        for (var i = 0; i < fill.Gradient.Stops.Count; i++)
        {
            var stop = fill.Gradient.Stops[i];
            colors[i] = ToSkiaColor(stop.Color);
            positions[i] = stop.Offset;
        }

        var shader = SKShader.CreateLinearGradient(
            new SKPoint(start.X, start.Y),
            new SKPoint(end.X, end.Y),
            colors,
            positions,
            SKShaderTileMode.Clamp);

        return new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = shader
        };
    }

    private static SKMatrix ToSkiaMatrix(Matrix3x2 matrix)
    {
        return new SKMatrix
        {
            ScaleX = matrix.M11,
            SkewY = matrix.M12,
            SkewX = matrix.M21,
            ScaleY = matrix.M22,
            TransX = matrix.M31,
            TransY = matrix.M32,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1
        };
    }

    private SKTypeface ResolveTypeface(string? fontFamily, bool isItalic, bool isBold)
    {
        var family = string.IsNullOrWhiteSpace(fontFamily) ? SKTypeface.Default.FamilyName : fontFamily!;
        var weight = isBold ? (int)SKFontStyleWeight.Bold : (int)SKFontStyleWeight.Normal;
        var slant = isItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var key = new TypefaceKey(family, weight, (int)slant);
        lock (_cacheLock)
        {
            if (_typefaceCache.TryGetValue(key, out var cached) && cached is not null)
            {
                return cached;
            }

            var style = new SKFontStyle(weight, (int)SKFontStyleWidth.Normal, slant);
            var resolved = SKTypeface.FromFamilyName(family, style) ?? SKTypeface.Default;
            _typefaceCache[key] = resolved;
            return resolved;
        }
    }

    private readonly record struct HiddenLineContext(
        RenderDepthBuffer DepthBuffer,
        Matrix3x2 WorldToScreen,
        float DepthEpsilon,
        IReadOnlyDictionary<RenderClipGroup, RenderDepthBuffer>? ClipDepthBuffers)
    {
        public HiddenLineContext WithDepthBuffer(RenderDepthBuffer depthBuffer)
        {
            return new HiddenLineContext(depthBuffer, WorldToScreen, DepthEpsilon, ClipDepthBuffers);
        }

        public bool TryGetClipDepth(RenderClipGroup clipGroup, out RenderDepthBuffer depthBuffer)
        {
            if (ClipDepthBuffers is not null &&
                ClipDepthBuffers.TryGetValue(clipGroup, out var buffer) &&
                buffer is not null)
            {
                depthBuffer = buffer;
                return true;
            }

            depthBuffer = DepthBuffer;
            return false;
        }
    }

    private RenderDepthBuffer ResolveClipDepthBuffer(int index)
    {
        if (index < _hiddenLineClipPool.Count)
        {
            return _hiddenLineClipPool[index];
        }

        var buffer = new RenderDepthBuffer();
        _hiddenLineClipPool.Add(buffer);
        return buffer;
    }

    private static void CollectDepthPrimitives(
        IReadOnlyList<IRenderPrimitive> primitives,
        List<IRenderPrimitive> depthPrimitives,
        List<RenderClipGroup> clipGroups)
    {
        foreach (var primitive in primitives)
        {
            if (primitive is RenderClipGroup clipGroup)
            {
                clipGroups.Add(clipGroup);
                continue;
            }

            depthPrimitives.Add(primitive);
        }
    }

    private sealed class LayerDrawCache
    {
        public IReadOnlyList<LayerDrawOp> Ops { get; }

        public LayerDrawCache(IReadOnlyList<LayerDrawOp> ops)
        {
            Ops = ops;
        }
    }

    private sealed class LayerDrawOp
    {
        public RenderLineBatch? LineBatch { get; }
        public RenderPolylineBatch? PolylineBatch { get; }
        public RenderArcBatch? ArcBatch { get; }
        public RenderCircleBatch? CircleBatch { get; }
        public IRenderPrimitive? Primitive { get; }

        public LayerDrawOp(RenderLineBatch lineBatch)
        {
            LineBatch = lineBatch;
        }

        public LayerDrawOp(RenderPolylineBatch polylineBatch)
        {
            PolylineBatch = polylineBatch;
        }

        public LayerDrawOp(RenderArcBatch arcBatch)
        {
            ArcBatch = arcBatch;
        }

        public LayerDrawOp(RenderCircleBatch circleBatch)
        {
            CircleBatch = circleBatch;
        }

        public LayerDrawOp(IRenderPrimitive primitive)
        {
            Primitive = primitive;
        }
    }

    private sealed class RenderLineBatch
    {
        public LineBatchKey Key { get; }
        public SKPath Geometry { get; }
        public RenderBounds Bounds { get; }

        public RenderLineBatch(LineBatchKey key, SKPath geometry, RenderBounds bounds)
        {
            Key = key;
            Geometry = geometry;
            Bounds = bounds;
        }
    }

    private sealed class RenderPolylineBatch
    {
        public LineBatchKey Key { get; }
        public SKPath Geometry { get; }
        public RenderBounds Bounds { get; }

        public RenderPolylineBatch(LineBatchKey key, SKPath geometry, RenderBounds bounds)
        {
            Key = key;
            Geometry = geometry;
            Bounds = bounds;
        }
    }

    private sealed class RenderArcBatch
    {
        public LineBatchKey Key { get; }
        public SKPath Geometry { get; }
        public RenderBounds Bounds { get; }

        public RenderArcBatch(LineBatchKey key, SKPath geometry, RenderBounds bounds)
        {
            Key = key;
            Geometry = geometry;
            Bounds = bounds;
        }
    }

    private sealed class RenderCircleBatch
    {
        public LineBatchKey Key { get; }
        public SKPath Geometry { get; }
        public RenderBounds Bounds { get; }

        public RenderCircleBatch(LineBatchKey key, SKPath geometry, RenderBounds bounds)
        {
            Key = key;
            Geometry = geometry;
            Bounds = bounds;
        }
    }

    private readonly record struct TypefaceKey(
        string Family,
        int Weight,
        int Slant);

    private readonly record struct LineBatchKey(
        RenderColor Color,
        float Thickness,
        RenderLineCap LineCap,
        RenderLineJoin LineJoin);

    private readonly record struct PenKey(
        RenderColor Color,
        float Thickness,
        RenderLineCap LineCap,
        RenderLineJoin LineJoin);

    private readonly record struct DashPenKey(
        RenderColor Color,
        float Thickness,
        RenderLineCap LineCap,
        RenderLineJoin LineJoin,
        DashPatternKey Dash);

    private readonly struct DashPatternKey : IEquatable<DashPatternKey>
    {
        private const float Epsilon = 0.0001f;

        public float[] Pattern { get; }
        public float Phase { get; }
        private readonly int _hash;

        public DashPatternKey(float[] pattern, float phase)
        {
            Pattern = pattern;
            Phase = phase;
            _hash = ComputeHash(pattern, phase);
        }

        public bool Equals(DashPatternKey other)
        {
            if (_hash != other._hash)
            {
                return false;
            }

            if (Pattern.Length != other.Pattern.Length)
            {
                return false;
            }

            if (MathF.Abs(Phase - other.Phase) > Epsilon)
            {
                return false;
            }

            for (var i = 0; i < Pattern.Length; i++)
            {
                if (MathF.Abs(Pattern[i] - other.Pattern[i]) > Epsilon)
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            return obj is DashPatternKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _hash;
        }

        private static int ComputeHash(float[] pattern, float phase)
        {
            var hash = new HashCode();
            hash.Add(pattern.Length);
            hash.Add(phase);
            foreach (var value in pattern)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }

    private readonly record struct ObscuredPenKey(
        RenderColor Color,
        float Thickness,
        RenderLineCap LineCap,
        RenderLineJoin LineJoin,
        RenderObscuredLineType LineType);
}
