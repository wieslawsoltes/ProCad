using System.Collections.Generic;
using System.Numerics;
using ACadInspector.Rendering;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public class RenderHitTestEngineTests
{
    [Fact]
    public void HitTestPoint_ReportsLineHit()
    {
        var primitives = new List<IRenderPrimitive>
        {
            new RenderLine(
                new Vector2(0f, 0f),
                new Vector2(10f, 0f),
                RenderColor.DefaultForeground,
                0.5f,
                RenderLineCap.Round,
                RenderLineJoin.Round)
        };

        var bounds = RenderBounds.Empty;
        foreach (var primitive in primitives)
        {
            bounds = bounds.Expand(primitive.Bounds);
        }

        var layer = new RenderLayer("Test", RenderColor.DefaultForeground, isVisible: true, primitives, bounds);
        var spatialIndex = RenderSpatialIndex.Build(new[] { layer });
        var scene = new RenderScene(
            new[] { layer },
            bounds,
            RenderColor.DefaultBackground,
            RenderVisualStyle.Wireframe,
            RenderHiddenLineSettings.Default,
            spatialIndex,
            null,
            new RenderDiagnostics(),
            RenderStats.Empty);

        var engine = new RenderHitTestEngine();
        var results = new List<RenderHitTestResult>();
        engine.HitTestPoint(scene, new Vector2(5f, 0.2f), 0.1f, results);

        Assert.Contains(results, result => result.Primitive is RenderLine);
    }

    [Fact]
    public void HitTestPoint_ReportsCircleHit()
    {
        var primitives = new List<IRenderPrimitive>
        {
            new RenderCircle(
                new Vector2(0f, 0f),
                5f,
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
        var spatialIndex = RenderSpatialIndex.Build(new[] { layer });
        var scene = new RenderScene(
            new[] { layer },
            bounds,
            RenderColor.DefaultBackground,
            RenderVisualStyle.Wireframe,
            RenderHiddenLineSettings.Default,
            spatialIndex,
            null,
            new RenderDiagnostics(),
            RenderStats.Empty);

        var engine = new RenderHitTestEngine();
        var results = new List<RenderHitTestResult>();
        engine.HitTestPoint(scene, new Vector2(5f, 0f), 0.1f, results);

        Assert.Contains(results, result => result.Primitive is RenderCircle);
    }

    [Fact]
    public void HitTestPoint_RespectsHatchFillMode()
    {
        var outer = new List<Vector2>
        {
            new(0f, 0f),
            new(10f, 0f),
            new(10f, 10f),
            new(0f, 10f),
            new(0f, 0f)
        };
        var inner = new List<Vector2>
        {
            new(3f, 3f),
            new(7f, 3f),
            new(7f, 7f),
            new(3f, 7f),
            new(3f, 3f)
        };

        var evenOddFill = new RenderHatchFill(
            new[] { outer, inner },
            RenderColor.DefaultForeground,
            gradient: null,
            RenderLoopFillMode.EvenOdd);

        var nonZeroFill = new RenderHatchFill(
            new[] { outer, inner },
            RenderColor.DefaultForeground,
            gradient: null,
            RenderLoopFillMode.NonZero);

        var evenOddScene = BuildSceneWithPrimitive(evenOddFill);
        var nonZeroScene = BuildSceneWithPrimitive(nonZeroFill);

        var engine = new RenderHitTestEngine();
        var results = new List<RenderHitTestResult>();
        var point = new Vector2(5f, 5f);

        engine.HitTestPoint(evenOddScene, point, 0.1f, results);
        Assert.Empty(results);

        engine.HitTestPoint(nonZeroScene, point, 0.1f, results);
        Assert.NotEmpty(results);
    }

    private static RenderScene BuildSceneWithPrimitive(IRenderPrimitive primitive)
    {
        var bounds = RenderBounds.Empty.Expand(primitive.Bounds);
        var layer = new RenderLayer("Test", RenderColor.DefaultForeground, isVisible: true, new[] { primitive }, bounds);
        var spatialIndex = RenderSpatialIndex.Build(new[] { layer });
        return new RenderScene(
            new[] { layer },
            bounds,
            RenderColor.DefaultBackground,
            RenderVisualStyle.Wireframe,
            RenderHiddenLineSettings.Default,
            spatialIndex,
            null,
            new RenderDiagnostics(),
            RenderStats.Empty);
    }
}
