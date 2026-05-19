using ProCad.Rendering;

namespace ProCad.Controls;

/// <summary>
/// Configures shared CAD scene rendering behavior.
/// </summary>
public sealed class CadRenderOptions
{
    /// <summary>
    /// Gets the default render options.
    /// </summary>
    public static CadRenderOptions Default => new();

    /// <summary>
    /// Gets or sets a value indicating whether the scene background should be used.
    /// </summary>
    public bool UseSceneBackground { get; set; } = true;

    /// <summary>
    /// Gets or sets the fallback background color.
    /// </summary>
    public RenderColor Background { get; set; } = RenderColor.DefaultBackground;

    /// <summary>
    /// Gets or sets a value indicating whether a world grid should be rendered.
    /// </summary>
    public bool ShowGrid { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether X/Y axes should be rendered.
    /// </summary>
    public bool ShowAxes { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum visible stroke thickness in screen pixels.
    /// </summary>
    public double MinimumStrokeThickness { get; set; } = 1d;

    /// <summary>
    /// Gets or sets the selected primitive highlight color.
    /// </summary>
    public RenderColor SelectionColor { get; set; } = new(255, 205, 64, 255);

    /// <summary>
    /// Gets or sets the selected primitive, if any.
    /// </summary>
    public IRenderPrimitive? SelectedPrimitive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether hidden layers should be rendered.
    /// </summary>
    public bool IncludeHiddenLayers { get; set; }
}
