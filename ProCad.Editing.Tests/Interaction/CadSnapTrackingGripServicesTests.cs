using System.Numerics;
using ProCad.Editing.Interaction;
using Xunit;

namespace ProCad.Editing.Tests.Interaction;

public sealed class CadSnapTrackingGripServicesTests
{
    [Fact]
    public void SnapService_PrefersExplicitModeOverNearest()
    {
        var service = new CadSnapService();
        var candidates = new[]
        {
            new CadSnapCandidate(new Vector2(5.05f, 5.05f), CadSnapMode.Nearest, "NEAREST"),
            new CadSnapCandidate(new Vector2(5.2f, 5.2f), CadSnapMode.Endpoint, "END")
        };

        var resolved = service.TryResolve(new Vector2(5.1f, 5.1f), tolerance: 1f, candidates, out var result);

        Assert.True(resolved);
        Assert.Equal(new Vector2(5.2f, 5.2f), result.Point);
        Assert.Equal(CadSnapMode.Endpoint, result.Mode);
    }

    [Fact]
    public void TrackingService_OrthoConstrainsToHorizontalOrVertical()
    {
        var service = new CadTrackingService
        {
            OrthoEnabled = true
        };

        var constrained = service.Apply(new Vector2(0f, 0f), new Vector2(8f, 2f));

        Assert.Equal(new Vector2(8f, 0f), constrained);
    }

    [Fact]
    public void TrackingService_PolarSnapsToConfiguredIncrement()
    {
        var service = new CadTrackingService
        {
            PolarEnabled = true,
            PolarIncrementDegrees = 45f
        };

        var constrained = service.Apply(new Vector2(0f, 0f), new Vector2(8f, 2f));
        var expected = new Vector2(8.246211f, 0f);
        Assert.True(Vector2.Distance(expected, constrained) < 0.001f);
    }

    [Fact]
    public void GripService_BuildsDeduplicatedGripSetAndResolvesHotGrip()
    {
        var service = new CadGripService();
        var grips = service.BuildGripSet(new[]
        {
            new CadGripPoint(new Vector2(1f, 2f), "Vertex"),
            new CadGripPoint(new Vector2(1f, 2f), "Vertex"),
            new CadGripPoint(new Vector2(3f, 4f), "Midpoint")
        });

        Assert.Equal(2, grips.Count);
        Assert.Equal("0", grips[0].Tag);
        Assert.Equal("Midpoint", grips[1].Kind);

        var hotResolved = service.TryResolveHotGrip(new Vector2(3.05f, 4f), tolerance: 0.1f, grips, out var hotGrip);
        Assert.True(hotResolved);
        Assert.Equal(new Vector2(3f, 4f), hotGrip.Position);
    }
}
