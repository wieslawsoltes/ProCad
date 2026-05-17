using System.Numerics;
using ACadInspector.Controls.Skia;
using ACadInspector.Rendering;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;

namespace ACadInspector.Controls.Avalonia;

/// <summary>
/// Displays an ACadInspector render scene in an Avalonia control.
/// </summary>
public class CadViewerControl : Control
{
    private static readonly CadSkiaSceneRenderer Renderer = new();
    private bool _isTrackingPointer;
    private bool _isPanning;
    private Point _lastPointerPosition;
    private double _dragDistance;

    /// <summary>
    /// Defines the <see cref="Scene"/> property.
    /// </summary>
    public static readonly StyledProperty<RenderScene?> SceneProperty =
        AvaloniaProperty.Register<CadViewerControl, RenderScene?>(nameof(Scene));

    /// <summary>
    /// Defines the <see cref="Zoom"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<CadViewerControl, double>(nameof(Zoom), 1d);

    /// <summary>
    /// Defines the <see cref="PanX"/> property.
    /// </summary>
    public static readonly StyledProperty<double> PanXProperty =
        AvaloniaProperty.Register<CadViewerControl, double>(nameof(PanX));

    /// <summary>
    /// Defines the <see cref="PanY"/> property.
    /// </summary>
    public static readonly StyledProperty<double> PanYProperty =
        AvaloniaProperty.Register<CadViewerControl, double>(nameof(PanY));

    /// <summary>
    /// Defines the <see cref="Padding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> PaddingProperty =
        AvaloniaProperty.Register<CadViewerControl, double>(nameof(Padding), 24d);

    /// <summary>
    /// Defines the <see cref="ShowGrid"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<CadViewerControl, bool>(nameof(ShowGrid));

    /// <summary>
    /// Defines the <see cref="ShowAxes"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> ShowAxesProperty =
        AvaloniaProperty.Register<CadViewerControl, bool>(nameof(ShowAxes), true);

    /// <summary>
    /// Defines the <see cref="CanPan"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> CanPanProperty =
        AvaloniaProperty.Register<CadViewerControl, bool>(nameof(CanPan), true);

    /// <summary>
    /// Defines the <see cref="CanZoom"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> CanZoomProperty =
        AvaloniaProperty.Register<CadViewerControl, bool>(nameof(CanZoom), true);

    /// <summary>
    /// Defines the <see cref="AutoFitOnSceneChanged"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> AutoFitOnSceneChangedProperty =
        AvaloniaProperty.Register<CadViewerControl, bool>(nameof(AutoFitOnSceneChanged), true);

    /// <summary>
    /// Defines the <see cref="MinimumStrokeThickness"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MinimumStrokeThicknessProperty =
        AvaloniaProperty.Register<CadViewerControl, double>(nameof(MinimumStrokeThickness), 1d);

    static CadViewerControl()
    {
        AffectsRender<CadViewerControl>(
            SceneProperty,
            ZoomProperty,
            PanXProperty,
            PanYProperty,
            PaddingProperty,
            ShowGridProperty,
            ShowAxesProperty,
            MinimumStrokeThicknessProperty);
    }

