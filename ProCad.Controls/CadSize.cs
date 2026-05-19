namespace ProCad.Controls;

/// <summary>
/// Describes a control-local viewport size.
/// </summary>
public readonly struct CadSize
{
    /// <summary>
    /// Gets the viewport width.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the viewport height.
    /// </summary>
    public double Height { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CadSize"/> struct.
    /// </summary>
    public CadSize(double width, double height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets a value indicating whether the size can be rendered.
    /// </summary>
    public bool IsValid => Width > 0d && Height > 0d && !double.IsNaN(Width) && !double.IsNaN(Height);
}
