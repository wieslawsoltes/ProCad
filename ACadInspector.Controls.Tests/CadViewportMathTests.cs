using System.Numerics;
using ACadInspector.Rendering;

namespace ACadInspector.Controls.Tests;

public sealed class CadViewportMathTests
{
    [Fact]
    public void ZoomAtKeepsFocusPointStable()
    {
        var bounds = new RenderBounds(new Vector2(-10f, -5f), new Vector2(10f, 5f));
        var viewport = CadViewportMath.CreateViewport(
            new CadSize(800d, 600d),
            bounds,
            CadViewportState.Fit,
            padding: 20d);
        var focus = new CadPoint(620d, 180d);
        var worldBefore = viewport.ScreenToWorld(focus);

        var state = CadViewportMath.ZoomAt(viewport, focus, zoomFactor: 2d);
        var zoomedViewport = CadViewportMath.CreateViewport(
            viewport.Size,
            bounds,
            state,
            padding: 20d);

        var worldAfter = zoomedViewport.ScreenToWorld(focus);
        Assert.True(Vector2.Distance(worldBefore, worldAfter) < 0.0001f);
    }
}
