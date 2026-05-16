namespace ACadInspector.Controls;

/// <summary>
/// Describes the primary interaction mode for editor controls.
/// </summary>
public enum CadEditorInteractionMode
{
    /// <summary>
    /// The control behaves as a viewer and does not select primitives.
    /// </summary>
    View = 0,

    /// <summary>
    /// The control selects primitives from the render scene.
    /// </summary>
    Select = 1
}
