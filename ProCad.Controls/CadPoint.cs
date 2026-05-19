namespace ProCad.Controls;

/// <summary>
/// Describes a point in control-local screen coordinates.
/// </summary>
public readonly struct CadPoint
{
    /// <summary>
    /// Gets the X coordinate.
    /// </summary>
    public double X { get; }

    /// <summary>
    /// Gets the Y coordinate.
    /// </summary>
    public double Y { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CadPoint"/> struct.
    /// </summary>
    public CadPoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}
