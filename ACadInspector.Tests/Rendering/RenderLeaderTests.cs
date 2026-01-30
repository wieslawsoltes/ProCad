using System.Linq;
using ACadInspector.Rendering;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderLeaderTests
{
    [Fact]
    public void BuildScene_RendersLeaderPolylineAndArrow()
    {
        var document = new ACadSharp.CadDocument();
        var leader = new Leader
        {
            ArrowHeadEnabled = true
        };
        leader.Vertices.Add(new XYZ(0, 0, 0));
        leader.Vertices.Add(new XYZ(5, 0, 0));
        document.Entities.Add(leader);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.Contains(primitives, primitive => primitive is RenderLine || primitive is RenderPolyline);
        Assert.Contains(primitives, primitive => primitive is RenderFill);
    }

    [Fact]
    public void BuildScene_RendersMultiLeaderTextAndLines()
    {
        var document = new ACadSharp.CadDocument();
        var leader = new MultiLeader();
        leader.ContextData.HasTextContents = true;
        leader.ContextData.TextLabel = "Note";
        leader.ContextData.TextLocation = new XYZ(2, 2, 0);
        leader.ContextData.TextHeight = 1.0;
        leader.ContextData.TextStyle = TextStyle.Default;

        var root = new MultiLeaderObjectContextData.LeaderRoot();
        var line = new MultiLeaderObjectContextData.LeaderLine();
        line.Points.Add(new XYZ(0, 0, 0));
        line.Points.Add(new XYZ(2, 2, 0));
        root.Lines.Add(line);
        leader.ContextData.LeaderRoots.Add(root);
        document.Entities.Add(leader);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.Contains(primitives, primitive => primitive is RenderText);
        Assert.Contains(primitives, primitive => primitive is RenderLine || primitive is RenderPolyline);
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new LeaderRenderHandler(),
            new MultiLeaderRenderHandler(),
            new SolidRenderHandler(),
            new MTextRenderHandler(),
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
