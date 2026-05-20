using System.Collections.Generic;
using System.Numerics;
using ProCad.Rendering;
using ProCad.Services;
using ProCad.ViewModels;
using ACadSharp.Entities;
using Xunit;

namespace ProCad.Tests.Services;

public sealed class CadSelectionToolTests
{
    [Fact]
    public void HoverHitTest_UpdatesHoverAnnotation()
    {
        var (scene, entity) = CreateScene();
        var selectionService = new CadSelectionService();
        var annotationService = new CadSelectionAnnotationService();
        annotationService.UpdateScene(scene);
        var context = new CadToolContext(scene, scene.SpatialIndex, selectionService, annotationService);
        var tool = new CadSelectionTool();

        var request = new CadRenderHitTestRequest(new Vector2(5f, 0f), 0.5f, CadHitTestKind.Hover, CadInputModifiers.None);
        tool.HandleInput(CadToolInput.Hover(request), context);

        Assert.True(annotationService.HoverAnnotation.HasValue);
        Assert.Same(entity, annotationService.HoveredObject);
    }

    [Fact]
    public void SelectHitTest_SetsSelectionServiceAndAnnotation()
    {
        var (scene, entity) = CreateScene();
        var selectionService = new CadSelectionService();
        var annotationService = new CadSelectionAnnotationService();
        annotationService.UpdateScene(scene);
        var context = new CadToolContext(scene, scene.SpatialIndex, selectionService, annotationService);
        var tool = new CadSelectionTool();

        var request = new CadRenderHitTestRequest(new Vector2(5f, 0f), 0.5f, CadHitTestKind.Select, CadInputModifiers.None);
        tool.HandleInput(CadToolInput.Select(request), context);

        Assert.Same(entity, selectionService.SelectedObject);
        Assert.True(annotationService.SelectionAnnotation.HasValue);
    }

    private static (RenderScene scene, Entity entity) CreateScene()
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

        return (scene, entity);
    }
}
