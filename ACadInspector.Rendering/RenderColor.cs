namespace ACadInspector.Rendering;

public readonly struct RenderColor
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }
    public byte A { get; }

    public RenderColor(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public static RenderColor FromRgb(byte r, byte g, byte b) => new(r, g, b, 255);

    public static RenderColor DefaultForeground => new(230, 230, 230, 255);
    public static RenderColor DefaultBackground => new(24, 26, 31, 255);
}
