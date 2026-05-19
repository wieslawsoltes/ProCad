using System.Numerics;

namespace ProCad.Rendering;

public sealed record CadDynamicInputPayload(
    string Prompt,
    string? Value,
    Vector2? Anchor);
