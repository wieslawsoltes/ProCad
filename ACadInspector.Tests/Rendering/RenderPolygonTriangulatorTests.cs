using System;
using System.Collections.Generic;
using ACadInspector.Rendering;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderPolygonTriangulatorTests
{
    [Fact]
    public void TryTriangulateWithHoles_RemovesHoleArea()
    {
        var outer = new List<XYZ>
        {
            new XYZ(0, 0, 0),
            new XYZ(10, 0, 0),
            new XYZ(10, 10, 0),
            new XYZ(0, 10, 0),
            new XYZ(0, 0, 0)
        };

        var hole = new List<XYZ>
        {
            new XYZ(3, 3, 0),
            new XYZ(7, 3, 0),
            new XYZ(7, 7, 0),
            new XYZ(3, 7, 0),
            new XYZ(3, 3, 0)
        };

        var loops = new List<IReadOnlyList<XYZ>> { outer, hole };
        Assert.True(RenderPolygonTriangulator.TryTriangulateWithHoles(loops, out var triangles));

        var area = ComputeArea(triangles);
        Assert.InRange(area, 83.99, 84.01);
    }

    private static double ComputeArea(IReadOnlyList<MeshTessellator.Triangle> triangles)
    {
        var area = 0.0;
        foreach (var triangle in triangles)
        {
            var ab = triangle.B.Subtract(triangle.A);
            var ac = triangle.C.Subtract(triangle.A);
            var cross = XYZ.Cross(ab, ac);
            area += 0.5 * cross.GetLength();
        }

        return area;
    }
}
