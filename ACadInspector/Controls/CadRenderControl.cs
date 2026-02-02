using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ACadInspector.Rendering;
using SkiaSharp;
using AvaloniaVector = Avalonia.Vector;

namespace ACadInspector.Controls;

public sealed class CadRenderControl : Control
{
    public static readonly StyledProperty<RenderScene?> SceneProperty =
        AvaloniaProperty.Register<CadRenderControl, RenderScene?>(nameof(Scene));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<CadRenderControl, double>(nameof(Zoom), 1.0);

    public static readonly StyledProperty<AvaloniaVector> PanProperty =
        AvaloniaProperty.Register<CadRenderControl, AvaloniaVector>(nameof(Pan));

    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<CadRenderControl, bool>(nameof(ShowGrid), true);

    public static readonly StyledProperty<bool> ShowAxesProperty =
        AvaloniaProperty.Register<CadRenderControl, bool>(nameof(ShowAxes), true);

    public static readonly StyledProperty<IReadOnlyDictionary<string, bool>?> LayerVisibilityOverridesProperty =
        AvaloniaProperty.Register<CadRenderControl, IReadOnlyDictionary<string, bool>?>(nameof(LayerVisibilityOverrides));

    public static readonly StyledProperty<int> FitToViewTriggerProperty =
        AvaloniaProperty.Register<CadRenderControl, int>(nameof(FitToViewTrigger));

    public static readonly StyledProperty<int> ResetViewTriggerProperty =
        AvaloniaProperty.Register<CadRenderControl, int>(nameof(ResetViewTrigger));

    public static readonly StyledProperty<bool> FitOnSceneChangeProperty =
        AvaloniaProperty.Register<CadRenderControl, bool>(nameof(FitOnSceneChange), true);

    public static readonly StyledProperty<double> MinZoomProperty =
        AvaloniaProperty.Register<CadRenderControl, double>(nameof(MinZoom), 0.02);

    public static readonly StyledProperty<double> MaxZoomProperty =
        AvaloniaProperty.Register<CadRenderControl, double>(nameof(MaxZoom), 500.0);

    public static readonly StyledProperty<double> FitPaddingProperty =
        AvaloniaProperty.Register<CadRenderControl, double>(nameof(FitPadding), 24.0);

    public static readonly StyledProperty<double> MinPixelThicknessProperty =
        AvaloniaProperty.Register<CadRenderControl, double>(nameof(MinPixelThickness), 0.6);

    private bool _isPanning;
    private Point _panStart;
    private AvaloniaVector _panOrigin;
    private Vector2 _sceneCenter;
    private double _baseScale = 1.0;
    private Matrix3x2 _viewTransform = Matrix3x2.Identity;
    private double _cachedZoom = 1.0;
    private readonly Dictionary<PenKey, SKPaint> _strokePaintCache = new();
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
    private bool _isInteracting;
    private DateTime _interactionUntilUtc;
    private DispatcherTimer? _interactionTimer;
    private static readonly TimeSpan InteractionHold = TimeSpan.FromMilliseconds(150);
    private RenderState _renderState = RenderState.Empty;

    public RenderScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public AvaloniaVector Pan
    {
        get => GetValue(PanProperty);
        set => SetValue(PanProperty, value);
    }

    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public bool ShowAxes
    {
        get => GetValue(ShowAxesProperty);
        set => SetValue(ShowAxesProperty, value);
    }

    public IReadOnlyDictionary<string, bool>? LayerVisibilityOverrides
    {
        get => GetValue(LayerVisibilityOverridesProperty);
        set => SetValue(LayerVisibilityOverridesProperty, value);
    }

    public int FitToViewTrigger
    {
        get => GetValue(FitToViewTriggerProperty);
        set => SetValue(FitToViewTriggerProperty, value);
    }

    public int ResetViewTrigger
    {
        get => GetValue(ResetViewTriggerProperty);
        set => SetValue(ResetViewTriggerProperty, value);
    }

    public bool FitOnSceneChange
    {
        get => GetValue(FitOnSceneChangeProperty);
        set => SetValue(FitOnSceneChangeProperty, value);
    }

