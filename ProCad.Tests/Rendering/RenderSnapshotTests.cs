using Avalonia;
using Avalonia.Headless.XUnit;
using ReactiveUI;
using ReactiveUI.Avalonia;
using System;
using System.Reactive.Concurrency;

namespace ProCad.Tests.Rendering;

public sealed class RenderSnapshotTests : IDisposable
{
    public RenderSnapshotTests()
    {
        RxSchedulers.MainThreadScheduler = AvaloniaScheduler.Instance;
    }

    public void Dispose()
    {
        RxSchedulers.MainThreadScheduler = CurrentThreadScheduler.Instance;
    }

    [AvaloniaFact]
    public void RenderScene_MatchesBaseline()
    {
        var scene = RenderSceneSamples.CreateBaselineScene();
        using var bitmap = RenderSnapshotHarness.Capture(scene, new PixelSize(640, 480));
        var baselinePath = RenderSnapshotHarness.GetBaselinePath("baseline_scene.png");
        RenderSnapshotHarness.AssertMatches(bitmap, baselinePath);
    }
}
