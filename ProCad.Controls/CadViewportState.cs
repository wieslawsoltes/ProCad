namespace ProCad.Controls;

/// <summary>
/// Describes zoom and pan values shared by the platform controls.
/// </summary>
public readonly struct CadViewportState
{
    /// <summary>
    /// Gets the relative zoom factor.
    /// </summary>
    public double Zoom { get; }

    /// <summary>
    /// Gets the horizontal screen-space pan offset.
    /// </summary>
    public double PanX { get; }

    /// <summary>
    /// Gets the vertical screen-space pan offset.
    /// </summary>
    public double PanY { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CadViewportState"/> struct.
    /// </summary>
    public CadViewportState(double zoom, double panX, double panY)
    {
        Zoom = CadViewportMath.NormalizeZoom(zoom);
        PanX = CadViewportMath.NormalizeOffset(panX);
        PanY = CadViewportMath.NormalizeOffset(panY);
    }

    /// <summary>
    /// Gets the default fitted viewport state.
    /// </summary>
    public static CadViewportState Fit => new(1d, 0d, 0d);
}
