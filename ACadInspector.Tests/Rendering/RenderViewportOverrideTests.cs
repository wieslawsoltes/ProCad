using ACadInspector.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderViewportOverrideTests
{
    [Fact]
    public void ResolveEntityColor_UsesViewportLayerOverride()
    {
        var document = new CadDocument();
        var layer = new Layer("TestLayer");
        document.Layers.Add(layer);

        var viewport = new Viewport
        {
            Center = new XYZ(0, 0, 0),
            Width = 10,
            Height = 10
        };

        var record = new XRecord();
        record.CreateEntry(0, viewport);
        record.CreateEntry(62, (short)1);

        var xdict = layer.CreateExtendedDictionary();
        xdict.Add("ACAD_LAYEROVERRIDE", record);

        var overrides = ViewportLayerOverrideResolver.Resolve(document, viewport);
        Assert.NotNull(overrides);

        var settings = new CadRenderSceneSettings();
        var context = CreateContext(document, settings, overrides);
        var line = new Line { Layer = layer };

        var color = context.ResolveEntityColor(line);

        Assert.Equal(255, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(0, color.B);
    }

    private static RenderBuildContext CreateContext(
        CadDocument document,
        CadRenderSceneSettings settings,
        RenderLayerOverrides? overrides)
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
            new RenderStatsAccumulator(),
            overrides);
    }
}
