using ProCad.Rendering;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderGeometrySamplerTests
{
    [Fact]
    public void SampleArc_ScalesSegmentsWithSweep()
    {
        var arcSmall = new Arc
        {
            Center = new XYZ(0, 0, 0),
            Radius = 5.0,
            StartAngle = 0.0,
            EndAngle = MathHelper.HalfPI * 0.5,
            Normal = XYZ.AxisZ
        };

        var arcLarge = new Arc
        {
            Center = new XYZ(0, 0, 0),
            Radius = 5.0,
            StartAngle = 0.0,
            EndAngle = MathHelper.PI,
            Normal = XYZ.AxisZ
        };

        var sampler = new DefaultRenderGeometrySampler();

        var small = sampler.SampleArc(arcSmall, 32);
        var large = sampler.SampleArc(arcLarge, 32);

        Assert.True(small.Count < large.Count);
    }

    [Fact]
    public void SampleSpline_RespectsMaxPrecision()
    {
        var spline = new Spline();
        spline.FitPoints.Add(new XYZ(0, 0, 0));
        spline.FitPoints.Add(new XYZ(5, 10, 0));
        spline.FitPoints.Add(new XYZ(10, 0, 0));
        Assert.True(spline.UpdateFromFitPoints());

        var sampler = new DefaultRenderGeometrySampler();

        var low = sampler.SampleSpline(spline, 8);
        var high = sampler.SampleSpline(spline, 32);

        Assert.True(low.Count <= 8);
        Assert.True(high.Count <= 32);
        Assert.True(high.Count >= low.Count);
    }
}
