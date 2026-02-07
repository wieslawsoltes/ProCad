using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ACadInspector.Rendering;
using ACadInspector.Services;
using ACadInspector.Rendering.Backends;
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

    public static readonly StyledProperty<IReadOnlyDictionary<string, bool>?> EntityTypeVisibilityOverridesProperty =
        AvaloniaProperty.Register<CadRenderControl, IReadOnlyDictionary<string, bool>?>(nameof(EntityTypeVisibilityOverrides));

    public static readonly StyledProperty<IRenderBackend?> RenderBackendProperty =
        AvaloniaProperty.Register<CadRenderControl, IRenderBackend?>(nameof(RenderBackend));

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

    public static readonly StyledProperty<bool> EnableInteractionOptimizationProperty =
        AvaloniaProperty.Register<CadRenderControl, bool>(nameof(EnableInteractionOptimization), false);

    public static readonly StyledProperty<bool> ShowDebugOverlayProperty =
        AvaloniaProperty.Register<CadRenderControl, bool>(nameof(ShowDebugOverlay), false);

    public static readonly StyledProperty<RenderBounds?> HoverBoundsProperty =
        AvaloniaProperty.Register<CadRenderControl, RenderBounds?>(nameof(HoverBounds));

    public static readonly StyledProperty<RenderBounds?> SelectionBoundsProperty =
        AvaloniaProperty.Register<CadRenderControl, RenderBounds?>(nameof(SelectionBounds));

    public static readonly StyledProperty<RenderAnnotation?> HoverAnnotationProperty =
        AvaloniaProperty.Register<CadRenderControl, RenderAnnotation?>(nameof(HoverAnnotation));

    public static readonly StyledProperty<RenderAnnotation?> SelectionAnnotationProperty =
        AvaloniaProperty.Register<CadRenderControl, RenderAnnotation?>(nameof(SelectionAnnotation));

    public static readonly StyledProperty<RenderOverlayScene?> OverlaySceneProperty =
        AvaloniaProperty.Register<CadRenderControl, RenderOverlayScene?>(nameof(OverlayScene));

    public static readonly StyledProperty<CadDynamicInputPayload?> DynamicInputProperty =
        AvaloniaProperty.Register<CadRenderControl, CadDynamicInputPayload?>(nameof(DynamicInput));

    public static readonly StyledProperty<CadRenderFocusRequest?> FocusRequestProperty =
        AvaloniaProperty.Register<CadRenderControl, CadRenderFocusRequest?>(nameof(FocusRequest));

    public static readonly StyledProperty<IReadOnlyList<RenderBounds>?> DebugBvhBoundsProperty =
        AvaloniaProperty.Register<CadRenderControl, IReadOnlyList<RenderBounds>?>(nameof(DebugBvhBounds));

    private bool _isPanning;
    private Point _panStart;
    private AvaloniaVector _panOrigin;
    private Vector2 _sceneCenter;
    private double _baseScale = 1.0;
    private Matrix3x2 _viewTransform = Matrix3x2.Identity;
    private double _cachedZoom = 1.0;
    private bool _isInteracting;
    private bool _pendingFit;
    private DateTime _interactionUntilUtc;
    private DispatcherTimer? _interactionTimer;
    private static readonly TimeSpan InteractionHold = TimeSpan.FromMilliseconds(150);
    private IRenderBackend _backend;
    private bool _ownsBackend;
    private CadRenderStateSnapshot _renderState = CadRenderStateSnapshot.Empty;

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

    public IReadOnlyDictionary<string, bool>? EntityTypeVisibilityOverrides
    {
        get => GetValue(EntityTypeVisibilityOverridesProperty);
        set => SetValue(EntityTypeVisibilityOverridesProperty, value);
    }

    public IRenderBackend? RenderBackend
    {
        get => GetValue(RenderBackendProperty);
        set => SetValue(RenderBackendProperty, value);
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

    public bool EnableInteractionOptimization
    {
        get => GetValue(EnableInteractionOptimizationProperty);
        set => SetValue(EnableInteractionOptimizationProperty, value);
    }

    public bool ShowDebugOverlay
    {
        get => GetValue(ShowDebugOverlayProperty);
        set => SetValue(ShowDebugOverlayProperty, value);
    }

    public RenderBounds? HoverBounds
    {
        get => GetValue(HoverBoundsProperty);
        set => SetValue(HoverBoundsProperty, value);
    }

    public RenderBounds? SelectionBounds
    {
        get => GetValue(SelectionBoundsProperty);
        set => SetValue(SelectionBoundsProperty, value);
    }

    public RenderAnnotation? HoverAnnotation
    {
        get => GetValue(HoverAnnotationProperty);
        set => SetValue(HoverAnnotationProperty, value);
    }

    public RenderAnnotation? SelectionAnnotation
    {
        get => GetValue(SelectionAnnotationProperty);
        set => SetValue(SelectionAnnotationProperty, value);
    }

    public RenderOverlayScene? OverlayScene
    {
        get => GetValue(OverlaySceneProperty);
        set => SetValue(OverlaySceneProperty, value);
    }

    public CadDynamicInputPayload? DynamicInput
    {
        get => GetValue(DynamicInputProperty);
        set => SetValue(DynamicInputProperty, value);
    }

    public CadRenderFocusRequest? FocusRequest
    {
        get => GetValue(FocusRequestProperty);
        set => SetValue(FocusRequestProperty, value);
    }

    public IReadOnlyList<RenderBounds>? DebugBvhBounds
    {
        get => GetValue(DebugBvhBoundsProperty);
        set => SetValue(DebugBvhBoundsProperty, value);
    }

    public CadRenderControl()
    {
        ClipToBounds = true;
        _backend = CreateBackend(RenderBackend);
        UpdateRenderState();
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var state = Volatile.Read(ref _renderState);
        var isInteractive = _isInteracting && state.EnableInteractionOptimization;
        context.Custom(new BackendRenderOp(_backend, size, state, isInteractive));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        UpdateViewTransform();
        if (_pendingFit && FitOnSceneChange)
        {
            if (TryFitToScene())
            {
                _pendingFit = false;
            }
        }
        return size;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SceneProperty)
        {
            _backend.ClearImageCache();
            _backend.InvalidateHiddenLineCache();
            _backend.InvalidateInteractionCache();
            if (FitOnSceneChange)
            {
                _pendingFit = !TryFitToScene();
            }
            else
            {
                _pendingFit = false;
            }
            UpdateRenderState();
            InvalidateVisual();
            return;
        }

        if (change.Property == EnableInteractionOptimizationProperty)
        {
            _backend.InvalidateInteractionCache();
        }

        if (change.Property == RenderBackendProperty)
        {
            SetBackend(RenderBackend);
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
            change.Property == EntityTypeVisibilityOverridesProperty ||
            change.Property == MinPixelThicknessProperty ||
            change.Property == EnableInteractionOptimizationProperty ||
            change.Property == ShowDebugOverlayProperty ||
            change.Property == HoverBoundsProperty ||
            change.Property == SelectionBoundsProperty ||
            change.Property == HoverAnnotationProperty ||
            change.Property == SelectionAnnotationProperty ||
            change.Property == OverlaySceneProperty ||
            change.Property == DynamicInputProperty ||
            change.Property == FocusRequestProperty ||
            change.Property == DebugBvhBoundsProperty)
        {
            if (change.Property == ZoomProperty || change.Property == PanProperty)
            {
                UpdateViewTransform();
            }
            if (change.Property == MinPixelThicknessProperty)
            {
                _backend.ClearStrokePaintCache();
            }
            if (change.Property == FocusRequestProperty && FocusRequest is not null)
            {
                FocusOnBounds(FocusRequest.Bounds, FocusRequest.Padding);
            }
            UpdateRenderState();
            InvalidateVisual();
        }
    }

    private void FocusOnBounds(RenderBounds bounds, double paddingPixels)
    {
        if (Scene is null || bounds.IsEmpty)
        {
            return;
        }

        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var boundsSize = bounds.Size;
        if (boundsSize.X <= 0f || boundsSize.Y <= 0f)
        {
            bounds = bounds.Inflate(1f);
            boundsSize = bounds.Size;
        }

        var padding = Math.Max(0.0, paddingPixels);
        var availableWidth = Math.Max(1.0, size.Width - 2.0 * padding);
        var availableHeight = Math.Max(1.0, size.Height - 2.0 * padding);

        var desiredScale = Math.Min(availableWidth / boundsSize.X, availableHeight / boundsSize.Y);
        if (double.IsNaN(desiredScale) || double.IsInfinity(desiredScale) || desiredScale <= 0.0)
        {
            return;
        }

        var zoom = desiredScale / _baseScale;
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);

        var center = bounds.Center;
        var pan = ComputePanForZoom(center, new Point(size.Width * 0.5, size.Height * 0.5), zoom);

        SetCurrentValue(ZoomProperty, zoom);
        SetCurrentValue(PanProperty, pan);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _interactionTimer?.Stop();
        _backend.ClearImageCache();
        _backend.InvalidateInteractionCache();
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
        var scaleFactor = Math.Pow(1.2, delta);
        var currentZoom = Zoom;
        var nextZoom = Math.Clamp(currentZoom * scaleFactor, MinZoom, MaxZoom);
        if (Math.Abs(nextZoom - currentZoom) < 0.0001)
        {
            return;
        }

        var pointer = e.GetPosition(this);
        if (!TryScreenToWorld(pointer, out var world))
        {
            return;
        }
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
        if (!TryFitToScene())
        {
            _pendingFit = true;
        }
    }

    private bool TryFitToScene()
    {
        var scene = Scene;
        var size = Bounds.Size;
        if (scene is null || size.Width <= 0 || size.Height <= 0 || scene.Bounds.IsEmpty)
        {
            return false;
        }

        var sceneSize = scene.Bounds.Size;
        if (sceneSize.X <= 0 || sceneSize.Y <= 0)
        {
            _baseScale = 1.0;
            _sceneCenter = Vector2.Zero;
            return false;
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
        return true;
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
            _backend.ClearStrokePaintCache();
        }

        UpdateRenderState();
    }

    private void UpdateRenderState()
    {
        var snapshot = new CadRenderStateSnapshot(
            Scene,
            ShowGrid,
            ShowAxes,
            EnableInteractionOptimization,
            LayerVisibilityOverrides,
            EntityTypeVisibilityOverrides,
            Zoom,
            MinPixelThickness,
            _baseScale,
            _viewTransform,
            ShowDebugOverlay,
            HoverBounds,
            SelectionBounds,
            HoverAnnotation,
            SelectionAnnotation,
            OverlayScene,
            DynamicInput,
            DebugBvhBounds);
        Volatile.Write(ref _renderState, snapshot);
    }

    private IRenderBackend CreateBackend(IRenderBackend? backend)
    {
        if (backend is not null)
        {
            _ownsBackend = false;
            return backend;
        }

        _ownsBackend = true;
        var factory = RenderBackendRegistry.Factory ?? SkiaRenderBackendFactory.Instance;
        return factory.Create();
    }

    private void SetBackend(IRenderBackend? backend)
    {
        if (ReferenceEquals(backend, _backend))
        {
            return;
        }

        if (_ownsBackend)
        {
            _backend.Dispose();
        }

        _backend = CreateBackend(backend);
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

    public bool TryScreenToWorld(Point point, out Vector2 world)
    {
        if (!Matrix3x2.Invert(_viewTransform, out var inverse))
        {
            world = Vector2.Zero;
            return false;
        }

        world = Vector2.Transform(new Vector2((float)point.X, (float)point.Y), inverse);
        return true;
    }

    public float PixelsToWorld(float pixels)
    {
        var scale = (float)(_baseScale * Zoom);
        if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
        {
            return pixels;
        }

        return pixels / scale;
    }

    private sealed class BackendRenderOp : ICustomDrawOperation
    {
        private readonly IRenderBackend _backend;
        private readonly Rect _bounds;
        private readonly Size _size;
        private readonly CadRenderStateSnapshot _state;
        private readonly bool _isInteractive;

        public BackendRenderOp(IRenderBackend backend, Size size, CadRenderStateSnapshot state, bool isInteractive)
        {
            _backend = backend;
            _size = size;
            _bounds = new Rect(size);
            _state = state;
            _isInteractive = isInteractive;
        }

        public Rect Bounds => _bounds;

        public void Render(ImmediateDrawingContext context)
        {
            _backend.Render(context, _size, _state, _isInteractive);
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
}
