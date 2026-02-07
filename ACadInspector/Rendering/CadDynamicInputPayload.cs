using System.Numerics;

namespace ACadInspector.Rendering;

public sealed record CadDynamicInputPayload(
    string Prompt,
    string? Value,
    Vector2? Anchor);
