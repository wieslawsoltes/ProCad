using System.Collections.Generic;
using System.Numerics;

namespace ACadInspector.Rendering;

public sealed class CadRenderStateSnapshot
{
    public static readonly CadRenderStateSnapshot Empty = new(
        scene: null,
        showGrid: true,
        showAxes: true,
        layerVisibilityOverrides: null,
        zoom: 1.0,
        minPixelThickness: 0.6,
        baseScale: 1.0,
        viewTransform: Matrix3x2.Identity);

    public RenderScene? Scene { get; }
    public bool ShowGrid { get; }
    public bool ShowAxes { get; }
    public IReadOnlyDictionary<string, bool>? LayerVisibilityOverrides { get; }
    public double Zoom { get; }
    public double MinPixelThickness { get; }
    public double BaseScale { get; }
    public Matrix3x2 ViewTransform { get; }

    public CadRenderStateSnapshot(
        RenderScene? scene,
        bool showGrid,
        bool showAxes,
        IReadOnlyDictionary<string, bool>? layerVisibilityOverrides,
        double zoom,
        double minPixelThickness,
        double baseScale,
        Matrix3x2 viewTransform)
    {
        Scene = scene;
        ShowGrid = showGrid;
        ShowAxes = showAxes;
        LayerVisibilityOverrides = layerVisibilityOverrides;
        Zoom = zoom;
        MinPixelThickness = minPixelThickness;
        BaseScale = baseScale;
        ViewTransform = viewTransform;
    }
}
