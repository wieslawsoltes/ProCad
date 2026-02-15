using System.Linq;
using System.Numerics;
using ACadInspector.Rendering;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderWipeoutTests
{
    [Fact]
    public void BuildScene_RendersWipeoutFill()
    {
        var document = new ACadSharp.CadDocument();
        var wipeout = new Wipeout
        {
            InsertPoint = new XYZ(0, 0, 0),
            UVector = XYZ.AxisX,
            VVector = XYZ.AxisY,
            ClipMode = ClipMode.Inside
        };
        wipeout.ClipBoundaryVertices.Add(new XY(0, 0));
        wipeout.ClipBoundaryVertices.Add(new XY(2, 2));
        document.Entities.Add(wipeout);

        var settings = new CadRenderSceneSettings();
        var scene = CreateSceneBuilder().Build(document, settings);
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.Contains(primitives, primitive =>
            primitive is RenderFill fill &&
            fill.Color.Equals(settings.Background));
    }

    [Fact]
    public void BuildScene_AddsWipeoutFrameWhenEnabled()
    {
        var document = new ACadSharp.CadDocument();
        var wipeout = new Wipeout
        {
            InsertPoint = new XYZ(0, 0, 0),
            UVector = XYZ.AxisX,
            VVector = XYZ.AxisY,
            ClipMode = ClipMode.Inside
        };
        wipeout.ClipBoundaryVertices.Add(new XY(0, 0));
        wipeout.ClipBoundaryVertices.Add(new XY(2, 2));
        document.Entities.Add(wipeout);

        var settings = new CadRenderSceneSettings
        {
            WipeoutFrameVisibility = RenderFrameVisibility.DisplayAndPlot
        };
        var scene = CreateSceneBuilder().Build(document, settings);
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.Contains(primitives, primitive => primitive is RenderPolyline);
    }

    [Fact]
    public void BuildScene_DoesNotAddWipeoutFrameWhenHidden()
    {
        var document = new ACadSharp.CadDocument();
        var wipeout = new Wipeout
        {
            InsertPoint = new XYZ(0, 0, 0),
            UVector = XYZ.AxisX,
            VVector = XYZ.AxisY,
            ClipMode = ClipMode.Inside
        };
        wipeout.ClipBoundaryVertices.Add(new XY(0, 0));
        wipeout.ClipBoundaryVertices.Add(new XY(2, 2));
        document.Entities.Add(wipeout);

        var settings = new CadRenderSceneSettings
        {
            WipeoutFrameVisibility = RenderFrameVisibility.Hidden
        };
        var scene = CreateSceneBuilder().Build(document, settings);
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.DoesNotContain(primitives, primitive => primitive is RenderPolyline);
    }

    [Fact]
    public void BuildScene_HiddenWipeout_RendersBoundaryWithoutMask()
    {
        var document = new ACadSharp.CadDocument();
        var wipeout = new Wipeout
        {
            InsertPoint = new XYZ(0, 0, 0),
            UVector = XYZ.AxisX,
            VVector = XYZ.AxisY,
            ClipMode = ClipMode.Inside
        };
        wipeout.ClipBoundaryVertices.Add(new XY(0, 0));
        wipeout.ClipBoundaryVertices.Add(new XY(2, 2));
        wipeout.ShowImage = false;
        document.Entities.Add(wipeout);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings
        {
            WipeoutFrameVisibility = RenderFrameVisibility.Hidden
        });
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.DoesNotContain(primitives, primitive => primitive is RenderFill or RenderHatchFill);
        var frame = Assert.Single(primitives.OfType<RenderPolyline>());
        Assert.True(frame.IsClosed);
    }

    [Fact]
    public void BuildScene_OutsideClipRendersInverseMask()
    {
        var document = new ACadSharp.CadDocument();
        document.Header.ModelSpaceExtMin = new XYZ(0, 0, 0);
        document.Header.ModelSpaceExtMax = new XYZ(10, 10, 0);

        var wipeout = new Wipeout
        {
            InsertPoint = new XYZ(0, 0, 0),
            UVector = XYZ.AxisX,
            VVector = XYZ.AxisY,
            ClipMode = ClipMode.Outside
        };
        wipeout.ClipBoundaryVertices.Add(new XY(2, 2));
        wipeout.ClipBoundaryVertices.Add(new XY(8, 8));
        document.Entities.Add(wipeout);

        var settings = new CadRenderSceneSettings();
        var scene = CreateSceneBuilder().Build(document, settings);
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        var fill = Assert.Single(primitives.OfType<RenderHatchFill>());
        Assert.Equal(RenderLoopFillMode.EvenOdd, fill.FillMode);
        Assert.Equal(2, fill.Loops.Count);
        Assert.Equal(settings.Background, fill.Color);
        Assert.DoesNotContain(primitives, primitive => primitive is RenderFill);
    }

    [Fact]
    public void BuildScene_OutsideClipHitTestSkipsInterior()
    {
        var document = new ACadSharp.CadDocument();
        document.Header.ModelSpaceExtMin = new XYZ(0, 0, 0);
        document.Header.ModelSpaceExtMax = new XYZ(10, 10, 0);

        var wipeout = new Wipeout
        {
            InsertPoint = new XYZ(0, 0, 0),
            UVector = XYZ.AxisX,
            VVector = XYZ.AxisY,
            ClipMode = ClipMode.Outside
        };
        wipeout.ClipBoundaryVertices.Add(new XY(2, 2));
        wipeout.ClipBoundaryVertices.Add(new XY(8, 8));
        document.Entities.Add(wipeout);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var engine = new RenderHitTestEngine();
        var results = new System.Collections.Generic.List<RenderHitTestResult>();

        engine.HitTestPoint(scene, new Vector2(5f, 5f), 0.05f, results);
        Assert.Empty(results);

        engine.HitTestPoint(scene, new Vector2(1f, 1f), 0.05f, results);
        Assert.Contains(results, result => result.Primitive is RenderHatchFill);
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new WipeoutRenderHandler(),
            new FallbackRenderHandler()
        };

        return new CadRenderSceneBuilder(
            new RenderEntityDispatcher(handlers),
            new DefaultRenderStyleResolver(),
            new DefaultRenderLinePatternResolver(),
            new DefaultRenderShapeResolver(),
            new DefaultRenderTextShaper(),
            new DefaultRenderEntityVisibilityResolver(),
            new DefaultRenderGeometrySampler(),
            new DefaultRenderEntityOrderResolver(),
            new RenderCacheStampProvider());
    }
}
