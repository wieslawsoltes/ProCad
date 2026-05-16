using ACadInspector.Rendering;

namespace ACadInspector.Controls;

/// <summary>
/// Provides data for CAD editor selection changes.
/// </summary>
public sealed class CadSelectionChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CadSelectionChangedEventArgs"/> class.
    /// </summary>
    public CadSelectionChangedEventArgs(RenderHitTestResult? hit)
    {
        Hit = hit;
    }

    /// <summary>
    /// Gets the selected hit result, or <c>null</c> when selection was cleared.
    /// </summary>
    public RenderHitTestResult? Hit { get; }
}
