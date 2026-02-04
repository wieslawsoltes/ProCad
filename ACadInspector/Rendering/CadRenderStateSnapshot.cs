using System.Collections.Generic;
using System.Numerics;

namespace ACadInspector.Rendering;

public sealed class CadRenderStateSnapshot
{
    public static readonly CadRenderStateSnapshot Empty = new(
        scene: null,
        showGrid: true,
        showAxes: true,
        enableInteractionOptimization: false,
        layerVisibilityOverrides: null,
        zoom: 1.0,
        minPixelThickness: 0.6,
        baseScale: 1.0,
        viewTransform: Matrix3x2.Identity,
        showDebugOverlay: false,
        hoverBounds: null,
        selectionBounds: null,
        hoverAnnotation: null,
        selectionAnnotation: null,
        debugBvhBounds: null);

    public RenderScene? Scene { get; }
    public bool ShowGrid { get; }
    public bool ShowAxes { get; }
    public bool EnableInteractionOptimization { get; }
    public IReadOnlyDictionary<string, bool>? LayerVisibilityOverrides { get; }
    public double Zoom { get; }
    public double MinPixelThickness { get; }
    public double BaseScale { get; }
    public Matrix3x2 ViewTransform { get; }
    public bool ShowDebugOverlay { get; }
    public RenderBounds? HoverBounds { get; }
    public RenderBounds? SelectionBounds { get; }
    public RenderAnnotation? HoverAnnotation { get; }
    public RenderAnnotation? SelectionAnnotation { get; }
    public IReadOnlyList<RenderBounds>? DebugBvhBounds { get; }

    public CadRenderStateSnapshot(
        RenderScene? scene,
        bool showGrid,
        bool showAxes,
        bool enableInteractionOptimization,
        IReadOnlyDictionary<string, bool>? layerVisibilityOverrides,
        double zoom,
        double minPixelThickness,
        double baseScale,
        Matrix3x2 viewTransform,
        bool showDebugOverlay,
        RenderBounds? hoverBounds,
        RenderBounds? selectionBounds,
        RenderAnnotation? hoverAnnotation,
        RenderAnnotation? selectionAnnotation,
        IReadOnlyList<RenderBounds>? debugBvhBounds)
    {
        Scene = scene;
        ShowGrid = showGrid;
        ShowAxes = showAxes;
        EnableInteractionOptimization = enableInteractionOptimization;
        LayerVisibilityOverrides = layerVisibilityOverrides;
        Zoom = zoom;
        MinPixelThickness = minPixelThickness;
        BaseScale = baseScale;
        ViewTransform = viewTransform;
        ShowDebugOverlay = showDebugOverlay;
        HoverBounds = hoverBounds;
        SelectionBounds = selectionBounds;
        HoverAnnotation = hoverAnnotation;
        SelectionAnnotation = selectionAnnotation;
        DebugBvhBounds = debugBvhBounds;
    }
}
