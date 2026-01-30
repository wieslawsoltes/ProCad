using System.Collections.Generic;
using System.Globalization;
using ACadInspector.Rendering;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderAcisSatTessellatorTests
{
    [Fact]
    public void TryTessellate_ParsesFaceLoops()
    {
        var sat = string.Join("\n", new[]
        {
            "ACIS 7.0",
            "1 0 0",
            "face $2",
            "loop $3 $4 $5 $6",
            "coedge $7",
            "coedge $8",
            "coedge $9",
            "coedge $10",
            "edge $11 $12",
            "edge $12 $13",
            "edge $13 $14",
            "edge $14 $11",
            "vertex $15",
            "vertex $16",
            "vertex $17",
            "vertex $18",
            "point 0 0 0",
            "point 1 0 0",
            "point 1 1 0",
            "point 0 1 0"
        });

        var success = RenderAcisSatTessellator.TryTessellate(
            sat,
            XYZ.Zero,
            out var triangles);

        Assert.True(success);
        Assert.Equal(2, triangles.Count);
    }

    [Fact]
    public void TryTessellate_TessellatesCylinderSurface()
    {
        var sat = BuildSurfaceSat(
            "cylinder-surface 0 0 0 0 0 1 5 0 0 1 10",
            MathHelper.TwoPI,
            10.0);

        var success = RenderAcisSatTessellator.TryTessellate(
            sat,
            XYZ.Zero,
            out var triangles);

        Assert.True(success);
        Assert.NotEmpty(triangles);
    }

    [Fact]
    public void TryTessellate_TessellatesConeSurface()
    {
        var sin = Math.Sin(Math.PI / 4.0);
        var cos = Math.Cos(Math.PI / 4.0);
        var surfaceLine = string.Format(
            CultureInfo.InvariantCulture,
            "cone-surface 0 0 0 0 0 1 5 0 0 1 {0} {1} 5",
            sin,
            cos);

        var sat = BuildSurfaceSat(surfaceLine, MathHelper.TwoPI, 5.0);

        var success = RenderAcisSatTessellator.TryTessellate(
            sat,
            XYZ.Zero,
            out var triangles);

        Assert.True(success);
        Assert.NotEmpty(triangles);
    }

    [Fact]
    public void TryTessellate_TessellatesTorusSurface()
    {
        var sat = BuildSurfaceSat(
            "torus-surface 0 0 0 0 0 1 1 0 0 10 2",
            MathHelper.TwoPI,
            MathHelper.TwoPI);

        var success = RenderAcisSatTessellator.TryTessellate(
            sat,
            XYZ.Zero,
            out var triangles);

        Assert.True(success);
        Assert.NotEmpty(triangles);
    }

    private static string BuildSurfaceSat(string surfaceLine, double uMax, double vMax)
    {
        var uText = uMax.ToString("G17", CultureInfo.InvariantCulture);
        var vText = vMax.ToString("G17", CultureInfo.InvariantCulture);
        var lines = new List<string>
        {
            "ACIS 7.0",
            "1 0 0",
            "face $2 $27",
            "loop $3 $4 $5 $6",
            "coedge $7 $19",
            "coedge $8 $20",
            "coedge $9 $21",
            "coedge $10 $22",
            "edge $11 $12",
            "edge $12 $13",
            "edge $13 $14",
            "edge $14 $11",
            "vertex $15",
            "vertex $16",
            "vertex $17",
            "vertex $18",
            "point 5 0 0",
            "point 0 5 0",
            "point 0 5 10",
            "point 5 0 10",
            "pcurve $23 $24",
            "pcurve $24 $25",
            "pcurve $25 $26",
            "pcurve $26 $23",
            "point 0 0 0",
            $"point {uText} 0 0",
            $"point {uText} {vText} 0",
            $"point 0 {vText} 0",
            surfaceLine
        };

        return string.Join("\n", lines);
    }
}
