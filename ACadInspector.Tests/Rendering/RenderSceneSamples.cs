using System.Collections.Generic;
using System.Numerics;
using ACadInspector.Rendering;

namespace ACadInspector.Tests.Rendering;

internal static class RenderSceneSamples
{
    public static RenderScene CreateBaselineScene()
    {
        var primitives = new List<IRenderPrimitive>
        {
            new RenderLine(
                new Vector2(-12f, -6f),
                new Vector2(12f, 8f),
                RenderColor.FromRgb(245, 170, 90),
                0.2f,
                RenderLineCap.Round,
                RenderLineJoin.Round),
            new RenderCircle(
                new Vector2(-4f, 4f),
                3.5f,
                RenderColor.FromRgb(90, 200, 220),
                0.15f,
                RenderLineCap.Round,
                RenderLineJoin.Round),
            new RenderPolyline(
                new List<Vector2>
                {
                    new(-8f, -4f),
                    new(-2f, -10f),
                    new(6f, -6f)
                },
                isClosed: false,
                color: RenderColor.FromRgb(140, 220, 140),
                thickness: 0.12f,
                lineCap: RenderLineCap.Round,
                lineJoin: RenderLineJoin.Round)
        };

        var bounds = RenderBounds.Empty;
        foreach (var primitive in primitives)
        {
            bounds = bounds.Expand(primitive.Bounds);
        }

        var layer = new RenderLayer("Baseline", RenderColor.DefaultForeground, isVisible: true, primitives, bounds);
        return new RenderScene(
            new[] { layer },
            bounds,
            RenderColor.DefaultBackground,
            RenderVisualStyle.Wireframe,
            new RenderDiagnostics(),
            RenderStats.Empty);
    }
}
