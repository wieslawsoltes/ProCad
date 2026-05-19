using ProCad.Rendering;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderQualityTests
{
    [Theory]
    [InlineData(RenderQuality.Draft, 96, 24)]
    [InlineData(RenderQuality.Medium, 96, 48)]
    [InlineData(RenderQuality.High, 96, 96)]
    public void ResolveCirclePrecision_UsesQualityScale(RenderQuality quality, int basePrecision, int expected)
    {
        var settings = new CadRenderSceneSettings
        {
            Quality = quality,
            CirclePrecision = basePrecision
        };

        Assert.Equal(expected, settings.ResolveCirclePrecision());
    }

    [Fact]
    public void ResolvePrecision_EnforcesMinimum()
    {
        var settings = new CadRenderSceneSettings
        {
            Quality = RenderQuality.Draft,
            CirclePrecision = 12
        };

        Assert.Equal(8, settings.ResolveCirclePrecision());
    }
}
