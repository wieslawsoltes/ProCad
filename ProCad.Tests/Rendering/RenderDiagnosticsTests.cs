using ProCad.Rendering;
using ACadSharp;
using ACadSharp.Classes;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderDiagnosticsTests
{
    [Fact]
    public void BuildScene_RecordsUnsupportedEntity()
    {
        var document = new CadDocument();
        var entity = new UnsupportedEntity();
        document.Entities.Add(entity);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());

        Assert.True(scene.Diagnostics.Unsupported.TryGetValue(nameof(UnsupportedEntity), out var info));
        Assert.Equal(1, info.Count);
        Assert.Contains(entity.Handle, info.SampleHandles);
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
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

    private sealed class UnsupportedEntity : Entity
    {
        public override ObjectType ObjectType => ObjectType.UNDEFINED;

        public override string ObjectName => "UNSUPPORTED_ENTITY";

        public override string SubclassMarker => DxfSubclassMarker.Entity;

        public override void ApplyTransform(Transform transform)
        {
        }

        public override BoundingBox GetBoundingBox()
        {
            return new BoundingBox(new XYZ(0, 0, 0), new XYZ(2, 2, 0));
        }
    }
}
