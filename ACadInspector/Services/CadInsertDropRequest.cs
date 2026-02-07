using System.Numerics;

namespace ACadInspector.Services;

public readonly record struct CadInsertDropRequest(string BlockName, Vector2 WorldPoint);