    public double MinZoom
    {
        get => GetValue(MinZoomProperty);
        set => SetValue(MinZoomProperty, value);
    }

    public double MaxZoom
    {
        get => GetValue(MaxZoomProperty);
        set => SetValue(MaxZoomProperty, value);
    }

    public double FitPadding
    {
        get => GetValue(FitPaddingProperty);
        set => SetValue(FitPaddingProperty, value);
    }

    public double MinPixelThickness
    {
        get => GetValue(MinPixelThicknessProperty);
        set => SetValue(MinPixelThicknessProperty, value);
    }

    public CadRenderControl()
    {
        ClipToBounds = true;
        UpdateRenderState();
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        context.Custom(new SkiaRenderOp(this, size));
    }

    private void RenderSkia(SKCanvas canvas, Size size)
    {
        var state = Volatile.Read(ref _renderState);
        var scene = state.Scene;
        var background = scene?.Background ?? RenderColor.DefaultBackground;
        canvas.Clear(ToSkiaColor(background));

        if (scene is null || scene.Bounds.IsEmpty)
        {
            return;
        }

        var isInteractive = _isInteracting;
        var renderStyle = isInteractive ? RenderVisualStyle.Wireframe : scene.VisualStyle;
        var hasViewport = TryGetWorldViewport(size, state.ViewTransform, out var viewport);
        if (hasViewport)
        {
            var padding = GetViewportPadding(state);
            viewport = ExpandBounds(viewport, padding);
        }

        var matrix = ToSkiaMatrix(state.ViewTransform);
        canvas.Save();
        canvas.Concat(ref matrix);

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
            hiddenLine = ResolveHiddenLineContext(scene, size, state.ViewTransform);
        }

        foreach (var layer in scene.Layers)
        {
            if (!IsLayerVisible(layer, state.LayerVisibilityOverrides))
            {
                continue;
            }

            DrawLayer(canvas, layer, renderStyle, hiddenLine, hasViewport, viewport, isInteractive, state);
        }

