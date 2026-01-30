using System.Linq;
using ACadInspector.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderDimensionTests
{
    [Fact]
    public void BuildScene_RendersDimensionPrimitives()
    {
        var document = new CadDocument();
        var dimension = new DimensionLinear
        {
            FirstPoint = new XYZ(0, 0, 0),
            SecondPoint = new XYZ(10, 0, 0),
            DefinitionPoint = new XYZ(10, 5, 0),
            Rotation = 0.0
        };
        document.Entities.Add(dimension);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var lines = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderLine>()
            .ToArray();

        Assert.Contains(lines, item =>
            Math.Abs(item.Start.Y - 5f) < 0.001f && Math.Abs(item.End.Y - 5f) < 0.001f);
    }

    [Fact]
    public void BuildScene_AppliesDimensionStyleOverrides()
    {
        var document = new CadDocument();
        var baseStyle = new ACadSharp.Tables.DimensionStyle("BASE")
        {
            DimensionLineColor = Color.Red,
            ArrowSize = 1.0,
            TextHeight = 1.0
        };
        document.DimensionStyles.Add(baseStyle);

        var dimension = new DimensionLinear
        {
            FirstPoint = new XYZ(0, 0, 0),
            SecondPoint = new XYZ(10, 0, 0),
            DefinitionPoint = new XYZ(10, 5, 0),
            Rotation = 0.0,
            Style = baseStyle
        };
        var overrideMap = new DxfClassMap(DimensionStyle.StyleOverrideEntryName);
        var styleMap = DxfClassMap.Create<DimensionStyle>();
        var colorProperty = styleMap.DxfProperties[176];
        colorProperty.StoredValue = Color.Green.Index;
        overrideMap.DxfProperties.Add(176, colorProperty);
        dimension.SetStyleOverrideMap(overrideMap);
        document.Entities.Add(dimension);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var lines = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderLine>()
            .ToArray();

        var expected = new RenderColor(Color.Green.R, Color.Green.G, Color.Green.B, 255);
        Assert.Contains(lines, item => item.Color.Equals(expected));
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new DimensionRenderHandler(),
            new LineRenderHandler(),
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
