using Avalonia;
using Avalonia.Headless.XUnit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderSnapshotTests
{
    [AvaloniaFact]
    public void RenderScene_MatchesBaseline()
    {
        var scene = RenderSceneSamples.CreateBaselineScene();
        using var bitmap = RenderSnapshotHarness.Capture(scene, new PixelSize(640, 480));
        var baselinePath = RenderSnapshotHarness.GetBaselinePath("baseline_scene.png");
        RenderSnapshotHarness.AssertMatches(bitmap, baselinePath);
    }
}
