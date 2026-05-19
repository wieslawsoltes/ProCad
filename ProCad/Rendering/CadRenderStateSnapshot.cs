using System.Collections.Generic;
using System.Numerics;

namespace ProCad.Rendering;

public sealed class CadRenderStateSnapshot
{
    public static readonly CadRenderStateSnapshot Empty = new(
        scene: null,
        showGrid: true,
        showAxes: true,
        enableInteractionOptimization: false,
        layerVisibilityOverrides: null,
        entityTypeVisibilityOverrides: null,
        zoom: 1.0,
        minPixelThickness: 0.6,
        baseScale: 1.0,
        viewTransform: Matrix3x2.Identity,
        showDebugOverlay: false,
        hoverBounds: null,
        selectionBounds: null,
        hoverAnnotation: null,
        selectionAnnotation: null,
        overlayScene: RenderOverlayScene.Empty,
        dynamicInput: null,
        debugBvhBounds: null);

    public RenderScene? Scene { get; }
    public bool ShowGrid { get; }
    public bool ShowAxes { get; }
    public bool EnableInteractionOptimization { get; }
    public IReadOnlyDictionary<string, bool>? LayerVisibilityOverrides { get; }
    public IReadOnlyDictionary<string, bool>? EntityTypeVisibilityOverrides { get; }
    public double Zoom { get; }
    public double MinPixelThickness { get; }
    public double BaseScale { get; }
    public Matrix3x2 ViewTransform { get; }
    public bool ShowDebugOverlay { get; }
    public RenderBounds? HoverBounds { get; }
    public RenderBounds? SelectionBounds { get; }
    public RenderAnnotation? HoverAnnotation { get; }
    public RenderAnnotation? SelectionAnnotation { get; }
    public RenderOverlayScene OverlayScene { get; }
    public CadDynamicInputPayload? DynamicInput { get; }
    public IReadOnlyList<RenderBounds>? DebugBvhBounds { get; }

    public CadRenderStateSnapshot(
        RenderScene? scene,
        bool showGrid,
        bool showAxes,
        bool enableInteractionOptimization,
        IReadOnlyDictionary<string, bool>? layerVisibilityOverrides,
        IReadOnlyDictionary<string, bool>? entityTypeVisibilityOverrides,
        double zoom,
        double minPixelThickness,
        double baseScale,
        Matrix3x2 viewTransform,
        bool showDebugOverlay,
        RenderBounds? hoverBounds,
        RenderBounds? selectionBounds,
        RenderAnnotation? hoverAnnotation,
        RenderAnnotation? selectionAnnotation,
        RenderOverlayScene? overlayScene,
        CadDynamicInputPayload? dynamicInput,
        IReadOnlyList<RenderBounds>? debugBvhBounds)
    {
        Scene = scene;
        ShowGrid = showGrid;
        ShowAxes = showAxes;
        EnableInteractionOptimization = enableInteractionOptimization;
        LayerVisibilityOverrides = layerVisibilityOverrides;
        EntityTypeVisibilityOverrides = entityTypeVisibilityOverrides;
        Zoom = zoom;
        MinPixelThickness = minPixelThickness;
        BaseScale = baseScale;
        ViewTransform = viewTransform;
        ShowDebugOverlay = showDebugOverlay;
        HoverBounds = hoverBounds;
        SelectionBounds = selectionBounds;
        HoverAnnotation = hoverAnnotation;
        SelectionAnnotation = selectionAnnotation;
        OverlayScene = overlayScene ?? RenderOverlayScene.Empty;
        DynamicInput = dynamicInput;
        DebugBvhBounds = debugBvhBounds;
    }
}
