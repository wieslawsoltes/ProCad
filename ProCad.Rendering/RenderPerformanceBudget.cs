namespace ProCad.Rendering;

public sealed class RenderPerformanceBudget
{
    public int? MaxEntities { get; init; }
    public int? MaxVisibleEntities { get; init; }
    public int? MaxLayers { get; init; }
    public int? MaxPrimitives { get; init; }
    public double? MaxBuildMilliseconds { get; init; }
}
