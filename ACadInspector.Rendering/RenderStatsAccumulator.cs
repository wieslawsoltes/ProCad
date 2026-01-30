namespace ACadInspector.Rendering;

public sealed class RenderStatsAccumulator
{
    public int EntityCount { get; private set; }
    public int VisibleEntityCount { get; private set; }

    public void TrackEntity(bool isVisible)
    {
        EntityCount++;
        if (isVisible)
        {
            VisibleEntityCount++;
        }
    }
}
