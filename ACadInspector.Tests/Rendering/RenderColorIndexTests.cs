using ACadInspector.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderColorIndexTests
{
    [Fact]
    public void ResolveEntityColor_Index7_UsesBlackOnPaper()
    {
        var document = new CadDocument();
        var layer = new Layer("TestLayer") { Color = new Color(7) };
        document.Layers.Add(layer);

        var line = new Line
        {
            Layer = layer,
            Color = Color.ByLayer
        };

        var settings = new CadRenderSceneSettings
        {
            IsPaperSpace = true
        };

        var context = CreateContext(document, settings);
        var color = context.ResolveEntityColor(line);

        Assert.Equal(0, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(0, color.B);
    }

    [Fact]
    public void ResolveEntityColor_Index7_UsesWhiteOnDarkBackground()
    {
        var document = new CadDocument();
        var layer = new Layer("TestLayer") { Color = new Color(7) };
        document.Layers.Add(layer);

        var line = new Line
        {
            Layer = layer,
            Color = Color.ByLayer
        };

        var settings = new CadRenderSceneSettings
        {
            IsPaperSpace = false,
            Background = RenderColor.DefaultBackground
        };

        var context = CreateContext(document, settings);
        var color = context.ResolveEntityColor(line);

        Assert.Equal(255, color.R);
        Assert.Equal(255, color.G);
        Assert.Equal(255, color.B);
    }

    private static RenderBuildContext CreateContext(CadDocument document, CadRenderSceneSettings settings)
    {
        return new RenderBuildContext(
            document,
            settings,
            new DefaultRenderStyleResolver(),
            new DefaultRenderLinePatternResolver(),
            new DefaultRenderShapeResolver(),
            new DefaultRenderTextShaper(),
            new DefaultRenderEntityVisibilityResolver(),
            new DefaultRenderGeometrySampler(),
            new DefaultRenderEntityOrderResolver(),
            new RenderEntityDispatcher(Array.Empty<IRenderEntityHandler>()),
            new RenderDiagnostics(),
            new RenderStatsAccumulator());
    }
}
