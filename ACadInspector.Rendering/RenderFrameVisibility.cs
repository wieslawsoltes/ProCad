namespace ACadInspector.Rendering;

public enum RenderFrameVisibility
{
    Hidden = 0,
    DisplayAndPlot = 1,
    DisplayNotPlot = 2
}

public static class RenderFrameVisibilityExtensions
{
    public static bool ShouldDisplay(this RenderFrameVisibility visibility)
    {
        return visibility != RenderFrameVisibility.Hidden;
    }
}
