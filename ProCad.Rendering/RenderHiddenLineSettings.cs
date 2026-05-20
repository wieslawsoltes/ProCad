namespace ProCad.Rendering;

public enum RenderObscuredLineType : byte
{
    Off = 0,
    Solid = 1,
    Dotted = 2,
    Dashed = 3,
    ShortDash = 4,
    MediumDash = 5,
    LongDash = 6,
    DoubleShortDash = 7,
    DoubleMediumDash = 8,
    DoubleLongDash = 9,
    MediumDashShortDashShortDash = 10,
    LongDashShortDashShortDash = 11
}

public enum RenderHiddenLineColorMode
{
    Entity = 0,
    Layer = 1,
    Fixed = 2
}

public readonly struct RenderHiddenLineSettings
{
    public static RenderHiddenLineSettings Default => new(
        RenderObscuredLineType.Off,
        RenderHiddenLineColorMode.Entity,
        RenderColor.DefaultForeground);

    public RenderObscuredLineType LineType { get; }
    public RenderHiddenLineColorMode ColorMode { get; }
    public RenderColor Color { get; }

    public RenderHiddenLineSettings(
        RenderObscuredLineType lineType,
        RenderHiddenLineColorMode colorMode,
        RenderColor color)
    {
        LineType = lineType;
        ColorMode = colorMode;
        Color = color;
    }

    public bool IsEnabled => LineType != RenderObscuredLineType.Off;
}
