using System.Collections.Generic;
using System.Numerics;
using ProCad.Rendering;
using ProCad.Services;
using ACadSharp.Entities;
using Xunit;

namespace ProCad.Tests.Services;

public sealed class CadSelectionAnnotationServiceTests
{
    [Fact]
    public void UpdateSelection_UsesSceneBoundsAndLabel()
    {
        var (scene, entity, primitive) = CreateScene();
        var service = new CadSelectionAnnotationService();
        service.UpdateScene(scene);

        service.UpdateSelection(entity, null);

        Assert.True(service.SelectionAnnotation.HasValue);
        var annotation = service.SelectionAnnotation!.Value;
        Assert.Equal(primitive.Bounds.Min, annotation.Bounds.Min);
        Assert.Equal(primitive.Bounds.Max, annotation.Bounds.Max);
        Assert.False(string.IsNullOrWhiteSpace(annotation.Label));
    }

    [Fact]
    public void UpdateHover_TracksHoverObject()
    {
        var (scene, entity, _) = CreateScene();
        var service = new CadSelectionAnnotationService();
        service.UpdateScene(scene);

        service.UpdateHover(entity, null);

        Assert.Same(entity, service.HoveredObject);
        Assert.True(service.HoverAnnotation.HasValue);
    }

    [Fact]
    public void UpdateSelection_UseSceneGeometryFalse_PrefersFallbackBoundsAndSkipsSceneGeometry()
    {
        var (scene, entity, primitive) = CreateScene();
        var service = new CadSelectionAnnotationService();
        service.UpdateScene(scene);
        var fallback = new RenderBounds(new Vector2(20f, 10f), new Vector2(30f, 18f));

        service.UpdateSelection(entity, fallback, primitive: null, useSceneGeometry: false);

        Assert.True(service.SelectionAnnotation.HasValue);
        var annotation = service.SelectionAnnotation!.Value;
        Assert.Equal(fallback.Min, annotation.Bounds.Min);
        Assert.Equal(fallback.Max, annotation.Bounds.Max);
        Assert.False(annotation.HasGeometry);
        Assert.NotEqual(primitive.Bounds.Min, annotation.Bounds.Min);
    }

    private static (RenderScene scene, Entity entity, RenderLine primitive) CreateScene()
    {
        var entity = new Line();
        var primitive = new RenderLine(
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            RenderColor.DefaultForeground,
            0.2f,
            RenderLineCap.Round,
            RenderLineJoin.Round);

        var bounds = primitive.Bounds;
        var layer = new RenderLayer("Test", RenderColor.DefaultForeground, true, new[] { primitive }, bounds);
        var spatialIndex = RenderSpatialIndex.Build(new[] { layer });
        var metadata = new Dictionary<IRenderPrimitive, RenderPrimitiveMetadata>(ReferenceEqualityComparer.Instance)
        {
            [primitive] = new RenderPrimitiveMetadata(entity, entity)
        };

        var scene = new RenderScene(
            new[] { layer },
            bounds,
            RenderColor.DefaultBackground,
            RenderVisualStyle.Wireframe,
            RenderHiddenLineSettings.Default,
            spatialIndex,
            metadata,
            new RenderDiagnostics(),
            RenderStats.Empty);

        return (scene, entity, primitive);
    }
}
