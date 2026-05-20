using System.Numerics;

namespace ProCad.Editing.Interaction;

[Flags]
public enum CadInteractionModifiers
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2
}

[Flags]
public enum CadInteractionPointerButtons
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Middle = 1 << 2
}

public enum CadInteractionEventKind
{
    PointerDown,
    PointerMove,
    PointerUp,
    PointerWheel,
    KeyDown,
    KeyUp,
    TextInput,
    CommandInput
}

public readonly record struct CadInteractionViewport(
    Vector2 Center,
    float Width,
    float Height,
    float Zoom);

public readonly record struct CadInteractionEvent(
    CadInteractionEventKind Kind,
    Vector2 WorldPoint,
    Vector2 ScreenPoint,
    CadInteractionModifiers Modifiers,
    CadInteractionPointerButtons PointerButtons,
    float Tolerance,
    float WheelDelta,
    string? Key,
    string? Text,
    CadInteractionViewport? Viewport = null);
