using ProCad.Rendering;

namespace ProCad.Services;

public readonly struct CadToolContext
{
    public RenderScene? Scene { get; }
    public RenderSpatialIndex? SpatialIndex { get; }
    public CadSelectionService SelectionService { get; }
    public CadSelectionAnnotationService AnnotationService { get; }

    public CadToolContext(
        RenderScene? scene,
        RenderSpatialIndex? spatialIndex,
        CadSelectionService selectionService,
        CadSelectionAnnotationService annotationService)
    {
        Scene = scene;
        SpatialIndex = spatialIndex;
        SelectionService = selectionService;
        AnnotationService = annotationService;
    }
}
