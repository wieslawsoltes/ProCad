using System.Numerics;

namespace ACadInspector.ViewModels;

[System.Flags]
public enum CadInputModifiers
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2
}

public enum CadHitTestKind
{
    Hover = 0,
    Select = 1
}

public readonly struct CadRenderHitTestRequest
{
    public Vector2 WorldPoint { get; }
    public float Tolerance { get; }
    public CadHitTestKind Kind { get; }
    public CadInputModifiers Modifiers { get; }

    public CadRenderHitTestRequest(
        Vector2 worldPoint,
        float tolerance,
        CadHitTestKind kind,
        CadInputModifiers modifiers)
    {
        WorldPoint = worldPoint;
        Tolerance = tolerance;
        Kind = kind;
        Modifiers = modifiers;
    }
}
