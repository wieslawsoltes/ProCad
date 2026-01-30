using System.Linq;
using ACadInspector.Rendering;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderPaperSpaceTests
{
    [Fact]
    public void BuildScene_RendersViewportContentsInPaperSpace()
    {
        var document = new ACadSharp.CadDocument();
        var line = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0)
        };
        document.Entities.Add(line);

        var paperViewport = new Viewport
        {
            Center = new XYZ(0, 0, 0),
            Width = 1.0,
            Height = 1.0,
            ViewCenter = new XY(0, 0),
            ViewHeight = 1.0
        };
        var viewport = new Viewport
        {
            Center = new XYZ(0, 0, 0),
            Width = 10.0,
            Height = 10.0,
            ViewCenter = new XY(5, 0),
            ViewHeight = 10.0
        };
        document.PaperSpace.Entities.Add(paperViewport);
        document.PaperSpace.Entities.Add(viewport);

        var settings = new CadRenderSceneSettings { IsPaperSpace = true };
        var scene = CreateSceneBuilder().Build(document, settings);
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();
        var clip = primitives.OfType<RenderClipGroup>().FirstOrDefault();

        Assert.NotNull(clip);
        Assert.Contains(clip!.Primitives, primitive =>
            primitive is RenderLine || primitive is RenderPolyline);
        Assert.Contains(primitives, primitive =>
            primitive is RenderPolyline polyline &&
            polyline.IsClosed &&
            polyline.Points.Count == 4);
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new LineRenderHandler(),
            new ViewportRenderHandler(),
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
