using System.Numerics;

namespace ProCad.Services;

public readonly record struct CadInsertDropRequest(string BlockName, Vector2 WorldPoint);
