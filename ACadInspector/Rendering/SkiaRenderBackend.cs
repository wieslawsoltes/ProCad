using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Skia;
using ACadInspector.Rendering;

namespace ACadInspector.Rendering.Backends;

public sealed class SkiaRenderBackend : IRenderBackend
{
    private readonly CadSkiaRenderService _renderer = new();

    public void Render(ImmediateDrawingContext context, Size size, CadRenderStateSnapshot state, bool isInteractive)
    {
        if (context is null)
        {
            return;
        }

        var leaseFeature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
        if (leaseFeature is null)
        {
            return;
        }

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;
        if (canvas is null)
        {
            return;
        }

        _renderer.Render(canvas, size, state, isInteractive);
    }

    public void ClearImageCache() => _renderer.ClearImageCache();

    public void ClearStrokePaintCache() => _renderer.ClearStrokePaintCache();

    public void InvalidateHiddenLineCache() => _renderer.InvalidateHiddenLineCache();

    public void InvalidateInteractionCache() => _renderer.InvalidateInteractionCache();

    public void Dispose()
    {
        _renderer.ClearImageCache();
        _renderer.InvalidateInteractionCache();
        _renderer.InvalidateHiddenLineCache();
    }
}
