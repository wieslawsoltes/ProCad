using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ACadInspector.Rendering;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderHiddenLineOcclusionTests
{
    [Fact]
    public void HiddenLineOcclusion_HidesLineBehindTriangle()
    {
        var triangle = new RenderTriangle(
            new Vector2(10, 10),
            new Vector2(90, 10),
            new Vector2(50, 90),
            RenderColor.FromRgb(200, 200, 200),
            shade: 1f,
            depthA: 1f,
            depthB: 1f,
            depthC: 1f);

        var scene = BuildScene(triangle);
        var depthBuffer = new RenderDepthBuffer();
        var hasDepth = RenderHiddenLineUtils.TryBuildDepthBuffer(
            scene,
            Matrix3x2.Identity,
            depthBuffer,
            width: 120,
            height: 120);

        Assert.True(hasDepth);

        var segments = new List<RenderLineSegment>();
        RenderHiddenLineUtils.AppendVisibleSegments(
            depthBuffer,
            Matrix3x2.Identity,
            new Vector2(0, 50),
            new Vector2(100, 50),
            depthStart: 0f,
            depthEnd: 0f,
            segments: segments);

        var visibleLength = segments.Sum(segment => Vector2.Distance(segment.Start, segment.End));
        Assert.True(visibleLength < 90f);
    }

    [Fact]
    public void HiddenLineOcclusion_KeepsLineInFrontOfTriangle()
    {
        var triangle = new RenderTriangle(
            new Vector2(10, 10),
            new Vector2(90, 10),
            new Vector2(50, 90),
            RenderColor.FromRgb(200, 200, 200),
            shade: 1f,
            depthA: 1f,
            depthB: 1f,
            depthC: 1f);

        var scene = BuildScene(triangle);
        var depthBuffer = new RenderDepthBuffer();
        var hasDepth = RenderHiddenLineUtils.TryBuildDepthBuffer(
            scene,
            Matrix3x2.Identity,
            depthBuffer,
            width: 120,
            height: 120);

        Assert.True(hasDepth);

        var segments = new List<RenderLineSegment>();
        RenderHiddenLineUtils.AppendVisibleSegments(
            depthBuffer,
            Matrix3x2.Identity,
            new Vector2(0, 50),
            new Vector2(100, 50),
            depthStart: 2f,
            depthEnd: 2f,
            segments: segments);

        var visibleLength = segments.Sum(segment => Vector2.Distance(segment.Start, segment.End));
        Assert.True(visibleLength > 90f);
    }

    [Fact]
    public void HiddenLineOcclusion_RespectsClipLoops()
    {
        var triangle = new RenderTriangle(
            new Vector2(0, 0),
            new Vector2(100, 0),
            new Vector2(0, 100),
            RenderColor.FromRgb(200, 200, 200),
            shade: 1f,
            depthA: 1f,
            depthB: 1f,
            depthC: 1f);

        var clip = new List<Vector2>
        {
            new Vector2(40, 40),
            new Vector2(60, 40),
            new Vector2(60, 60),
            new Vector2(40, 60)
        };

        var depthBuffer = new RenderDepthBuffer();
        var hasDepth = RenderHiddenLineUtils.TryBuildDepthBuffer(
            new IRenderPrimitive[] { triangle },
            Matrix3x2.Identity,
            depthBuffer,
            width: 120,
            height: 120,
            clipLoops: new[] { clip });

        Assert.True(hasDepth);

        var segments = new List<RenderLineSegment>();
        RenderHiddenLineUtils.AppendVisibleSegments(
            depthBuffer,
            Matrix3x2.Identity,
            new Vector2(0, 50),
            new Vector2(100, 50),
            depthStart: 0f,
            depthEnd: 0f,
            segments: segments);

        var visibleLength = segments.Sum(segment => Vector2.Distance(segment.Start, segment.End));
        Assert.InRange(visibleLength, 10f, 100f);
    }

    private static RenderScene BuildScene(RenderTriangle triangle)
    {
        var bounds = RenderBounds.Empty.Expand(triangle.A).Expand(triangle.B).Expand(triangle.C);
        var layer = new RenderLayer(
            name: "0",
            color: RenderColor.DefaultForeground,
            isVisible: true,
            primitives: new IRenderPrimitive[] { triangle },
            bounds: bounds);

        return new RenderScene(
            new[] { layer },
            bounds,
            RenderColor.DefaultBackground,
            RenderVisualStyle.HiddenLine,
            new RenderDiagnostics(),
            RenderStats.Empty);
    }
}
