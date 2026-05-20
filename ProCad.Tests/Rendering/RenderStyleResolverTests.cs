using ProCad.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderStyleResolverTests
{
    [Fact]
    public void ResolveLineWeight_UsesExplicitLineWeight()
    {
        var settings = new CadRenderSceneSettings
        {
            MillimetersPerUnit = 1f,
            DefaultLineWeightMm = 0.25f,
            MinLineWeightMm = 0.05f
        };

        var line = new Line { LineWeight = LineWeightType.W50 };
        var resolver = new DefaultRenderStyleResolver();

        var thickness = resolver.ResolveLineWeight(line, settings);

        Assert.InRange(thickness, 0.49f, 0.51f);
    }

    [Fact]
    public void ResolveLineWeight_UsesLayerLineWeightWhenByLayer()
    {
        var settings = new CadRenderSceneSettings
        {
            MillimetersPerUnit = 1f,
            DefaultLineWeightMm = 0.25f,
            MinLineWeightMm = 0.05f
        };

        var layer = new Layer("Test") { LineWeight = LineWeightType.W35 };
        var line = new Line { Layer = layer, LineWeight = LineWeightType.ByLayer };
        var resolver = new DefaultRenderStyleResolver();

        var thickness = resolver.ResolveLineWeight(line, settings);

        Assert.InRange(thickness, 0.34f, 0.36f);
    }

    [Fact]
    public void ResolveEntityColor_RespectsTransparency()
    {
        var settings = new CadRenderSceneSettings();
        var line = new Line
        {
            Color = new Color(255, 120, 10),
            Transparency = new Transparency(50)
        };
        var resolver = new DefaultRenderStyleResolver();

        var color = resolver.ResolveEntityColor(line, settings);

        Assert.Equal((byte)127, color.A);
    }

    [Fact]
    public void ResolveLineCapsAndJoins_UsesHeaderSettings()
    {
        var document = new CadDocument();
        document.Header.EndCaps = 2;
        document.Header.JoinStyle = 1;

        var line = new Line();
        document.Entities.Add(line);

        var resolver = new DefaultRenderStyleResolver();
        var settings = new CadRenderSceneSettings();

        Assert.Equal(RenderLineCap.Square, resolver.ResolveLineCap(line, settings));
        Assert.Equal(RenderLineJoin.Round, resolver.ResolveLineJoin(line, settings));
    }
}
