using ProCad.Rendering;
using CSMath;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderNurbsEvaluatorTests
{
    [Fact]
    public void EvaluateCurve_LinearSegmentsMatchExpected()
    {
        var controlPoints = new[]
        {
            new XYZ(0, 0, 0),
            new XYZ(10, 0, 0),
            new XYZ(10, 10, 0)
        };
        var knots = new[] { 0.0, 0.0, 0.5, 1.0, 1.0 };
        var weights = new double[0];

        var quarter = RenderNurbsEvaluator.EvaluateCurve(1, knots, controlPoints, weights, 0.25);
        var threeQuarter = RenderNurbsEvaluator.EvaluateCurve(1, knots, controlPoints, weights, 0.75);

        AssertClose(new XYZ(5, 0, 0), quarter);
        AssertClose(new XYZ(10, 5, 0), threeQuarter);
    }

    [Fact]
    public void EvaluateSurface_BilinearPatchMatchesExpected()
    {
        var controlPoints = new[]
        {
            new XYZ(0, 0, 0),
            new XYZ(2, 0, 0),
            new XYZ(0, 2, 0),
            new XYZ(2, 2, 0)
        };
        var knotsU = new[] { 0.0, 0.0, 1.0, 1.0 };
        var knotsV = new[] { 0.0, 0.0, 1.0, 1.0 };
        var weights = new double[0];

        var point = RenderNurbsEvaluator.EvaluateSurface(
            1,
            1,
            knotsU,
            knotsV,
            controlPoints,
            weights,
            2,
            2,
            0.5,
            0.5);

        AssertClose(new XYZ(1, 1, 0), point);
    }

    private static void AssertClose(XYZ expected, XYZ actual, double tolerance = 1e-6)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
        Assert.InRange(actual.Z, expected.Z - tolerance, expected.Z + tolerance);
    }
}
