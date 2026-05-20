using System.Linq;
using ProCad.Rendering;
using ACadSharp;
using ACadSharp.Classes;
using ACadSharp.Entities;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderProxyTests
{
    [Fact]
    public void BuildScene_TracksProxyEntitiesAndAddsPoint()
    {
        var document = new CadDocument();
        var proxy = new ProxyEntity
        {
            DxfClass = new DxfClass { DxfName = "CUSTOM", CppClassName = "CustomProxy" }
        };
        document.Entities.Add(proxy);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings
        {
            IncludeUnsupportedAsPoints = true
        });

        Assert.True(scene.Diagnostics.Unsupported.ContainsKey(nameof(ProxyEntity)));

        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();
        Assert.Contains(primitives, primitive => primitive is RenderPoint);
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new ProxyEntityRenderHandler(),
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
