using System.Linq;
using ProCad.Rendering;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderModelerGeometryTests
{
    [Fact]
    public void BuildScene_RendersModelerGeometryWires()
    {
        var document = new ACadSharp.CadDocument();
        var solid = new Solid3D();
        var wire = new ModelerGeometry.Wire();
        wire.Points.Add(new XYZ(0, 0, 0));
        wire.Points.Add(new XYZ(4, 0, 0));
        wire.Points.Add(new XYZ(4, 3, 0));
        wire.Points.Add(new XYZ(0, 3, 0));
        wire.Points.Add(new XYZ(0, 0, 0));
        solid.Wires.Add(wire);
        document.Entities.Add(solid);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.Contains(primitives, primitive => primitive is RenderLine || primitive is RenderPolyline);
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new ModelerGeometryRenderHandler(),
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
