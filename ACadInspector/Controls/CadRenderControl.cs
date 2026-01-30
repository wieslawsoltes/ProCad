using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ACadInspector.Rendering;
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
    private readonly Dictionary<PenKey, Pen> _penCache = new();
    private readonly Dictionary<RenderColor, SolidColorBrush> _brushCache = new();
    private readonly Dictionary<TypefaceKey, Typeface> _typefaceCache = new();
    private readonly ConditionalWeakTable<RenderPolyline, StreamGeometry> _polylineGeometryCache = new();
    private readonly ConditionalWeakTable<RenderFill, StreamGeometry> _fillGeometryCache = new();
    private readonly ConditionalWeakTable<RenderTriangle, StreamGeometry> _triangleGeometryCache = new();
    private readonly ConditionalWeakTable<RenderArc, StreamGeometry> _arcGeometryCache = new();
    private readonly ConditionalWeakTable<RenderClipGroup, StreamGeometry> _clipGeometryCache = new();
    private readonly ConditionalWeakTable<RenderHatchFill, StreamGeometry> _hatchFillGeometryCache = new();
    private readonly ConditionalWeakTable<RenderHatchPattern, StreamGeometry> _hatchPatternGeometryCache = new();
    private readonly ConditionalWeakTable<RenderHatchPattern, StreamGeometry> _hatchPatternStrokeCache = new();
    private readonly ConditionalWeakTable<RenderHatchFill, IBrush> _hatchBrushCache = new();
    private readonly ConditionalWeakTable<RenderText, FormattedText> _textCache = new();
    private readonly ConditionalWeakTable<RenderLayer, LayerDrawCache> _layerDrawCache = new();
    private readonly Dictionary<string, Bitmap?> _imageCache = new(StringComparer.OrdinalIgnoreCase);
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
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        var background = Scene?.Background ?? RenderColor.DefaultBackground;
        context.FillRectangle(GetSolidBrush(background), new Rect(size));

        if (Scene is null || size.Width <= 0 || size.Height <= 0 || Scene.Bounds.IsEmpty)
        {
            return;
        }

        UpdateViewTransform();
        var isInteractive = _isInteracting;
        var renderStyle = isInteractive ? RenderVisualStyle.Wireframe : Scene.VisualStyle;
        var matrix = ToAvaloniaMatrix(_viewTransform);
        var hasViewport = TryGetWorldViewport(out var viewport);
        if (hasViewport)
        {
            var padding = GetViewportPadding();
            viewport = ExpandBounds(viewport, padding);
        }

        using (context.PushTransform(matrix))
        {
            if (ShowGrid && !isInteractive)
            {
                DrawGrid(context);
            }

            if (ShowAxes && !isInteractive)
            {
                DrawAxes(context);
            }

            HiddenLineContext? hiddenLine = null;
            if (!isInteractive && renderStyle == RenderVisualStyle.HiddenLine)
            {
                hiddenLine = ResolveHiddenLineContext(Scene, size);
            }

            foreach (var layer in Scene.Layers)
            {
                if (!IsLayerVisible(layer))
                {
                    continue;
                }

                DrawLayer(context, layer, renderStyle, hiddenLine, hasViewport, viewport, isInteractive);
            }
        }
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
            change.Property == LayerVisibilityOverridesProperty)
        {
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
            _penCache.Clear();
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
        DrawingContext context,
        RenderLayer layer,
        RenderVisualStyle style,
        HiddenLineContext? hiddenLine,
        bool hasViewport,
        RenderBounds viewport,
        bool isInteractive)
    {
        if (hasViewport && !BoundsIntersects(layer.Bounds, viewport))
        {
            return;
        }

        if (style == RenderVisualStyle.HiddenLine)
        {
            foreach (var primitive in layer.Primitives)
            {
                DrawPrimitive(context, primitive, style, hiddenLine, hasViewport, viewport, isInteractive);
            }

            return;
        }

        var drawCache = GetLayerDrawCache(layer);
        foreach (var op in drawCache.Ops)
        {
            if (op.LineBatch is not null)
            {
                DrawLineBatch(context, op.LineBatch, hasViewport, viewport);
                continue;
            }

            if (op.PolylineBatch is not null)
            {
                DrawPolylineBatch(context, op.PolylineBatch, hasViewport, viewport);
                continue;
            }

            if (op.ArcBatch is not null)
            {
                DrawArcBatch(context, op.ArcBatch, hasViewport, viewport);
                continue;
            }

            if (op.CircleBatch is not null)
            {
                DrawCircleBatch(context, op.CircleBatch, hasViewport, viewport);
                continue;
            }

            if (op.Primitive is not null)
            {
                DrawPrimitive(context, op.Primitive, style, hiddenLine, hasViewport, viewport, isInteractive);
            }
        }
    }

    private bool IsLayerVisible(RenderLayer layer)
    {
        var overrides = LayerVisibilityOverrides;
        if (overrides is not null && overrides.TryGetValue(layer.Name, out var isVisible))
        {
            return isVisible;
        }

        return layer.IsVisible;
    }

    private void DrawPrimitive(
        DrawingContext context,
        IRenderPrimitive primitive,
        RenderVisualStyle style,
        HiddenLineContext? hiddenLine,
        bool hasViewport,
        RenderBounds viewport,
        bool isInteractive)
    {
        if (hasViewport && !BoundsIntersects(primitive.Bounds, viewport))
        {
            return;
        }

        if (primitive is RenderClipGroup clipGroup)
        {
            DrawClipGroup(context, clipGroup, style, hiddenLine, hasViewport, viewport, isInteractive);
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
                    var pen = GetPen(line.Color, line.Thickness, line.LineCap, line.LineJoin);
                    if (style == RenderVisualStyle.HiddenLine && hiddenLine.HasValue)
                    {
                        DrawHiddenLine(context, pen, line, hiddenLine.Value);
                    }
                    else
                    {
                        context.DrawLine(pen, ToPoint(line.Start), ToPoint(line.End));
                    }
                    break;
                }
            case RenderPolyline polyline:
                {
                    var pen = GetPen(polyline.Color, polyline.Thickness, polyline.LineCap, polyline.LineJoin);
                    if (style == RenderVisualStyle.HiddenLine && hiddenLine.HasValue)
                    {
                        DrawHiddenPolyline(context, pen, polyline, hiddenLine.Value);
                    }
                    else
                    {
                        DrawPolyline(context, pen, polyline);
                    }
                    break;
                }
            case RenderFill fill:
                DrawFill(context, fill);
                break;
            case RenderTriangle triangle:
                DrawTriangle(context, triangle);
                break;
            case RenderHatchFill hatchFill:
                DrawHatchFill(context, hatchFill);
                break;
            case RenderHatchPattern hatchPattern:
                {
                    DrawHatchPattern(context, hatchPattern);
                    break;
                }
            case RenderImage image:
                DrawImage(context, image);
                break;
            case RenderCircle circle:
                {
                    var pen = GetPen(circle.Color, circle.Thickness, circle.LineCap, circle.LineJoin);
                    context.DrawEllipse(null, pen, ToPoint(circle.Center), circle.Radius, circle.Radius);
                    break;
                }
            case RenderArc arc:
                {
                    var pen = GetPen(arc.Color, arc.Thickness, arc.LineCap, arc.LineJoin);
                    DrawArc(context, pen, arc);
                    break;
                }
            case RenderPoint point:
                {
                    var pen = GetPen(point.Color, point.Thickness, point.LineCap, point.LineJoin);
                    DrawPoint(context, pen, point);
                    break;
                }
            case RenderText text:
                DrawText(context, text);
                break;
        }
    }

    private void DrawLineBatch(
        DrawingContext context,
        RenderLineBatch batch,
        bool hasViewport,
        RenderBounds viewport)
    {
        if (hasViewport && !BoundsIntersects(batch.Bounds, viewport))
        {
            return;
        }

        var key = batch.Key;
        var pen = GetPen(key.Color, key.Thickness, key.LineCap, key.LineJoin);
        context.DrawGeometry(null, pen, batch.Geometry);
    }

    private void DrawPolylineBatch(
        DrawingContext context,
        RenderPolylineBatch batch,
        bool hasViewport,
        RenderBounds viewport)
    {
        if (hasViewport && !BoundsIntersects(batch.Bounds, viewport))
        {
            return;
        }

        var key = batch.Key;
        var pen = GetPen(key.Color, key.Thickness, key.LineCap, key.LineJoin);
        context.DrawGeometry(null, pen, batch.Geometry);
    }

    private void DrawArcBatch(
        DrawingContext context,
        RenderArcBatch batch,
        bool hasViewport,
        RenderBounds viewport)
    {
        if (hasViewport && !BoundsIntersects(batch.Bounds, viewport))
        {
            return;
        }

        var key = batch.Key;
        var pen = GetPen(key.Color, key.Thickness, key.LineCap, key.LineJoin);
        context.DrawGeometry(null, pen, batch.Geometry);
    }

    private void DrawCircleBatch(
        DrawingContext context,
        RenderCircleBatch batch,
        bool hasViewport,
        RenderBounds viewport)
    {
        if (hasViewport && !BoundsIntersects(batch.Bounds, viewport))
        {
            return;
        }

        var key = batch.Key;
        var pen = GetPen(key.Color, key.Thickness, key.LineCap, key.LineJoin);
        context.DrawGeometry(null, pen, batch.Geometry);
    }

    private void DrawClipGroup(
        DrawingContext context,
        RenderClipGroup clipGroup,
        RenderVisualStyle style,
        HiddenLineContext? hiddenLine,
        bool hasViewport,
        RenderBounds viewport,
        bool isInteractive)
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
                DrawPrimitive(context, child, style, clipHidden, hasViewport, viewport, isInteractive);
            }
            return;
        }

        var geometry = GetClipGeometry(clipGroup);
        if (geometry is null)
        {
            foreach (var child in clipGroup.Primitives)
            {
                DrawPrimitive(context, child, style, clipHidden, hasViewport, viewport, isInteractive);
            }
            return;
        }
        using (context.PushGeometryClip(geometry))
        {
            foreach (var child in clipGroup.Primitives)
            {
                DrawPrimitive(context, child, style, clipHidden, hasViewport, viewport, isInteractive);
            }
        }
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
        DrawingContext context,
        Pen pen,
        RenderLine line,
        HiddenLineContext hidden)
    {
        if (!line.HasDepth)
        {
            context.DrawLine(pen, ToPoint(line.Start), ToPoint(line.End));
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
            context.DrawLine(pen, ToPoint(segment.Start), ToPoint(segment.End));
        }
    }

    private void DrawHiddenPolyline(
        DrawingContext context,
        Pen pen,
        RenderPolyline polyline,
        HiddenLineContext hidden)
    {
        if (!polyline.HasDepths)
        {
            DrawPolyline(context, pen, polyline);
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
                context.DrawLine(pen, ToPoint(segment.Start), ToPoint(segment.End));
            }
        }
    }

    private HiddenLineContext? BuildHiddenLineContext(RenderScene scene, Size size)
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
            _viewTransform,
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
                _viewTransform,
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
            _viewTransform,
            RenderHiddenLineUtils.DefaultDepthEpsilon,
            _hiddenLineClipDepths);
    }

    private HiddenLineContext? ResolveHiddenLineContext(RenderScene scene, Size size)
    {
        if (_hiddenLineCacheValid &&
            ReferenceEquals(_hiddenLineCacheScene, scene) &&
            SizeEquals(_hiddenLineCacheSize, size) &&
            MatrixEquals(_hiddenLineCacheTransform, _viewTransform))
        {
            return _hiddenLineCache;
        }

        _hiddenLineCache = BuildHiddenLineContext(scene, size);
        _hiddenLineCacheScene = scene;
        _hiddenLineCacheSize = size;
        _hiddenLineCacheTransform = _viewTransform;
        _hiddenLineCacheValid = true;
        return _hiddenLineCache;
    }

    private void DrawPolyline(DrawingContext context, Pen pen, RenderPolyline polyline)
    {
        if (polyline.Points.Count < 2)
        {
            return;
        }

        var geometry = GetPolylineGeometry(polyline);
        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawFill(DrawingContext context, RenderFill fill)
    {
        if (fill.Points.Count < 3)
        {
            return;
        }

        var geometry = GetFillGeometry(fill);
        var brush = GetSolidBrush(fill.Color);
        context.DrawGeometry(brush, null, geometry);
    }

    private void DrawTriangle(DrawingContext context, RenderTriangle triangle)
    {
        var geometry = GetTriangleGeometry(triangle);
        var shaded = ApplyShade(triangle.Color, triangle.Shade);
        var brush = GetSolidBrush(shaded);
        context.DrawGeometry(brush, null, geometry);
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

    private void DrawHatchFill(DrawingContext context, RenderHatchFill fill)
    {
        if (fill.Loops.Count == 0)
        {
            return;
        }

        var geometry = GetHatchGeometry(fill);
        var brush = GetHatchBrush(fill);
        if (brush is null)
        {
            return;
        }

        context.DrawGeometry(brush, null, geometry);
    }

    private void DrawHatchPattern(DrawingContext context, RenderHatchPattern pattern)
    {
        if (pattern.Loops.Count == 0 || pattern.Segments.Count == 0)
        {
            return;
        }

        var clipGeometry = GetHatchGeometry(pattern);
        var strokeGeometry = GetHatchPatternStrokeGeometry(pattern);
        using (context.PushGeometryClip(clipGeometry))
        {
            var pen = GetPen(pattern.Color, pattern.Thickness, pattern.LineCap, pattern.LineJoin);
            context.DrawGeometry(null, pen, strokeGeometry);
        }
    }

    private void DrawImage(DrawingContext context, RenderImage image)
    {
        if (image.Size.X <= 0 || image.Size.Y <= 0)
        {
            return;
        }

        var bitmap = ResolveBitmap(image.SourcePath);
        if (bitmap is null)
        {
            DrawImagePlaceholder(context, image);
            return;
        }

        var matrix = new Matrix(
            image.UVector.X,
            image.UVector.Y,
            image.VVector.X,
            image.VVector.Y,
            image.Origin.X,
            image.Origin.Y);

        using (context.PushOpacity(Math.Clamp(image.Opacity, 0f, 1f)))
        using (context.PushTransform(matrix))
        {
            context.DrawImage(bitmap, new Rect(0, 0, image.Size.X, image.Size.Y));
        }
    }

    private void DrawImagePlaceholder(DrawingContext context, RenderImage image)
    {
        var corners = GetImageCorners(image);
        if (corners.Length != 4)
        {
            return;
        }

        using (context.PushOpacity(Math.Clamp(image.Opacity, 0f, 1f)))
        {
            var pen = GetPen(image.Color, 0f, RenderLineCap.Round, RenderLineJoin.Round);
            context.DrawLine(pen, ToPoint(corners[0]), ToPoint(corners[1]));
            context.DrawLine(pen, ToPoint(corners[1]), ToPoint(corners[2]));
            context.DrawLine(pen, ToPoint(corners[2]), ToPoint(corners[3]));
            context.DrawLine(pen, ToPoint(corners[3]), ToPoint(corners[0]));
            context.DrawLine(pen, ToPoint(corners[0]), ToPoint(corners[2]));
            context.DrawLine(pen, ToPoint(corners[1]), ToPoint(corners[3]));
        }
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

    private void DrawArc(DrawingContext context, Pen pen, RenderArc arc)
    {
        if (arc.Radius <= 0f)
        {
            return;
        }

        var geometry = GetArcGeometry(arc);
        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawPoint(DrawingContext context, Pen pen, RenderPoint point)
    {
        var scale = (float)(_baseScale * Zoom);
        var size = (float)Math.Max(3.0, 6.0 / Math.Max(scale, 0.0001f));
        var center = point.Point;
        var left = new Point(center.X - size, center.Y);
        var right = new Point(center.X + size, center.Y);
        var top = new Point(center.X, center.Y - size);
        var bottom = new Point(center.X, center.Y + size);
        context.DrawLine(pen, left, right);
        context.DrawLine(pen, top, bottom);
    }

    private void DrawText(DrawingContext context, RenderText text)
    {
        if (string.IsNullOrWhiteSpace(text.Text) || text.FontSize <= 0)
        {
            return;
        }

        var formatted = GetFormattedText(text);

        var scaleX = text.WidthFactor * (text.MirrorX ? -1 : 1);
        var scaleY = text.MirrorY ? 1 : -1;
        using (context.PushTransform(Matrix.CreateScale(scaleX, scaleY)))
        using (context.PushTransform(Matrix.CreateSkew(text.ObliqueAngle, 0)))
        using (context.PushTransform(Matrix.CreateRotation(text.Rotation)))
        using (context.PushTransform(Matrix.CreateTranslation(text.Anchor.X, text.Anchor.Y)))
        {
            context.DrawText(formatted, new Point(text.Offset.X, text.Offset.Y));
        }
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
        var geometry = new StreamGeometry();
        var bounds = RenderBounds.Empty;
        using (var geo = geometry.Open())
        {
            foreach (var line in lines)
            {
                geo.BeginFigure(ToPoint(line.Start), isFilled: false);
                geo.LineTo(ToPoint(line.End));
                geo.EndFigure(isClosed: false);
                bounds = bounds.Expand(line.Start).Expand(line.End);
            }
        }

        return new RenderLineBatch(key, geometry, bounds);
    }

    private static RenderPolylineBatch BuildPolylineBatch(LineBatchKey key, List<RenderPolyline> polylines)
    {
        var geometry = new StreamGeometry();
        var bounds = RenderBounds.Empty;
        using (var geo = geometry.Open())
        {
            foreach (var polyline in polylines)
            {
                if (polyline.Points.Count < 2)
                {
                    continue;
                }

                geo.BeginFigure(ToPoint(polyline.Points[0]), isFilled: false);
                for (var i = 1; i < polyline.Points.Count; i++)
                {
                    geo.LineTo(ToPoint(polyline.Points[i]));
                }
                geo.EndFigure(polyline.IsClosed);

                foreach (var point in polyline.Points)
                {
                    bounds = bounds.Expand(point);
                }
            }
        }

        return new RenderPolylineBatch(key, geometry, bounds);
    }

    private static RenderArcBatch BuildArcBatch(LineBatchKey key, List<RenderArc> arcs)
    {
        var geometry = new StreamGeometry();
        var bounds = RenderBounds.Empty;
        using (var geo = geometry.Open())
        {
            foreach (var arc in arcs)
            {
                if (TryAppendArcFigure(geo, arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle))
                {
                    bounds = bounds.Expand(arc.Bounds);
                }
            }
        }

        return new RenderArcBatch(key, geometry, bounds);
    }

    private static RenderCircleBatch BuildCircleBatch(LineBatchKey key, List<RenderCircle> circles)
    {
        var geometry = new StreamGeometry();
        var bounds = RenderBounds.Empty;
        using (var geo = geometry.Open())
        {
            foreach (var circle in circles)
            {
                if (TryAppendCircleFigure(geo, circle.Center, circle.Radius))
                {
                    bounds = bounds.Expand(circle.Bounds);
                }
            }
        }

        return new RenderCircleBatch(key, geometry, bounds);
    }

    private StreamGeometry GetPolylineGeometry(RenderPolyline polyline)
    {
        return _polylineGeometryCache.GetValue(polyline, BuildPolylineGeometry);
    }

    private StreamGeometry GetFillGeometry(RenderFill fill)
    {
        return _fillGeometryCache.GetValue(fill, BuildFillGeometry);
    }

    private StreamGeometry GetTriangleGeometry(RenderTriangle triangle)
    {
        return _triangleGeometryCache.GetValue(triangle, BuildTriangleGeometry);
    }

    private StreamGeometry GetArcGeometry(RenderArc arc)
    {
        return _arcGeometryCache.GetValue(arc, BuildArcGeometry);
    }

    private StreamGeometry? GetClipGeometry(RenderClipGroup clipGroup)
    {
        if (clipGroup.Loops.Count == 0)
        {
            return null;
        }

        return _clipGeometryCache.GetValue(clipGroup, BuildClipGeometry);
    }

    private StreamGeometry GetHatchGeometry(RenderHatchFill fill)
    {
        return _hatchFillGeometryCache.GetValue(fill, BuildHatchFillGeometry);
    }

    private StreamGeometry GetHatchGeometry(RenderHatchPattern pattern)
    {
        return _hatchPatternGeometryCache.GetValue(pattern, BuildHatchPatternGeometry);
    }

    private StreamGeometry GetHatchPatternStrokeGeometry(RenderHatchPattern pattern)
    {
        return _hatchPatternStrokeCache.GetValue(pattern, BuildHatchPatternStrokeGeometry);
    }

    private FormattedText GetFormattedText(RenderText text)
    {
        return _textCache.GetValue(text, BuildFormattedText);
    }

    private IBrush? GetHatchBrush(RenderHatchFill fill)
    {
        if (fill.Gradient is null)
        {
            return GetSolidBrush(fill.Color);
        }

        if (_hatchBrushCache.TryGetValue(fill, out var brush))
        {
            return brush;
        }

        var created = CreateHatchBrush(fill);
        if (created is null)
        {
            return null;
        }

        _hatchBrushCache.Add(fill, created);
        return created;
    }

    private void DrawGrid(DrawingContext context)
    {
        if (!TryGetWorldViewport(out var viewport))
        {
            return;
        }

        var scale = (float)(_baseScale * Zoom);
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
        var pen = GetPen(gridColor, (float)(1.0 / scale), RenderLineCap.Round, RenderLineJoin.Round);

        var startX = MathF.Floor(viewport.Min.X / step) * step;
        var endX = viewport.Max.X;
        for (var x = startX; x <= endX; x += step)
        {
            context.DrawLine(pen, new Point(x, viewport.Min.Y), new Point(x, viewport.Max.Y));
        }

        var startY = MathF.Floor(viewport.Min.Y / step) * step;
        var endY = viewport.Max.Y;
        for (var y = startY; y <= endY; y += step)
        {
            context.DrawLine(pen, new Point(viewport.Min.X, y), new Point(viewport.Max.X, y));
        }
    }

    private void DrawAxes(DrawingContext context)
    {
        if (!TryGetWorldViewport(out var viewport))
        {
            return;
        }

        var scale = (float)(_baseScale * Zoom);
        var pen = GetPen(
            new RenderColor(140, 150, 170, 160),
            (float)(1.2 / Math.Max(scale, 0.0001f)),
            RenderLineCap.Round,
            RenderLineJoin.Round);

        if (viewport.Min.X <= 0 && viewport.Max.X >= 0)
        {
            context.DrawLine(pen, new Point(0, viewport.Min.Y), new Point(0, viewport.Max.Y));
        }

        if (viewport.Min.Y <= 0 && viewport.Max.Y >= 0)
        {
            context.DrawLine(pen, new Point(viewport.Min.X, 0), new Point(viewport.Max.X, 0));
        }
    }

    private bool TryGetWorldViewport(out RenderBounds viewport)
    {
        if (!Matrix3x2.Invert(_viewTransform, out var inverse))
        {
            viewport = RenderBounds.Empty;
            return false;
        }

        var size = Bounds.Size;
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

    private Pen GetPen(RenderColor color, float thickness, RenderLineCap lineCap, RenderLineJoin lineJoin)
    {
        var scale = (float)(_baseScale * Zoom);
        var minWorld = (float)(MinPixelThickness / Math.Max(scale, 0.0001f));
        var worldThickness = MathF.Max(thickness, minWorld);
        var key = new PenKey(color, worldThickness, lineCap, lineJoin);
        if (_penCache.TryGetValue(key, out var pen))
        {
            return pen;
        }

        pen = new Pen(GetSolidBrush(color), worldThickness)
        {
            LineCap = ToAvaloniaLineCap(lineCap),
            LineJoin = ToAvaloniaLineJoin(lineJoin)
        };
        _penCache[key] = pen;
        return pen;
    }

    private Bitmap? ResolveBitmap(string? path)
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
            var bitmap = new Bitmap(path);
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

    private static Point ToPoint(Vector2 value) => new(value.X, value.Y);

    private static Color ToAvaloniaColor(RenderColor color)
    {
        return Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    private float GetViewportPadding()
    {
        var scale = (float)(_baseScale * Zoom);
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

    private SolidColorBrush GetSolidBrush(RenderColor color)
    {
        if (_brushCache.TryGetValue(color, out var brush))
        {
            return brush;
        }

        brush = new SolidColorBrush(ToAvaloniaColor(color));
        _brushCache[color] = brush;
        return brush;
    }

    private static PenLineCap ToAvaloniaLineCap(RenderLineCap lineCap)
    {
        return lineCap switch
        {
            RenderLineCap.Round => PenLineCap.Round,
            RenderLineCap.Square => PenLineCap.Square,
            _ => PenLineCap.Flat
        };
    }

    private static PenLineJoin ToAvaloniaLineJoin(RenderLineJoin lineJoin)
    {
        return lineJoin switch
        {
            RenderLineJoin.Round => PenLineJoin.Round,
            RenderLineJoin.Bevel => PenLineJoin.Bevel,
            _ => PenLineJoin.Miter
        };
    }

    private static StreamGeometry BuildPolylineGeometry(RenderPolyline polyline)
    {
        var geometry = new StreamGeometry();
        using (var geo = geometry.Open())
        {
            geo.BeginFigure(ToPoint(polyline.Points[0]), isFilled: false);
            for (var i = 1; i < polyline.Points.Count; i++)
            {
                geo.LineTo(ToPoint(polyline.Points[i]));
            }
            geo.EndFigure(polyline.IsClosed);
        }

        return geometry;
    }

    private static StreamGeometry BuildFillGeometry(RenderFill fill)
    {
        var geometry = new StreamGeometry();
        using (var geo = geometry.Open())
        {
            geo.BeginFigure(ToPoint(fill.Points[0]), isFilled: true);
            for (var i = 1; i < fill.Points.Count; i++)
            {
                geo.LineTo(ToPoint(fill.Points[i]));
            }
            geo.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static StreamGeometry BuildTriangleGeometry(RenderTriangle triangle)
    {
        var geometry = new StreamGeometry();
        using (var geo = geometry.Open())
        {
            geo.BeginFigure(ToPoint(triangle.A), isFilled: true);
            geo.LineTo(ToPoint(triangle.B));
            geo.LineTo(ToPoint(triangle.C));
            geo.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static StreamGeometry BuildArcGeometry(RenderArc arc)
    {
        var geometry = new StreamGeometry();
        using (var geo = geometry.Open())
        {
            TryAppendArcFigure(geo, arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle);
        }

        return geometry;
    }

    private static bool TryAppendArcFigure(
        StreamGeometryContext geo,
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

        var startPoint = new Point(
            center.X + radius * MathF.Cos(start),
            center.Y + radius * MathF.Sin(start));
        var endPoint = new Point(
            center.X + radius * MathF.Cos(end),
            center.Y + radius * MathF.Sin(end));
        var isLargeArc = sweep > MathF.PI;

        geo.BeginFigure(startPoint, isFilled: false);
        geo.ArcTo(endPoint, new Size(radius, radius), 0, isLargeArc, SweepDirection.CounterClockwise);
        geo.EndFigure(isClosed: false);
        return true;
    }

    private static bool TryAppendCircleFigure(
        StreamGeometryContext geo,
        Vector2 center,
        float radius)
    {
        if (radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius))
        {
            return false;
        }

        var startPoint = new Point(center.X + radius, center.Y);
        var midPoint = new Point(center.X - radius, center.Y);
        var size = new Size(radius, radius);

        geo.BeginFigure(startPoint, isFilled: false);
        geo.ArcTo(midPoint, size, 0, isLargeArc: false, SweepDirection.CounterClockwise);
        geo.ArcTo(startPoint, size, 0, isLargeArc: false, SweepDirection.CounterClockwise);
        geo.EndFigure(isClosed: false);
        return true;
    }

    private static StreamGeometry BuildClipGeometry(RenderClipGroup clipGroup)
    {
        return BuildLoopGeometry(clipGroup.Loops);
    }

    private static StreamGeometry BuildHatchFillGeometry(RenderHatchFill fill)
    {
        return BuildLoopGeometry(fill.Loops);
    }

    private static StreamGeometry BuildHatchPatternGeometry(RenderHatchPattern pattern)
    {
        return BuildLoopGeometry(pattern.Loops);
    }

    private static StreamGeometry BuildHatchPatternStrokeGeometry(RenderHatchPattern pattern)
    {
        var geometry = new StreamGeometry();
        using (var geo = geometry.Open())
        {
            foreach (var segment in pattern.Segments)
            {
                geo.BeginFigure(ToPoint(segment.Start), isFilled: false);
                geo.LineTo(ToPoint(segment.End));
                geo.EndFigure(isClosed: false);
            }
        }

        return geometry;
    }

    private FormattedText BuildFormattedText(RenderText text)
    {
        var typeface = ResolveTypeface(text.FontFamily, text.IsItalic, text.IsBold);
        var brush = GetSolidBrush(text.Color);
        return new FormattedText(
            text.Text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            text.FontSize,
            brush);
    }

    private static StreamGeometry BuildLoopGeometry(IReadOnlyList<IReadOnlyList<Vector2>> loops)
    {
        var geometry = new StreamGeometry();

        using (var geo = geometry.Open())
        {
            geo.SetFillRule(FillRule.EvenOdd);
            foreach (var loop in loops)
            {
                if (loop.Count < 3)
                {
                    continue;
                }

                geo.BeginFigure(ToPoint(loop[0]), isFilled: true);
                for (var i = 1; i < loop.Count; i++)
                {
                    geo.LineTo(ToPoint(loop[i]));
                }
                geo.EndFigure(isClosed: true);
            }
        }

        return geometry;
    }

    private static IBrush? CreateHatchBrush(RenderHatchFill fill)
    {
        if (fill.Gradient is null)
        {
            return new SolidColorBrush(ToAvaloniaColor(fill.Color));
        }

        return CreateLinearGradient(fill);
    }

    private static IBrush CreateLinearGradient(RenderHatchFill fill)
    {
        var bounds = fill.Bounds;
        var center = new Vector2((bounds.Min.X + bounds.Max.X) * 0.5f, (bounds.Min.Y + bounds.Max.Y) * 0.5f);
        var direction = new Vector2(MathF.Cos(fill.Gradient!.Angle), MathF.Sin(fill.Gradient.Angle));
        var half = MathF.Max(bounds.Size.X, bounds.Size.Y) * 0.5f;
        center += direction * (fill.Gradient.Shift * half);
        var start = center - direction * half;
        var end = center + direction * half;

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(new Point(start.X, start.Y), RelativeUnit.Absolute),
            EndPoint = new RelativePoint(new Point(end.X, end.Y), RelativeUnit.Absolute),
            GradientStops = CreateGradientStops(fill.Gradient)
        };
    }

    private static GradientStops CreateGradientStops(RenderHatchGradient gradient)
    {
        var stops = new GradientStops();
        foreach (var stop in gradient.Stops)
        {
            stops.Add(new GradientStop(ToAvaloniaColor(stop.Color), stop.Offset));
        }

        return stops;
    }

    private static Matrix ToAvaloniaMatrix(Matrix3x2 matrix)
    {
        return new Matrix(
            matrix.M11,
            matrix.M12,
            matrix.M21,
            matrix.M22,
            matrix.M31,
            matrix.M32);
    }

    private Typeface ResolveTypeface(string? fontFamily, bool isItalic, bool isBold)
    {
        var fontStyle = isItalic ? FontStyle.Italic : FontStyle.Normal;
        var fontWeight = isBold ? FontWeight.Bold : FontWeight.Normal;
        var family = string.IsNullOrWhiteSpace(fontFamily) ? string.Empty : fontFamily;
        var key = new TypefaceKey(family, fontStyle, fontWeight);
        if (_typefaceCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Typeface resolved;
        if (family.Length == 0)
        {
            resolved = new Typeface(FontFamily.Default, fontStyle, fontWeight);
        }
        else
        {
            try
            {
                resolved = new Typeface(new FontFamily(family), fontStyle, fontWeight);
            }
            catch (Exception)
            {
                resolved = new Typeface(FontFamily.Default, fontStyle, fontWeight);
            }
        }

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
        public StreamGeometry Geometry { get; }
        public RenderBounds Bounds { get; }

        public RenderLineBatch(LineBatchKey key, StreamGeometry geometry, RenderBounds bounds)
        {
            Key = key;
            Geometry = geometry;
            Bounds = bounds;
        }
    }

    private sealed class RenderPolylineBatch
    {
        public LineBatchKey Key { get; }
        public StreamGeometry Geometry { get; }
        public RenderBounds Bounds { get; }

        public RenderPolylineBatch(LineBatchKey key, StreamGeometry geometry, RenderBounds bounds)
        {
            Key = key;
            Geometry = geometry;
            Bounds = bounds;
        }
    }

    private sealed class RenderArcBatch
    {
        public LineBatchKey Key { get; }
        public StreamGeometry Geometry { get; }
        public RenderBounds Bounds { get; }

        public RenderArcBatch(LineBatchKey key, StreamGeometry geometry, RenderBounds bounds)
        {
            Key = key;
            Geometry = geometry;
            Bounds = bounds;
        }
    }

    private sealed class RenderCircleBatch
    {
        public LineBatchKey Key { get; }
        public StreamGeometry Geometry { get; }
        public RenderBounds Bounds { get; }

        public RenderCircleBatch(LineBatchKey key, StreamGeometry geometry, RenderBounds bounds)
        {
            Key = key;
            Geometry = geometry;
            Bounds = bounds;
        }
    }

    private readonly record struct TypefaceKey(
        string Family,
        FontStyle Style,
        FontWeight Weight);

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
