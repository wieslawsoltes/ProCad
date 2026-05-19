using System.Numerics;
using ProCad.Rendering;

namespace ProCad.Controls;

/// <summary>
/// Maps a render scene's world coordinates to a control viewport.
/// </summary>
public readonly struct CadSceneViewport
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CadSceneViewport"/> struct.
    /// </summary>
    public CadSceneViewport(
        CadSize size,
        RenderBounds worldBounds,
        double baseScale,
        CadViewportState state)
    {
        Size = size;
        WorldBounds = worldBounds;
        BaseScale = baseScale;
        State = state;
    }

    /// <summary>
    /// Gets the viewport size in screen units.
    /// </summary>
    public CadSize Size { get; }

    /// <summary>
    /// Gets the fitted scene bounds.
    /// </summary>
    public RenderBounds WorldBounds { get; }

    /// <summary>
    /// Gets the scene-to-screen scale before applying user zoom.
    /// </summary>
    public double BaseScale { get; }

    /// <summary>
    /// Gets the viewport zoom and pan state.
    /// </summary>
    public CadViewportState State { get; }

    /// <summary>
    /// Gets the total world-to-screen scale.
    /// </summary>
    public double Scale => BaseScale * State.Zoom;

    /// <summary>
    /// Gets the fitted world center.
    /// </summary>
    public Vector2 WorldCenter => WorldBounds.IsEmpty ? Vector2.Zero : WorldBounds.Center;

    /// <summary>
    /// Gets a value indicating whether the viewport can render.
    /// </summary>
    public bool IsValid => Size.IsValid && Scale > 0d && !double.IsNaN(Scale) && !double.IsInfinity(Scale);

    /// <summary>
    /// Converts a world-space point into screen coordinates.
    /// </summary>
    public CadPoint WorldToScreen(Vector2 worldPoint)
    {
        var scale = Scale;
        var center = WorldCenter;
        var x = ((worldPoint.X - center.X) * scale) + (Size.Width * 0.5d) + State.PanX;
        var y = (Size.Height * 0.5d) - ((worldPoint.Y - center.Y) * scale) + State.PanY;
        return new CadPoint(x, y);
    }

    /// <summary>
    /// Converts a screen-space point into world coordinates.
    /// </summary>
    public Vector2 ScreenToWorld(CadPoint screenPoint)
    {
        var scale = Scale;
        if (scale <= 0d || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return WorldCenter;
        }

        var center = WorldCenter;
        var x = ((screenPoint.X - (Size.Width * 0.5d) - State.PanX) / scale) + center.X;
        var y = center.Y - ((screenPoint.Y - (Size.Height * 0.5d) - State.PanY) / scale);
        return new Vector2((float)x, (float)y);
    }

    /// <summary>
    /// Converts a screen-space distance into world-space units.
    /// </summary>
    public float ScreenToWorldDistance(double screenDistance)
    {
        var scale = Scale;
        if (scale <= 0d || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return (float)screenDistance;
        }

        return (float)(screenDistance / scale);
    }
}
