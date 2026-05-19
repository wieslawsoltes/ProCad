using System.Collections.Generic;
using System.Numerics;
using ProCad.Rendering;
using Xunit;

namespace ProCad.Tests.Rendering;

public class RenderSpatialIndexTests
{
    [Fact]
    public void QueryPoint_FindsCandidates()
    {
        var primitives = new List<IRenderPrimitive>
        {
            new RenderLine(
                new Vector2(-10f, 0f),
                new Vector2(10f, 0f),
                RenderColor.DefaultForeground,
                0.5f,
                RenderLineCap.Round,
                RenderLineJoin.Round),
            new RenderCircle(
                new Vector2(20f, 0f),
                3f,
                RenderColor.DefaultForeground,
                0.2f,
                RenderLineCap.Round,
                RenderLineJoin.Round)
        };

        var bounds = RenderBounds.Empty;
        foreach (var primitive in primitives)
        {
            bounds = bounds.Expand(primitive.Bounds);
        }

        var layer = new RenderLayer("Test", RenderColor.DefaultForeground, isVisible: true, primitives, bounds);
        var index = RenderSpatialIndex.Build(new[] { layer });

        var results = new List<RenderSpatialHit>();
        index.QueryPoint(new Vector2(0f, 0.1f), 0.5f, results);

        Assert.NotEmpty(results);
    }
}
