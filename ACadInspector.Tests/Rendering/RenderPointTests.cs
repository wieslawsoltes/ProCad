using System.Linq;
using ACadInspector.Rendering;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderPointTests
{
    [Fact]
    public void BuildScene_UsesPointDisplaySettings()
    {
        var document = new ACadSharp.CadDocument();
        document.Entities.Add(new Point { Location = new XYZ(0, 0, 0) });

        var settings = new CadRenderSceneSettings
        {
            PointDisplayMode = 34,
            PointDisplaySize = -2.0
        };

        var scene = CreateSceneBuilder().Build(document, settings);
        var renderPoint = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderPoint>().FirstOrDefault();

        Assert.NotNull(renderPoint);
        Assert.Equal(34, renderPoint!.DisplayMode);
        Assert.Equal(-2.0, renderPoint.DisplaySize, 3);
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new PointRenderHandler(),
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
