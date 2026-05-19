using System.Linq;
using ProCad.Rendering;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderAttributeTests
{
    [Fact]
    public void BuildScene_RendersAttributeValueInsteadOfDefinition()
    {
        var document = new ACadSharp.CadDocument();
        var block = new BlockRecord("BLOCK1");
        var definition = new AttributeDefinition
        {
            Tag = "TAG1",
            Value = "DEF",
            Height = 1.0,
            InsertPoint = new XYZ(0, 0, 0)
        };
        block.Entities.Add(definition);

        var insert = new Insert(block)
        {
            InsertPoint = new XYZ(0, 0, 0)
        };
        insert.Attributes[0].Value = "VAL";
        document.Entities.Add(insert);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var texts = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderText>()
            .Select(text => text.Text)
            .ToArray();

        Assert.Contains("VAL", texts);
        Assert.DoesNotContain("DEF", texts);
    }

    [Fact]
    public void BuildScene_SkipsHiddenAttributes()
    {
        var document = new ACadSharp.CadDocument();
        var block = new BlockRecord("BLOCK2");
        var definition = new AttributeDefinition
        {
            Tag = "TAG2",
            Value = "DEF",
            Height = 1.0,
            InsertPoint = new XYZ(0, 0, 0)
        };
        block.Entities.Add(definition);

        var insert = new Insert(block)
        {
            InsertPoint = new XYZ(0, 0, 0)
        };
        insert.Attributes[0].Value = "VAL";
        insert.Attributes[0].Flags = AttributeFlags.Hidden;
        document.Entities.Add(insert);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var texts = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderText>()
            .Select(text => text.Text)
            .ToArray();

        Assert.DoesNotContain("VAL", texts);
        Assert.DoesNotContain("DEF", texts);
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new InsertRenderHandler(NullRenderXRefResolver.Instance),
            new TextEntityRenderHandler(),
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
