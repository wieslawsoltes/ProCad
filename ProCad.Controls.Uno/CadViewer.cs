using System.Numerics;
using ProCad.Controls.Skia;
using ProCad.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace ProCad.Controls.Uno;

/// <summary>
/// Displays an ProCad render scene in an Uno Platform control.
/// </summary>
public class CadViewer : SKCanvasElement
{
    private static readonly CadSkiaSceneRenderer Renderer = new();
    private bool _isTrackingPointer;
    private bool _isPanning;
    private Point _lastPointerPosition;
    private double _dragDistance;

    /// <summary>
    /// Defines the <see cref="Scene"/> property.
    /// </summary>
    public static readonly DependencyProperty SceneProperty =
        DependencyProperty.Register(
            nameof(Scene),
            typeof(RenderScene),
            typeof(CadViewer),
            new PropertyMetadata(null, OnScenePropertyChanged));

    /// <summary>
    /// Defines the <see cref="Zoom"/> property.
    /// </summary>
    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(
            nameof(Zoom),
            typeof(double),
            typeof(CadViewer),
            new PropertyMetadata(1d, OnRenderPropertyChanged));

    /// <summary>
    /// Defines the <see cref="PanX"/> property.
    /// </summary>
    public static readonly DependencyProperty PanXProperty =
        DependencyProperty.Register(
            nameof(PanX),
            typeof(double),
            typeof(CadViewer),
            new PropertyMetadata(0d, OnRenderPropertyChanged));

    /// <summary>
    /// Defines the <see cref="PanY"/> property.
    /// </summary>
    public static readonly DependencyProperty PanYProperty =
        DependencyProperty.Register(
            nameof(PanY),
            typeof(double),
            typeof(CadViewer),
            new PropertyMetadata(0d, OnRenderPropertyChanged));

    /// <summary>
    /// Defines the <see cref="Padding"/> property.
    /// </summary>
    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(
            nameof(Padding),
            typeof(double),
            typeof(CadViewer),
            new PropertyMetadata(24d, OnRenderPropertyChanged));

    /// <summary>
    /// Defines the <see cref="ShowGrid"/> property.
    /// </summary>
    public static readonly DependencyProperty ShowGridProperty =
        DependencyProperty.Register(
            nameof(ShowGrid),
            typeof(bool),
            typeof(CadViewer),
            new PropertyMetadata(false, OnRenderPropertyChanged));

    /// <summary>
    /// Defines the <see cref="ShowAxes"/> property.
    /// </summary>
    public static readonly DependencyProperty ShowAxesProperty =
        DependencyProperty.Register(
            nameof(ShowAxes),
            typeof(bool),
            typeof(CadViewer),
            new PropertyMetadata(true, OnRenderPropertyChanged));

    /// <summary>
    /// Defines the <see cref="CanPan"/> property.
    /// </summary>
    public static readonly DependencyProperty CanPanProperty =
        DependencyProperty.Register(
            nameof(CanPan),
            typeof(bool),
            typeof(CadViewer),
            new PropertyMetadata(true));

    /// <summary>
    /// Defines the <see cref="CanZoom"/> property.
    /// </summary>
    public static readonly DependencyProperty CanZoomProperty =
        DependencyProperty.Register(
            nameof(CanZoom),
            typeof(bool),
            typeof(CadViewer),
            new PropertyMetadata(true));

    /// <summary>
    /// Defines the <see cref="AutoFitOnSceneChanged"/> property.
    /// </summary>
    public static readonly DependencyProperty AutoFitOnSceneChangedProperty =
        DependencyProperty.Register(
            nameof(AutoFitOnSceneChanged),
            typeof(bool),
            typeof(CadViewer),
            new PropertyMetadata(true));

