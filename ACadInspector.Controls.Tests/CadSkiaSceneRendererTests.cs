using System.Numerics;
using ACadInspector.Controls.Skia;
using ACadInspector.Rendering;
using SkiaSharp;

namespace ACadInspector.Controls.Tests;

public sealed class CadSkiaSceneRendererTests
{
    [Fact]
    public void RenderDrawsVisiblePrimitivePixels()
    {
        var scene = CreateLineScene();
        var viewport = CadViewportMath.CreateViewport(
            new CadSize(160d, 120d),
            scene.Bounds,
            CadViewportState.Fit,
            padding: 12d);

        using var bitmap = new SKBitmap(160, 120);
        using var canvas = new SKCanvas(bitmap);
        var renderer = new CadSkiaSceneRenderer();
        renderer.Render(canvas, scene, viewport, new CadRenderOptions { ShowAxes = false });

        var nonBackgroundPixels = 0;
        var background = new SKColor(0, 0, 0, 255);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) != background)
                {
                    nonBackgroundPixels++;
                }
            }
        }

        Assert.True(nonBackgroundPixels > 0);
    }

    private static RenderScene CreateLineScene()
    {
        var primitive = new RenderLine(
            new Vector2(-10f, 0f),
            new Vector2(10f, 0f),
            new RenderColor(255, 255, 255),
            thickness: 0f,
            RenderLineCap.Round,
            RenderLineJoin.Round);
        var layer = new RenderLayer(
            "0",
            RenderColor.DefaultForeground,
            isVisible: true,
            new IRenderPrimitive[] { primitive },
            primitive.Bounds);
        var layers = new[] { layer };

        return new RenderScene(
            layers,
            primitive.Bounds,
            new RenderColor(0, 0, 0),
            RenderVisualStyle.Wireframe,
            RenderHiddenLineSettings.Default,
            RenderSpatialIndex.Build(layers),
            primitiveMetadata: null,
            new RenderDiagnostics(),
            RenderStats.Empty);
    }
}
