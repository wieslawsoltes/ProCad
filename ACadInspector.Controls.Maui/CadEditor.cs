using ACadInspector.Rendering;
using Microsoft.Maui.Controls;
using SkiaSharp;

namespace ACadInspector.Controls.Maui;

/// <summary>
/// Provides selectable CAD scene editing affordances for .NET MAUI.
/// </summary>
public class CadEditor : CadViewer
{
    private readonly RenderHitTestEngine _hitTestEngine = new();
    private readonly List<RenderHitTestResult> _hits = new();

    /// <summary>
    /// Defines the <see cref="InteractionMode"/> property.
    /// </summary>
    public static readonly BindableProperty InteractionModeProperty =
        BindableProperty.Create(
            nameof(InteractionMode),
            typeof(CadEditorInteractionMode),
            typeof(CadEditor),
            CadEditorInteractionMode.Select);

    /// <summary>
    /// Defines the <see cref="SelectionTolerance"/> property.
    /// </summary>
    public static readonly BindableProperty SelectionToleranceProperty =
        BindableProperty.Create(
            nameof(SelectionTolerance),
            typeof(double),
            typeof(CadEditor),
            6d);

    /// <summary>
    /// Defines the <see cref="SelectedPrimitive"/> property.
    /// </summary>
    public static readonly BindableProperty SelectedPrimitiveProperty =
        BindableProperty.Create(
            nameof(SelectedPrimitive),
            typeof(IRenderPrimitive),
            typeof(CadEditor),
            null,
            propertyChanged: OnSelectedPrimitiveChanged);

    /// <summary>
    /// Raised when primitive selection changes.
    /// </summary>
    public event EventHandler<CadSelectionChangedEventArgs>? SelectionChanged;

    /// <summary>
    /// Gets or sets the editor interaction mode.
    /// </summary>
    public CadEditorInteractionMode InteractionMode
    {
        get => (CadEditorInteractionMode)GetValue(InteractionModeProperty);
        set => SetValue(InteractionModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the selection tolerance in screen pixels.
    /// </summary>
    public double SelectionTolerance
    {
        get => (double)GetValue(SelectionToleranceProperty);
        set => SetValue(SelectionToleranceProperty, Math.Max(0d, value));
    }

    /// <summary>
    /// Gets or sets the currently selected primitive.
    /// </summary>
    public IRenderPrimitive? SelectedPrimitive
    {
        get => (IRenderPrimitive?)GetValue(SelectedPrimitiveProperty);
        set => SetValue(SelectedPrimitiveProperty, value);
    }

    /// <summary>
    /// Gets the last selected hit result.
    /// </summary>
    public RenderHitTestResult? SelectedHit { get; private set; }

    /// <inheritdoc />
    protected override CadRenderOptions CreateRenderOptions()
    {
        var options = base.CreateRenderOptions();
        options.SelectedPrimitive = SelectedPrimitive;
        return options;
    }

    /// <inheritdoc />
    protected override void OnViewportTap(SKPoint point)
    {
        base.OnViewportTap(point);

        if (InteractionMode != CadEditorInteractionMode.Select)
        {
            return;
        }

        var scene = Scene;
        if (scene is null)
        {
            SetSelection(null);
            return;
        }

        var viewport = CreateViewport();
        var world = viewport.ScreenToWorld(new CadPoint(point.X, point.Y));
        var tolerance = viewport.ScreenToWorldDistance(SelectionTolerance);
        _hitTestEngine.HitTestPoint(
            scene,
            scene.SpatialIndex,
            world,
            tolerance,
            _hits,
            new RenderHitTestOptions(includeHiddenLayers: false, maxResults: 1, sortByDistance: true));

        SetSelection(_hits.Count == 0 ? null : _hits[0]);
    }

    private static void OnSelectedPrimitiveChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CadEditor control)
        {
            control.InvalidateSurface();
        }
    }

    private void SetSelection(RenderHitTestResult? hit)
    {
        SelectedHit = hit;
        SelectedPrimitive = hit?.Primitive;
        SelectionChanged?.Invoke(this, new CadSelectionChangedEventArgs(hit));
    }
}
