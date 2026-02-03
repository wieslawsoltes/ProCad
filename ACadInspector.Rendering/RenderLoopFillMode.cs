namespace ACadInspector.Rendering;

/// <summary>
/// Describes how loop collections are interpreted for fills and hit testing.
/// </summary>
public enum RenderLoopFillMode
{
    /// <summary>
    /// Odd parity (even-odd) fill rule.
    /// </summary>
    EvenOdd = 0,
    /// <summary>
    /// Non-zero winding fill rule.
    /// </summary>
    NonZero = 1,
    /// <summary>
    /// Use only the outermost loops (ignore holes).
    /// </summary>
    Outer = 2,
    /// <summary>
    /// Ignore holes and treat all loops as filled.
    /// </summary>
    Ignore = 3
}
