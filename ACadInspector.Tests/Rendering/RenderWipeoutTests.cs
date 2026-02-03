using System.Linq;
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
            VVector = XYZ.AxisY
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
            VVector = XYZ.AxisY
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
            VVector = XYZ.AxisY
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