        canvas.Restore();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        UpdateViewTransform();
        return size;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SceneProperty)
        {
            ClearImageCache();
            _hiddenLineCacheValid = false;
            _hiddenLineCacheScene = null;
            if (FitOnSceneChange)
            {
                FitToScene();
            }
            UpdateRenderState();
            InvalidateVisual();
            return;
        }

        if (change.Property == FitToViewTriggerProperty)
        {
            FitToScene();
            return;
        }

        if (change.Property == ResetViewTriggerProperty)
        {
            ResetView();
            return;
        }

        if (change.Property == ZoomProperty ||
            change.Property == PanProperty ||
            change.Property == ShowGridProperty ||
            change.Property == ShowAxesProperty ||
            change.Property == LayerVisibilityOverridesProperty ||
            change.Property == MinPixelThicknessProperty)
        {
            if (change.Property == ZoomProperty || change.Property == PanProperty)
            {
                UpdateViewTransform();
            }
            if (change.Property == MinPixelThicknessProperty)
            {
                _strokePaintCache.Clear();
            }
            UpdateRenderState();
            InvalidateVisual();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _interactionTimer?.Stop();
        ClearImageCache();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        if (!(point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed))
        {
            return;
        }

        _isPanning = true;
        MarkInteraction();
        _panStart = point.Position;
        _panOrigin = Pan;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isPanning)
        {
            return;
        }

        MarkInteraction();
        var delta = e.GetPosition(this) - _panStart;
        var next = _panOrigin + delta;
        SetCurrentValue(PanProperty, next);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        MarkInteraction();
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (Scene is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        UpdateViewTransform();

        var delta = e.Delta.Y;
        if (Math.Abs(delta) < 0.01)
        {
            return;
        }

        MarkInteraction();
        var scaleFactor = Math.Pow(1.1, delta);
        var currentZoom = Zoom;
        var nextZoom = Math.Clamp(currentZoom * scaleFactor, MinZoom, MaxZoom);
        if (Math.Abs(nextZoom - currentZoom) < 0.0001)
        {
            return;
        }

        var pointer = e.GetPosition(this);
        var world = ScreenToWorld(pointer);
        var nextPan = ComputePanForZoom(world, pointer, nextZoom);

        SetCurrentValue(ZoomProperty, nextZoom);
        SetCurrentValue(PanProperty, nextPan);
    }

    private void MarkInteraction()
    {
        _isInteracting = true;
        _interactionUntilUtc = DateTime.UtcNow + InteractionHold;
        EnsureInteractionTimer();
    }

    private void EnsureInteractionTimer()
    {
        if (_interactionTimer is null)
        {
            _interactionTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(50),
                DispatcherPriority.Render,
                OnInteractionTick);
        }

        if (!_interactionTimer.IsEnabled)
        {
            _interactionTimer.Start();
        }
    }

    private void OnInteractionTick(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow < _interactionUntilUtc)
        {
            return;
        }

        _interactionTimer?.Stop();
        if (_isInteracting)
        {
            _isInteracting = false;
            InvalidateVisual();
        }
    }

    private void FitToScene()
    {
        var scene = Scene;
        var size = Bounds.Size;
        if (scene is null || size.Width <= 0 || size.Height <= 0 || scene.Bounds.IsEmpty)
        {
            return;
        }

        var sceneSize = scene.Bounds.Size;
        if (sceneSize.X <= 0 || sceneSize.Y <= 0)
        {
            _baseScale = 1.0;
            _sceneCenter = Vector2.Zero;
            return;
        }

        var padding = (float)FitPadding;
        var width = MathF.Max(1f, (float)size.Width - 2f * padding);
        var height = MathF.Max(1f, (float)size.Height - 2f * padding);

        var scaleX = width / sceneSize.X;
        var scaleY = height / sceneSize.Y;
        _baseScale = Math.Max(0.00001, Math.Min(scaleX, scaleY));
        _sceneCenter = (scene.Bounds.Min + scene.Bounds.Max) * 0.5f;

        SetCurrentValue(ZoomProperty, 1.0);
        SetCurrentValue(PanProperty, default(AvaloniaVector));
        UpdateViewTransform();
        InvalidateVisual();
    }

    private void ResetView()
    {
        SetCurrentValue(ZoomProperty, 1.0);
        SetCurrentValue(PanProperty, default(AvaloniaVector));
        UpdateViewTransform();
        InvalidateVisual();
    }

    private void UpdateViewTransform()
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var scale = (float)(_baseScale * Zoom);
        var center = new Vector2((float)size.Width * 0.5f, (float)size.Height * 0.5f);
        var pan = new Vector2((float)Pan.X, (float)Pan.Y);

        _viewTransform = Matrix3x2.CreateTranslation(-_sceneCenter)
            * Matrix3x2.CreateScale(scale, -scale)
            * Matrix3x2.CreateTranslation(center + pan);

        if (Math.Abs(_cachedZoom - Zoom) > 0.0001)
        {
            _cachedZoom = Zoom;
            _strokePaintCache.Clear();
        }

        UpdateRenderState();
    }

    private void UpdateRenderState()
    {
        _renderState = new RenderState(
            Scene,
            ShowGrid,
            ShowAxes,
            LayerVisibilityOverrides,
            Zoom,
            MinPixelThickness,
            _baseScale,
            _viewTransform);
    }

    private sealed class RenderState
    {
        public static readonly RenderState Empty = new(
            scene: null,
            showGrid: true,
            showAxes: true,
            layerVisibilityOverrides: null,
            zoom: 1.0,
            minPixelThickness: 0.6,
            baseScale: 1.0,
            viewTransform: Matrix3x2.Identity);

        public RenderScene? Scene { get; }
        public bool ShowGrid { get; }
        public bool ShowAxes { get; }
        public IReadOnlyDictionary<string, bool>? LayerVisibilityOverrides { get; }
        public double Zoom { get; }
        public double MinPixelThickness { get; }
        public double BaseScale { get; }
        public Matrix3x2 ViewTransform { get; }

        public RenderState(
            RenderScene? scene,
            bool showGrid,
            bool showAxes,
            IReadOnlyDictionary<string, bool>? layerVisibilityOverrides,
            double zoom,
            double minPixelThickness,
            double baseScale,
            Matrix3x2 viewTransform)
        {
            Scene = scene;
            ShowGrid = showGrid;
            ShowAxes = showAxes;
            LayerVisibilityOverrides = layerVisibilityOverrides;
            Zoom = zoom;
            MinPixelThickness = minPixelThickness;
            BaseScale = baseScale;
            ViewTransform = viewTransform;
        }
    }

    private AvaloniaVector ComputePanForZoom(Vector2 worldPoint, Point pointer, double zoom)
    {
        var size = Bounds.Size;
        var center = new Vector2((float)size.Width * 0.5f, (float)size.Height * 0.5f);
        var scale = (float)(_baseScale * zoom);
        var offset = new Vector2(
            (worldPoint.X - _sceneCenter.X) * scale,
            -(worldPoint.Y - _sceneCenter.Y) * scale);
        var screen = new Vector2((float)pointer.X, (float)pointer.Y);
        var pan = screen - center - offset;
        return new AvaloniaVector(pan.X, pan.Y);
    }

    private Vector2 ScreenToWorld(Point point)
    {
        if (!Matrix3x2.Invert(_viewTransform, out var inverse))
        {
            return Vector2.Zero;
        }

        return Vector2.Transform(new Vector2((float)point.X, (float)point.Y), inverse);
    }

    private void DrawLayer(
        SKCanvas canvas,
        RenderLayer layer,
        RenderVisualStyle style,
        HiddenLineContext? hiddenLine,
        bool hasViewport,
        RenderBounds viewport,
        bool isInteractive,
        RenderState state)
    {
        if (hasViewport && !BoundsIntersects(layer.Bounds, viewport))
        {
            return;
        }

        if (style == RenderVisualStyle.HiddenLine)
        {
            foreach (var primitive in layer.Primitives)
            {
                DrawPrimitive(canvas, primitive, style, hiddenLine, hasViewport, viewport, isInteractive, state);
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
                DrawPrimitive(canvas, op.Primitive, style, hiddenLine, hasViewport, viewport, isInteractive, state);
            }
        }
    }

    private static bool IsLayerVisible(RenderLayer layer, IReadOnlyDictionary<string, bool>? overrides)
    {
        if (overrides is not null && overrides.TryGetValue(layer.Name, out var isVisible))
        {
            return isVisible;
        }

        return layer.IsVisible;
    }

    private void DrawPrimitive(
        SKCanvas canvas,
        IRenderPrimitive primitive,
        RenderVisualStyle style,
        HiddenLineContext? hiddenLine,
        bool hasViewport,
        RenderBounds viewport,
        bool isInteractive,
        RenderState state)
    {
        if (hasViewport && !BoundsIntersects(primitive.Bounds, viewport))
        {
            return;
        }

        if (primitive is RenderClipGroup clipGroup)
        {
            DrawClipGroup(canvas, clipGroup, style, hiddenLine, hasViewport, viewport, isInteractive, state);
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
                    var paint = GetStrokePaint(line.Color, line.Thickness, line.LineCap, line.LineJoin, state);
                    if (style == RenderVisualStyle.HiddenLine && hiddenLine.HasValue)
                    {
                        DrawHiddenLine(canvas, paint, line, hiddenLine.Value);
                    }
                    else
                    {
                        canvas.DrawLine(line.Start.X, line.Start.Y, line.End.X, line.End.Y, paint);
                    }
                    break;
                }
            case RenderPolyline polyline:
                {
                    var paint = GetStrokePaint(polyline.Color, polyline.Thickness, polyline.LineCap, polyline.LineJoin, state);
                    if (style == RenderVisualStyle.HiddenLine && hiddenLine.HasValue)
                    {
                        DrawHiddenPolyline(canvas, paint, polyline, hiddenLine.Value);
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
        RenderState state)
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
        RenderState state)
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
        RenderState state)
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
        RenderState state)
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
        bool hasViewport,
        RenderBounds viewport,
        bool isInteractive,
        RenderState state)
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
                DrawPrimitive(canvas, child, style, clipHidden, hasViewport, viewport, isInteractive, state);
            }
            return;
        }

        var geometry = GetClipGeometry(clipGroup);
        if (geometry is null)
        {
            foreach (var child in clipGroup.Primitives)
            {
                DrawPrimitive(canvas, child, style, clipHidden, hasViewport, viewport, isInteractive, state);
            }
            return;
        }

        canvas.Save();
        canvas.ClipPath(geometry, SKClipOperation.Intersect, antialias: true);
        foreach (var child in clipGroup.Primitives)
        {
            DrawPrimitive(canvas, child, style, clipHidden, hasViewport, viewport, isInteractive, state);
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
        HiddenLineContext hidden)
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
    }

    private void DrawHiddenPolyline(
        SKCanvas canvas,
        SKPaint paint,
        RenderPolyline polyline,
        HiddenLineContext hidden)
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

    private void DrawHatchPattern(SKCanvas canvas, RenderHatchPattern pattern, RenderState state)
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

    private void DrawImage(SKCanvas canvas, RenderImage image, RenderState state)
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

    private void DrawImagePlaceholder(SKCanvas canvas, RenderImage image, RenderState state)
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

    private void DrawPoint(SKCanvas canvas, SKPaint paint, RenderPoint point, RenderState state)
    {
        var scale = (float)(state.BaseScale * state.Zoom);
        var size = (float)Math.Max(3.0, 6.0 / Math.Max(scale, 0.0001f));
        var center = point.Point;
        var leftX = center.X - size;
        var rightX = center.X + size;
        var topY = center.Y - size;
        var bottomY = center.Y + size;
        canvas.DrawLine(leftX, center.Y, rightX, center.Y, paint);
        canvas.DrawLine(center.X, topY, center.X, bottomY, paint);
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
        canvas.Restore();
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
            return SKTextBlob.Create(text.Text, font);
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

    private void DrawGrid(SKCanvas canvas, Size size, RenderState state)
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

    private void DrawAxes(SKCanvas canvas, Size size, RenderState state)
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
        RenderState state)
    {
        var scale = (float)(state.BaseScale * state.Zoom);
        var minWorld = (float)(state.MinPixelThickness / Math.Max(scale, 0.0001f));
        var worldThickness = MathF.Max(thickness, minWorld);
        var key = new PenKey(color, worldThickness, lineCap, lineJoin);
        if (_strokePaintCache.TryGetValue(key, out var paint))
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

    private SKBitmap? ResolveBitmap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

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

    private void ClearImageCache()
    {
        foreach (var entry in _imageCache.Values)
        {
            entry?.Dispose();
        }

        _imageCache.Clear();
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

    private static float GetViewportPadding(RenderState state)
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
        if (_fillPaintCache.TryGetValue(color, out var paint))
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
        return BuildLoopGeometry(clipGroup.Loops);
    }

    private static SKPath BuildHatchFillGeometry(RenderHatchFill fill)
    {
        return BuildLoopGeometry(fill.Loops);
    }

    private static SKPath BuildHatchPatternGeometry(RenderHatchPattern pattern)
    {
        return BuildLoopGeometry(pattern.Loops);
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

    private static SKPath BuildLoopGeometry(IReadOnlyList<IReadOnlyList<Vector2>> loops)
    {
        var geometry = new SKPath
        {
            FillType = SKPathFillType.EvenOdd
        };

        foreach (var loop in loops)
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
        if (_typefaceCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var style = new SKFontStyle(weight, (int)SKFontStyleWidth.Normal, slant);
        var resolved = SKTypeface.FromFamilyName(family, style) ?? SKTypeface.Default;
        _typefaceCache[key] = resolved;
        return resolved;
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

    private sealed class SkiaRenderOp : ICustomDrawOperation
    {
        private readonly CadRenderControl _owner;
        private readonly Rect _bounds;
        private readonly Size _size;

        public SkiaRenderOp(CadRenderControl owner, Size size)
        {
            _owner = owner;
            _size = size;
            _bounds = new Rect(size);
        }

        public Rect Bounds => _bounds;

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
            if (leaseFeature == null)
            {
                return;
            }

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            if (canvas is null)
            {
                return;
            }

            _owner.RenderSkia(canvas, _size);
        }

        public bool HitTest(Point p) => _bounds.Contains(p);

        public void Dispose()
        {
        }

        public bool Equals(ICustomDrawOperation? other)
        {
            return ReferenceEquals(this, other);
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
}
