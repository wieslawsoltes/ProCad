namespace ACadInspector.Rendering;

public readonly struct RenderTextLayout
{
    /// <summary>
    /// Gets the raw text used for layout.
    /// </summary>
    public string Text { get; }
    /// <summary>
    /// Gets the estimated layout width before width-factor scaling.
    /// </summary>
    public float Width { get; }
    /// <summary>
    /// Gets the estimated layout height.
    /// </summary>
    public float Height { get; }

    /// <summary>
    /// Creates a new layout description.
    /// </summary>
    public RenderTextLayout(string text, float width, float height)
    {
        Text = text;
        Width = width;
        Height = height;
    }
}
