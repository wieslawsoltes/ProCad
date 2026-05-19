using System;
using Avalonia;
using Avalonia.Media;

namespace ProCad.Rendering;

/// <summary>
/// Abstraction for render backends (Skia, custom GPU, etc.).
/// </summary>
public interface IRenderBackend : IDisposable
{
    void Render(ImmediateDrawingContext context, Size size, CadRenderStateSnapshot state, bool isInteractive);
    void ClearImageCache();
    void ClearStrokePaintCache();
    void InvalidateHiddenLineCache();
    void InvalidateInteractionCache();
}
