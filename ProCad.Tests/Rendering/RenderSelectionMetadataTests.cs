using System.Numerics;
using ProCad.Rendering;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Xunit;

namespace ProCad.Tests.Rendering;

public class RenderSelectionMetadataTests
{
    [Fact]
    public void LayerBuilder_TracksEntityMetadata()
    {
        var selection = new RenderSelectionContext();
        var builder = new RenderLayerBuilder("0", RenderColor.DefaultForeground, isVisible: true, selection.CreateMetadata);
        var entity = new Line();

        RenderLine? primitive = null;
        using (selection.EnterEntity(entity))
        {
            primitive = new RenderLine(
                new Vector2(0f, 0f),
                new Vector2(5f, 0f),
                RenderColor.DefaultForeground,
                0.1f,
                RenderLineCap.Round,
                RenderLineJoin.Round);
            builder.Add(primitive);
        }

        Assert.NotNull(primitive);
        Assert.True(builder.TryGetMetadata(primitive!, out var metadata));
        Assert.Same(entity, metadata.SourceEntity);
        Assert.Same(entity, metadata.OwnerEntity);
    }

    [Fact]
    public void LayerBuilder_TracksOwnerOverride()
    {
        var selection = new RenderSelectionContext();
        var builder = new RenderLayerBuilder("0", RenderColor.DefaultForeground, isVisible: true, selection.CreateMetadata);
        var owner = new Insert(new BlockRecord("Block"));
        var child = new Line();

        RenderLine? primitive = null;
        using (selection.EnterOwnerOverride(owner))
        using (selection.EnterEntity(child))
        {
            primitive = new RenderLine(
                new Vector2(0f, 0f),
                new Vector2(5f, 0f),
                RenderColor.DefaultForeground,
                0.1f,
                RenderLineCap.Round,
                RenderLineJoin.Round);
            builder.Add(primitive);
        }

        Assert.NotNull(primitive);
        Assert.True(builder.TryGetMetadata(primitive!, out var metadata));
        Assert.Same(child, metadata.SourceEntity);
        Assert.Same(owner, metadata.OwnerEntity);
    }
}
