using System.Numerics;
using ProCad.Rendering;
using Xunit;

namespace ProCad.Tests.Rendering;

public class RenderBoundsTests
{
    [Fact]
    public void Inflate_ExpandsBounds()
    {
        var bounds = new RenderBounds(new Vector2(0f, 0f), new Vector2(1f, 2f));
        var inflated = bounds.Inflate(1f);

        Assert.Equal(-1f, inflated.Min.X, 3);
        Assert.Equal(-1f, inflated.Min.Y, 3);
        Assert.Equal(2f, inflated.Max.X, 3);
        Assert.Equal(3f, inflated.Max.Y, 3);
    }

    [Fact]
    public void Intersects_DetectsOverlap()
    {
        var left = new RenderBounds(new Vector2(0f, 0f), new Vector2(2f, 2f));
        var right = new RenderBounds(new Vector2(1f, 1f), new Vector2(3f, 3f));
        var separated = new RenderBounds(new Vector2(3f, 3f), new Vector2(4f, 4f));

        Assert.True(left.Intersects(right));
        Assert.False(left.Intersects(separated));
    }

    [Fact]
    public void ArcBounds_RespectsSweep()
    {
        var arc = new RenderArc(
            center: Vector2.Zero,
            radius: 10f,
            startAngle: 0f,
            endAngle: MathF.PI * 0.5f,
            color: RenderColor.DefaultForeground,
            thickness: 0f,
            lineCap: RenderLineCap.Round,
            lineJoin: RenderLineJoin.Round);

        Assert.Equal(0f, arc.Bounds.Min.X, 3);
        Assert.Equal(0f, arc.Bounds.Min.Y, 3);
        Assert.Equal(10f, arc.Bounds.Max.X, 3);
        Assert.Equal(10f, arc.Bounds.Max.Y, 3);
    }
}
