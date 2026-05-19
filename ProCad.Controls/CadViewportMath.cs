using ProCad.Rendering;

namespace ProCad.Controls;

/// <summary>
/// Provides platform-neutral viewport math for CAD controls.
/// </summary>
public static class CadViewportMath
{
    /// <summary>
    /// Gets the minimum accepted zoom factor.
    /// </summary>
    public const double MinimumZoom = 0.01d;

    /// <summary>
    /// Gets the maximum accepted zoom factor.
    /// </summary>
    public const double MaximumZoom = 1000d;

    /// <summary>
    /// Creates a viewport from scene bounds and current user state.
    /// </summary>
    public static CadSceneViewport CreateViewport(
        CadSize size,
        RenderBounds bounds,
        CadViewportState state,
        double padding)
    {
        var scale = CalculateBaseScale(size, bounds, padding);
        return new CadSceneViewport(size, bounds, scale, state);
    }

    /// <summary>
    /// Calculates a fitted base scale for the provided bounds.
    /// </summary>
    public static double CalculateBaseScale(CadSize size, RenderBounds bounds, double padding)
    {
        if (!size.IsValid || bounds.IsEmpty)
        {
            return 1d;
        }

        var usableWidth = Math.Max(1d, size.Width - (Math.Max(0d, padding) * 2d));
        var usableHeight = Math.Max(1d, size.Height - (Math.Max(0d, padding) * 2d));
        var worldSize = bounds.Size;
        var worldWidth = Math.Max(Math.Abs(worldSize.X), 1e-4f);
        var worldHeight = Math.Max(Math.Abs(worldSize.Y), 1e-4f);
        var scale = Math.Min(usableWidth / worldWidth, usableHeight / worldHeight);
        return scale > 0d && !double.IsNaN(scale) && !double.IsInfinity(scale) ? scale : 1d;
    }

    /// <summary>
    /// Returns a zoom state that keeps the provided focus point stable on screen.
    /// </summary>
    public static CadViewportState ZoomAt(
        CadSceneViewport viewport,
        CadPoint screenFocus,
        double zoomFactor)
    {
        if (!viewport.IsValid)
        {
            return viewport.State;
        }

        var oldState = viewport.State;
        var newZoom = NormalizeZoom(oldState.Zoom * zoomFactor);
        if (Math.Abs(newZoom - oldState.Zoom) < double.Epsilon)
        {
            return oldState;
        }

        var worldFocus = viewport.ScreenToWorld(screenFocus);
        var center = viewport.WorldCenter;
        var newScale = viewport.BaseScale * newZoom;
        var panX = screenFocus.X - (viewport.Size.Width * 0.5d) - ((worldFocus.X - center.X) * newScale);
        var panY = screenFocus.Y - (viewport.Size.Height * 0.5d) + ((worldFocus.Y - center.Y) * newScale);
        return new CadViewportState(newZoom, panX, panY);
    }

    /// <summary>
    /// Returns a panned viewport state.
    /// </summary>
    public static CadViewportState Pan(CadViewportState state, double deltaX, double deltaY)
    {
        return new CadViewportState(
            state.Zoom,
            NormalizeOffset(state.PanX + deltaX),
            NormalizeOffset(state.PanY + deltaY));
    }

    /// <summary>
    /// Normalizes a zoom value for control use.
    /// </summary>
    public static double NormalizeZoom(double zoom)
    {
        if (double.IsNaN(zoom) || double.IsInfinity(zoom) || zoom <= 0d)
        {
            return 1d;
        }

        return Math.Clamp(zoom, MinimumZoom, MaximumZoom);
    }

    /// <summary>
    /// Normalizes a pan offset.
    /// </summary>
    public static double NormalizeOffset(double offset)
    {
        return double.IsNaN(offset) || double.IsInfinity(offset) ? 0d : offset;
    }
}
