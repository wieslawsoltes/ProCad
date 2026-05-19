using System.Numerics;
using ProCad.Controls.Skia;
using ProCad.Rendering;
using Microsoft.Maui.Controls;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace ProCad.Controls.Maui;

/// <summary>
/// Displays an ProCad render scene in a .NET MAUI control.
/// </summary>
public class CadViewer : SKCanvasView
{
    private static readonly CadSkiaSceneRenderer Renderer = new();
    private bool _isTrackingPointer;
    private bool _isPanning;
    private SKPoint _lastPointerPosition;
    private double _dragDistance;
    private double _lastRenderWidth;
    private double _lastRenderHeight;

    /// <summary>
    /// Defines the <see cref="Scene"/> property.
    /// </summary>
    public static readonly BindableProperty SceneProperty =
        BindableProperty.Create(
            nameof(Scene),
            typeof(RenderScene),
            typeof(CadViewer),
            null,
            propertyChanged: OnScenePropertyChanged);

    /// <summary>
    /// Defines the <see cref="Zoom"/> property.
    /// </summary>
    public static readonly BindableProperty ZoomProperty =
        BindableProperty.Create(
            nameof(Zoom),
            typeof(double),
            typeof(CadViewer),
            1d,
            propertyChanged: OnRenderPropertyChanged);

    /// <summary>
    /// Defines the <see cref="PanX"/> property.
    /// </summary>
    public static readonly BindableProperty PanXProperty =
        BindableProperty.Create(
            nameof(PanX),
            typeof(double),
            typeof(CadViewer),
            0d,
            propertyChanged: OnRenderPropertyChanged);

    /// <summary>
    /// Defines the <see cref="PanY"/> property.
    /// </summary>
    public static readonly BindableProperty PanYProperty =
        BindableProperty.Create(
            nameof(PanY),
            typeof(double),
            typeof(CadViewer),
            0d,
            propertyChanged: OnRenderPropertyChanged);

    /// <summary>
    /// Defines the <see cref="Padding"/> property.
    /// </summary>
    public static readonly BindableProperty PaddingProperty =
        BindableProperty.Create(
            nameof(Padding),
            typeof(double),
            typeof(CadViewer),
            24d,
            propertyChanged: OnRenderPropertyChanged);

    /// <summary>
    /// Defines the <see cref="ShowGrid"/> property.
    /// </summary>
    public static readonly BindableProperty ShowGridProperty =
        BindableProperty.Create(
            nameof(ShowGrid),
            typeof(bool),
            typeof(CadViewer),
            false,
            propertyChanged: OnRenderPropertyChanged);

    /// <summary>
    /// Defines the <see cref="ShowAxes"/> property.
    /// </summary>
    public static readonly BindableProperty ShowAxesProperty =
        BindableProperty.Create(
            nameof(ShowAxes),
            typeof(bool),
            typeof(CadViewer),
            true,
            propertyChanged: OnRenderPropertyChanged);

    /// <summary>
    /// Defines the <see cref="CanPan"/> property.
    /// </summary>
    public static readonly BindableProperty CanPanProperty =
        BindableProperty.Create(
            nameof(CanPan),
            typeof(bool),
            typeof(CadViewer),
            true);

    /// <summary>
    /// Defines the <see cref="AutoFitOnSceneChanged"/> property.
    /// </summary>
    public static readonly BindableProperty AutoFitOnSceneChangedProperty =
        BindableProperty.Create(
            nameof(AutoFitOnSceneChanged),
            typeof(bool),
            typeof(CadViewer),
            true);

    /// <summary>
    /// Defines the <see cref="MinimumStrokeThickness"/> property.
    /// </summary>
    public static readonly BindableProperty MinimumStrokeThicknessProperty =
        BindableProperty.Create(
            nameof(MinimumStrokeThickness),
            typeof(double),
            typeof(CadViewer),
            1d,
            propertyChanged: OnRenderPropertyChanged);

    /// <summary>
    /// Initializes a new instance of the <see cref="CadViewer"/> class.
    /// </summary>
    public CadViewer()
    {
        EnableTouchEvents = true;
        PaintSurface += OnPaintSurface;
        Touch += OnTouch;
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
    /// Gets or sets a value indicating whether touch dragging pans the viewport.
    /// </summary>
    public bool CanPan
    {
        get => (bool)GetValue(CanPanProperty);
        set => SetValue(CanPanProperty, value);
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
        InvalidateSurface();
    }

    /// <summary>
    /// Converts a control point into world coordinates.
    /// </summary>
    public Vector2 ScreenToWorld(SKPoint point)
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
        var width = _lastRenderWidth > 0d ? _lastRenderWidth : Width;
        var height = _lastRenderHeight > 0d ? _lastRenderHeight : Height;
        var bounds = Scene?.Bounds ?? RenderBounds.Empty;
        var size = new CadSize(width, height);
        var state = new CadViewportState(Zoom, PanX, PanY);
        return CadViewportMath.CreateViewport(size, bounds, state, Padding);
    }

    /// <summary>
    /// Called when the viewer receives a tap without a meaningful drag.
    /// </summary>
    protected virtual void OnViewportTap(SKPoint point)
    {
    }

    private static void OnScenePropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not CadViewer control)
        {
            return;
        }

        if (control.AutoFitOnSceneChanged)
        {
            control.FitToView();
        }
        else
        {
            control.InvalidateSurface();
        }
    }

    private static void OnRenderPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CadViewer control)
        {
            control.InvalidateSurface();
        }
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        _lastRenderWidth = e.Info.Width;
        _lastRenderHeight = e.Info.Height;
        Renderer.Render(e.Surface.Canvas, Scene, CreateViewport(), CreateRenderOptions());
    }

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                _isTrackingPointer = true;
                _isPanning = CanPan;
                _dragDistance = 0d;
                _lastPointerPosition = e.Location;
                e.Handled = true;
                break;

            case SKTouchAction.Moved:
                if (!_isTrackingPointer)
                {
                    return;
                }

                var deltaX = e.Location.X - _lastPointerPosition.X;
                var deltaY = e.Location.Y - _lastPointerPosition.Y;
                _lastPointerPosition = e.Location;
                _dragDistance += Math.Abs(deltaX) + Math.Abs(deltaY);
                if (_isPanning)
                {
                    var state = CadViewportMath.Pan(new CadViewportState(Zoom, PanX, PanY), deltaX, deltaY);
                    PanX = state.PanX;
                    PanY = state.PanY;
                }

                e.Handled = true;
                break;

            case SKTouchAction.Released:
                if (!_isTrackingPointer)
                {
                    return;
                }

                _isTrackingPointer = false;
                _isPanning = false;
                if (_dragDistance <= 3d)
                {
                    OnViewportTap(e.Location);
                }

                e.Handled = true;
                break;

            case SKTouchAction.Cancelled:
                _isTrackingPointer = false;
                _isPanning = false;
                break;
        }
    }
}