    /// <summary>
    /// Gets or sets the render scene.
    /// </summary>
    public RenderScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>
    /// Gets or sets the relative zoom factor.
    /// </summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, CadViewportMath.NormalizeZoom(value));
    }

    /// <summary>
    /// Gets or sets the horizontal pan offset in screen units.
    /// </summary>
    public double PanX
    {
        get => GetValue(PanXProperty);
        set => SetValue(PanXProperty, CadViewportMath.NormalizeOffset(value));
    }

    /// <summary>
    /// Gets or sets the vertical pan offset in screen units.
    /// </summary>
    public double PanY
    {
        get => GetValue(PanYProperty);
        set => SetValue(PanYProperty, CadViewportMath.NormalizeOffset(value));
    }

    /// <summary>
    /// Gets or sets the fitted scene padding.
    /// </summary>
    public double Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, Math.Max(0d, value));
    }

    /// <summary>
    /// Gets or sets a value indicating whether the world grid is visible.
    /// </summary>
    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the world axes are visible.
    /// </summary>
    public bool ShowAxes
    {
        get => GetValue(ShowAxesProperty);
        set => SetValue(ShowAxesProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether pointer dragging pans the viewport.
    /// </summary>
    public bool CanPan
    {
        get => GetValue(CanPanProperty);
        set => SetValue(CanPanProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether wheel input zooms the viewport.
    /// </summary>
    public bool CanZoom
    {
        get => GetValue(CanZoomProperty);
        set => SetValue(CanZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the viewport resets when the scene changes.
    /// </summary>
    public bool AutoFitOnSceneChanged
    {
        get => GetValue(AutoFitOnSceneChangedProperty);
        set => SetValue(AutoFitOnSceneChangedProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum visible stroke thickness in screen pixels.
    /// </summary>
    public double MinimumStrokeThickness
    {
        get => GetValue(MinimumStrokeThicknessProperty);
        set => SetValue(MinimumStrokeThicknessProperty, Math.Max(0d, value));
    }

    /// <summary>
    /// Resets the viewport to the fitted scene.
    /// </summary>
    public void FitToView()
    {
        SetCurrentValue(ZoomProperty, 1d);
        SetCurrentValue(PanXProperty, 0d);
        SetCurrentValue(PanYProperty, 0d);
        InvalidateVisual();
    }

    /// <summary>
    /// Converts a control point into world coordinates.
    /// </summary>
    public Vector2 ScreenToWorld(Point point)
    {
        return CreateViewport().ScreenToWorld(new CadPoint(point.X, point.Y));
    }

    /// <summary>
    /// Converts a screen-space distance into world-space units.
    /// </summary>
    public float ScreenToWorldDistance(double screenDistance)
    {
        return CreateViewport().ScreenToWorldDistance(screenDistance);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var scene = Scene;
        var viewport = CreateViewport();
        var options = CreateRenderOptions();
        context.Custom(new CadSceneDrawOperation(new Rect(Bounds.Size), scene, viewport, options));
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SceneProperty && AutoFitOnSceneChanged)
        {
            FitToView();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsMiddleButtonPressed)
        {
            return;
        }

        _isTrackingPointer = true;
        _isPanning = CanPan;
        _dragDistance = 0d;
        _lastPointerPosition = point.Position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isTrackingPointer)
        {
            return;
        }

        var position = e.GetPosition(this);
        var delta = position - _lastPointerPosition;
        _lastPointerPosition = position;
        _dragDistance += Math.Abs(delta.X) + Math.Abs(delta.Y);

        if (_isPanning)
        {
            var state = CadViewportMath.Pan(new CadViewportState(Zoom, PanX, PanY), delta.X, delta.Y);
            SetCurrentValue(PanXProperty, state.PanX);
            SetCurrentValue(PanYProperty, state.PanY);
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isTrackingPointer)
        {
            _isTrackingPointer = false;
            _isPanning = false;
            e.Pointer.Capture(null);
            if (_dragDistance <= 3d)
            {
                OnViewportClick(e.GetPosition(this));
            }

            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (!CanZoom)
        {
            return;
        }

        var factor = e.Delta.Y > 0d ? 1.12d : 1d / 1.12d;
        var viewport = CreateViewport();
        var point = e.GetPosition(this);
        var state = CadViewportMath.ZoomAt(viewport, new CadPoint(point.X, point.Y), factor);
        SetCurrentValue(ZoomProperty, state.Zoom);
        SetCurrentValue(PanXProperty, state.PanX);
        SetCurrentValue(PanYProperty, state.PanY);
        e.Handled = true;
    }

    /// <summary>
    /// Creates platform-neutral render options for this control.
    /// </summary>
    protected virtual CadRenderOptions CreateRenderOptions()
    {
        return new CadRenderOptions
        {
            ShowGrid = ShowGrid,
            ShowAxes = ShowAxes,
            MinimumStrokeThickness = MinimumStrokeThickness
        };
    }

    /// <summary>
    /// Creates the current scene viewport.
    /// </summary>
    protected CadSceneViewport CreateViewport()
    {
        var scene = Scene;
        var bounds = scene?.Bounds ?? RenderBounds.Empty;
        var size = new CadSize(Bounds.Width, Bounds.Height);
        var state = new CadViewportState(Zoom, PanX, PanY);
        return CadViewportMath.CreateViewport(size, bounds, state, Padding);
    }

    /// <summary>
    /// Called when the viewer receives a click without a meaningful drag.
    /// </summary>
    protected virtual void OnViewportClick(Point point)
    {
    }

    private sealed class CadSceneDrawOperation : ICustomDrawOperation
    {
        private readonly RenderScene? _scene;
        private readonly CadSceneViewport _viewport;
        private readonly CadRenderOptions _options;

        public CadSceneDrawOperation(Rect bounds, RenderScene? scene, CadSceneViewport viewport, CadRenderOptions options)
        {
            Bounds = bounds;
            _scene = scene;
            _viewport = viewport;
            _options = options;
        }

        public Rect Bounds { get; }

        public bool HitTest(Point p) => Bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null)
            {
                return;
            }

            using var lease = leaseFeature.Lease();
            var canvas = lease?.SkCanvas;
            if (canvas is null)
            {
                return;
            }

            Renderer.Render(canvas, _scene, _viewport, _options);
        }
    }
}
