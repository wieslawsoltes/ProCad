using System;
using System.Numerics;
using Avalonia;
using ACadInspector.Rendering;
using SkiaSharp;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class CadRenderOverlayRenderingTests
{
    private const int CanvasWidth = 260;
    private const int CanvasHeight = 180;

    [Fact]
    public void OverlayScene_Primitives_AreRenderedToCanvas()
    {
        var overlay = new RenderOverlayScene(
            new[]
            {
                new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.Line,
                    Start: new Vector2(-30f, 0f),
                    End: new Vector2(30f, 0f),
                    Color: RenderColor.FromRgb(255, 255, 255),
                    StrokeWidth: 6f,
                    MarkerRadius: 0f,
                    Priority: 10)
            });

        using var bitmap = RenderOverlay(overlay, zoom: 1.0);
        var bounds = GetNonBackgroundBounds(bitmap, RenderColor.DefaultBackground);
        Assert.True(bounds.HasValue, "Expected overlay primitive to produce non-background pixels.");
    }

    [Fact]
    public void OverlayCrossMarker_KeepsStablePixelFootprint_WhenZoomChanges()
    {
        var overlay = new RenderOverlayScene(
            new[]
            {
                new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.CrossMarker,
                    Start: Vector2.Zero,
                    End: Vector2.Zero,
                    Color: RenderColor.FromRgb(255, 255, 255),
                    StrokeWidth: 1.5f,
                    MarkerRadius: 6f,
                    Priority: 10)
            });

        using var zoom1Bitmap = RenderOverlay(overlay, zoom: 1.0);
        using var zoom4Bitmap = RenderOverlay(overlay, zoom: 4.0);
        var background = RenderColor.DefaultBackground;
        var zoom1Bounds = GetNonBackgroundBounds(zoom1Bitmap, background);
        var zoom4Bounds = GetNonBackgroundBounds(zoom4Bitmap, background);
        Assert.True(zoom1Bounds.HasValue, "Expected cross marker at zoom=1 to be rendered.");
        Assert.True(zoom4Bounds.HasValue, "Expected cross marker at zoom=4 to be rendered.");

        var widthDelta = Math.Abs(zoom1Bounds!.Value.Width - zoom4Bounds!.Value.Width);
        var heightDelta = Math.Abs(zoom1Bounds.Value.Height - zoom4Bounds.Value.Height);
        Assert.True(widthDelta <= 2, $"Cross marker width changed too much across zoom: {widthDelta}px.");
        Assert.True(heightDelta <= 2, $"Cross marker height changed too much across zoom: {heightDelta}px.");
    }

    [Fact]
    public void OverlayText_WithAnchorOffset_UsesStableScreenOffset_WhenZoomChanges()
    {
        var overlay = new RenderOverlayScene(
            new[]
            {
                new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.Text,
                    Start: new Vector2(8f, -8f),
                    End: Vector2.Zero,
                    Color: RenderColor.FromRgb(255, 255, 255),
                    StrokeWidth: 1f,
                    MarkerRadius: 0f,
                    Text: "SNAP",
                    FillColor: new RenderColor(12, 16, 22, 220),
                    Priority: 10)
            });

        using var zoom1Bitmap = RenderOverlay(overlay, zoom: 1.0);
        using var zoom3Bitmap = RenderOverlay(overlay, zoom: 3.0);
        var background = RenderColor.DefaultBackground;
        var zoom1Bounds = GetNonBackgroundBounds(zoom1Bitmap, background);
        var zoom3Bounds = GetNonBackgroundBounds(zoom3Bitmap, background);
        Assert.True(zoom1Bounds.HasValue, "Expected text hint at zoom=1 to be rendered.");
        Assert.True(zoom3Bounds.HasValue, "Expected text hint at zoom=3 to be rendered.");

        var centerX = CanvasWidth / 2;
        var centerY = CanvasHeight / 2;
        var zoom1OffsetX = zoom1Bounds!.Value.X - centerX;
        var zoom1OffsetY = centerY - (zoom1Bounds.Value.Y + zoom1Bounds.Value.Height);
        var zoom3OffsetX = zoom3Bounds!.Value.X - centerX;
        var zoom3OffsetY = centerY - (zoom3Bounds.Value.Y + zoom3Bounds.Value.Height);

        Assert.True(
            Math.Abs(zoom1OffsetX - zoom3OffsetX) <= 2,
            $"Text hint X offset drifted across zoom: {zoom1OffsetX}px vs {zoom3OffsetX}px.");
        Assert.True(
            Math.Abs(zoom1OffsetY - zoom3OffsetY) <= 8,
            $"Text hint Y offset drifted across zoom: {zoom1OffsetY}px vs {zoom3OffsetY}px.");
    }

    private static SKBitmap RenderOverlay(RenderOverlayScene overlay, double zoom)
    {
        var service = new CadSkiaRenderService();
        using var surface = SKSurface.Create(new SKImageInfo(CanvasWidth, CanvasHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface is null)
        {
            throw new InvalidOperationException("Failed to create Skia surface for overlay rendering test.");
        }

        var zoomScale = (float)zoom;
        var viewTransform =
            Matrix3x2.CreateScale(zoomScale, -zoomScale) *
            Matrix3x2.CreateTranslation(CanvasWidth * 0.5f, CanvasHeight * 0.5f);

        var state = new CadRenderStateSnapshot(
            scene: null,
            showGrid: false,
            showAxes: false,
            enableInteractionOptimization: false,
            layerVisibilityOverrides: null,
            entityTypeVisibilityOverrides: null,
            zoom: zoom,
            minPixelThickness: 0.6,
            baseScale: 1.0,
            viewTransform: viewTransform,
            showDebugOverlay: false,
            hoverBounds: null,
            selectionBounds: null,
            hoverAnnotation: null,
            selectionAnnotation: null,
            overlayScene: overlay,
            dynamicInput: null,
            debugBvhBounds: null);

        service.Render(surface.Canvas, new Size(CanvasWidth, CanvasHeight), state, isInteractive: false);
        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    private static PixelRect? GetNonBackgroundBounds(SKBitmap bitmap, RenderColor background)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return null;
        }

        var minX = bitmap.Width;
        var minY = bitmap.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red == background.R && pixel.Green == background.G && pixel.Blue == background.B)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < 0 || maxY < 0)
        {
            return null;
        }

        return new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