    /// <summary>
    /// Defines the <see cref="MinimumStrokeThickness"/> property.
    /// </summary>
    public static readonly DependencyProperty MinimumStrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(MinimumStrokeThickness),
            typeof(double),
            typeof(CadViewer),
            new PropertyMetadata(1d, OnRenderPropertyChanged));

    /// <summary>
    /// Initializes a new instance of the <see cref="CadViewer"/> class.
    /// </summary>
    public CadViewer()
    {
        PointerPressed += OnControlPointerPressed;
        PointerMoved += OnControlPointerMoved;
        PointerReleased += OnControlPointerReleased;
        PointerCanceled += OnControlPointerCanceled;
        PointerWheelChanged += OnControlPointerWheelChanged;
    }

    /// <summary>
    /// Gets or sets the render scene.
    /// </summary>
    public RenderScene? Scene
    {
        get => (RenderScene?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>
    /// Gets or sets the relative zoom factor.
    /// </summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, CadViewportMath.NormalizeZoom(value));
    }

    /// <summary>
    /// Gets or sets the horizontal pan offset in screen units.
    /// </summary>
    public double PanX
    {
        get => (double)GetValue(PanXProperty);
        set => SetValue(PanXProperty, CadViewportMath.NormalizeOffset(value));
    }

    /// <summary>
    /// Gets or sets the vertical pan offset in screen units.
    /// </summary>
    public double PanY
    {
        get => (double)GetValue(PanYProperty);
        set => SetValue(PanYProperty, CadViewportMath.NormalizeOffset(value));
    }

    /// <summary>
    /// Gets or sets the fitted scene padding.
    /// </summary>
    public double Padding
    {
        get => (double)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, Math.Max(0d, value));
    }

    /// <summary>
    /// Gets or sets a value indicating whether the world grid is visible.
    /// </summary>
    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the world axes are visible.
    /// </summary>
    public bool ShowAxes
    {
        get => (bool)GetValue(ShowAxesProperty);
        set => SetValue(ShowAxesProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether pointer dragging pans the viewport.
    /// </summary>
    public bool CanPan
    {
        get => (bool)GetValue(CanPanProperty);
        set => SetValue(CanPanProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether wheel input zooms the viewport.
    /// </summary>
    public bool CanZoom
    {
        get => (bool)GetValue(CanZoomProperty);
        set => SetValue(CanZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the viewport resets when the scene changes.
    /// </summary>
    public bool AutoFitOnSceneChanged
    {
        get => (bool)GetValue(AutoFitOnSceneChangedProperty);
        set => SetValue(AutoFitOnSceneChangedProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum visible stroke thickness in screen pixels.
    /// </summary>
    public double MinimumStrokeThickness
    {
        get => (double)GetValue(MinimumStrokeThicknessProperty);
        set => SetValue(MinimumStrokeThicknessProperty, Math.Max(0d, value));
    }

    /// <summary>
    /// Resets the viewport to the fitted scene.
    /// </summary>
    public void FitToView()
    {
        Zoom = 1d;
        PanX = 0d;
        PanY = 0d;
        Invalidate();
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
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var viewport = CreateViewport(area.Width, area.Height);
        Renderer.Render(canvas, Scene, viewport, CreateRenderOptions());
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
        return CreateViewport(ActualWidth, ActualHeight);
    }

    /// <summary>
    /// Called when the viewer receives a click without a meaningful drag.
    /// </summary>
    protected virtual void OnViewportClick(Point point)
    {
    }

    private static void OnScenePropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not CadViewer control)
        {
            return;
        }

        if (control.AutoFitOnSceneChanged)
        {
            control.FitToView();
        }
        else
        {
            control.Invalidate();
        }
    }

    private static void OnRenderPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is CadViewer control)
        {
            control.Invalidate();
        }
    }

    private CadSceneViewport CreateViewport(double width, double height)
    {
        var bounds = Scene?.Bounds ?? RenderBounds.Empty;
        var size = new CadSize(width, height);
        var state = new CadViewportState(Zoom, PanX, PanY);
        return CadViewportMath.CreateViewport(size, bounds, state, Padding);
    }

    private void OnControlPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsMiddleButtonPressed)
        {
            return;
        }

        _isTrackingPointer = true;
        _isPanning = CanPan;
        _dragDistance = 0d;
        _lastPointerPosition = point.Position;
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnControlPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isTrackingPointer)
        {
            return;
        }

        var position = e.GetCurrentPoint(this).Position;
        var deltaX = position.X - _lastPointerPosition.X;
        var deltaY = position.Y - _lastPointerPosition.Y;
        _lastPointerPosition = position;
        _dragDistance += Math.Abs(deltaX) + Math.Abs(deltaY);

        if (_isPanning)
        {
            var state = CadViewportMath.Pan(new CadViewportState(Zoom, PanX, PanY), deltaX, deltaY);
            PanX = state.PanX;
            PanY = state.PanY;
        }

        e.Handled = true;
    }

    private void OnControlPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isTrackingPointer)
        {
            return;
        }

        _isTrackingPointer = false;
        _isPanning = false;
        ReleasePointerCapture(e.Pointer);
        var position = e.GetCurrentPoint(this).Position;
        if (_dragDistance <= 3d)
        {
            OnViewportClick(position);
        }

        e.Handled = true;
    }

    private void OnControlPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_isTrackingPointer)
        {
            _isTrackingPointer = false;
            _isPanning = false;
            ReleasePointerCapture(e.Pointer);
        }
    }

    private void OnControlPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!CanZoom)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        var factor = point.Properties.MouseWheelDelta > 0 ? 1.12d : 1d / 1.12d;
        var state = CadViewportMath.ZoomAt(CreateViewport(), new CadPoint(point.Position.X, point.Position.Y), factor);
        Zoom = state.Zoom;
        PanX = state.PanX;
        PanY = state.PanY;
        e.Handled = true;
    }
}
