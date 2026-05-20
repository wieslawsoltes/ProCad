using System;
using ProCad.Rendering;
using Avalonia;
using SkiaSharp;

namespace ProCad.Services;

public sealed class SkiaBlockPreviewRenderer : IBlockPreviewRenderer
{
    public byte[]? Render(CadRenderStateSnapshot snapshot, int size)
    {
        if (snapshot.Scene is null || snapshot.Scene.Bounds.IsEmpty)
        {
            return null;
        }

        var pixelSize = Math.Max(1, size);
        var info = new SKImageInfo(pixelSize, pixelSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface is null)
        {
            return null;
        }

        var renderer = new CadSkiaRenderService();
        renderer.Render(surface.Canvas, new Size(pixelSize, pixelSize), snapshot, isInteractive: false);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }
}
