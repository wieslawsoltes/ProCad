using ProCad.Rendering;
using Avalonia;
using Avalonia.Controls;

namespace ProCad.Controls.Avalonia;

/// <summary>
/// Provides selectable CAD scene editing affordances for Avalonia.
/// </summary>
public class CadEditorControl : CadViewerControl
{
    private readonly RenderHitTestEngine _hitTestEngine = new();
    private readonly List<RenderHitTestResult> _hits = new();

    /// <summary>
    /// Defines the <see cref="InteractionMode"/> property.
    /// </summary>
    public static readonly StyledProperty<CadEditorInteractionMode> InteractionModeProperty =
        AvaloniaProperty.Register<CadEditorControl, CadEditorInteractionMode>(nameof(InteractionMode), CadEditorInteractionMode.Select);

    /// <summary>
    /// Defines the <see cref="SelectionTolerance"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SelectionToleranceProperty =
        AvaloniaProperty.Register<CadEditorControl, double>(nameof(SelectionTolerance), 6d);

    /// <summary>
    /// Defines the <see cref="SelectedPrimitive"/> property.
    /// </summary>
    public static readonly StyledProperty<IRenderPrimitive?> SelectedPrimitiveProperty =
        AvaloniaProperty.Register<CadEditorControl, IRenderPrimitive?>(nameof(SelectedPrimitive));

    static CadEditorControl()
    {
        AffectsRender<CadEditorControl>(SelectedPrimitiveProperty);
    }

    /// <summary>
    /// Raised when primitive selection changes.
    /// </summary>
    public event EventHandler<CadSelectionChangedEventArgs>? SelectionChanged;

    /// <summary>
    /// Gets or sets the editor interaction mode.
    /// </summary>
    public CadEditorInteractionMode InteractionMode
    {
        get => GetValue(InteractionModeProperty);
        set => SetValue(InteractionModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the selection tolerance in screen pixels.
    /// </summary>
    public double SelectionTolerance
    {
        get => GetValue(SelectionToleranceProperty);
        set => SetValue(SelectionToleranceProperty, Math.Max(0d, value));
    }

    /// <summary>
    /// Gets or sets the currently selected primitive.
    /// </summary>
    public IRenderPrimitive? SelectedPrimitive
    {
        get => GetValue(SelectedPrimitiveProperty);
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
    protected override void OnViewportClick(Point point)
    {
        base.OnViewportClick(point);

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

    private void SetSelection(RenderHitTestResult? hit)
    {
        SelectedHit = hit;
        SetCurrentValue(SelectedPrimitiveProperty, hit?.Primitive);
        SelectionChanged?.Invoke(this, new CadSelectionChangedEventArgs(hit));
    }
}
