using System.Numerics;
using ProCad.Rendering;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class CadRenderImageRenderingTests
{
    [Fact]
    public void ResolveImagePaint_ModulatesAlphaByOpacityAndTintAlpha()
    {
        var image = new RenderImage(
            sourcePath: null,
            label: null,
            origin: Vector2.Zero,
            uVector: Vector2.UnitX,
            vVector: Vector2.UnitY,
            size: Vector2.One,
            color: new RenderColor(10, 20, 30, 128),
            opacity: 0.5f);

        var (paintColor, tintColor) = CadSkiaRenderService.ResolveImagePaint(image);

        Assert.Equal((byte)255, paintColor.Red);
        Assert.Equal((byte)255, paintColor.Green);
        Assert.Equal((byte)255, paintColor.Blue);
        Assert.Equal((byte)64, paintColor.Alpha);
        Assert.Equal((byte)10, tintColor.Red);
        Assert.Equal((byte)20, tintColor.Green);
        Assert.Equal((byte)30, tintColor.Blue);
        Assert.Equal((byte)255, tintColor.Alpha);
    }

    [Fact]
    public void ResolveImagePaint_ClampsOpacityToValidRange()
    {
        var image = new RenderImage(
            sourcePath: null,
            label: null,
            origin: Vector2.Zero,
            uVector: Vector2.UnitX,
            vVector: Vector2.UnitY,
            size: Vector2.One,
            color: new RenderColor(200, 180, 160, 255),
            opacity: 2.5f);

        var (paintColor, _) = CadSkiaRenderService.ResolveImagePaint(image);
        Assert.Equal((byte)255, paintColor.Alpha);

        var transparent = new RenderImage(
            sourcePath: null,
            label: null,
            origin: Vector2.Zero,
            uVector: Vector2.UnitX,
            vVector: Vector2.UnitY,
            size: Vector2.One,
            color: new RenderColor(200, 180, 160, 255),
            opacity: -1f);
        var (transparentPaintColor, _) = CadSkiaRenderService.ResolveImagePaint(transparent);
        Assert.Equal((byte)0, transparentPaintColor.Alpha);
    }

    [Fact]
    public void ResolveImagePlaceholderColor_UsesTintRgbWithComposedAlpha()
    {
        var image = new RenderImage(
            sourcePath: null,
            label: null,
            origin: Vector2.Zero,
            uVector: Vector2.UnitX,
            vVector: Vector2.UnitY,
            size: Vector2.One,
            color: new RenderColor(12, 34, 56, 128),
            opacity: 0.5f);

        var color = CadSkiaRenderService.ResolveImagePlaceholderColor(image);
        Assert.Equal((byte)12, color.Red);
        Assert.Equal((byte)34, color.Green);
        Assert.Equal((byte)56, color.Blue);
        Assert.Equal((byte)64, color.Alpha);
    }

    [Fact]
    public void ResolveImagePlaceholderColor_ClampsComposedAlphaRange()
    {
        var opaque = new RenderImage(
            sourcePath: null,
            label: null,
            origin: Vector2.Zero,
            uVector: Vector2.UnitX,
            vVector: Vector2.UnitY,
            size: Vector2.One,
            color: new RenderColor(12, 34, 56, 255),
            opacity: 3f);
        var opaqueColor = CadSkiaRenderService.ResolveImagePlaceholderColor(opaque);
        Assert.Equal((byte)255, opaqueColor.Alpha);

        var transparent = new RenderImage(
            sourcePath: null,
            label: null,
            origin: Vector2.Zero,
            uVector: Vector2.UnitX,
            vVector: Vector2.UnitY,
            size: Vector2.One,
            color: new RenderColor(12, 34, 56, 255),
            opacity: -2f);
        var transparentColor = CadSkiaRenderService.ResolveImagePlaceholderColor(transparent);
        Assert.Equal((byte)0, transparentColor.Alpha);
    }
}
