using System;
using System.Collections.Generic;
using ACadSharp.Header;

namespace ACadInspector.Rendering;

public sealed class CadRenderSceneSettings
{
    /// <summary>
    /// Gets the search paths for external assets such as SHX/SHP files.
    /// </summary>
    public IReadOnlyList<string> SupportPaths { get; init; } = Array.Empty<string>();

    public RenderQuality Quality { get; init; } = RenderQuality.High;

    public RenderVisualStyle VisualStyle { get; init; } = RenderVisualStyle.Wireframe;

    public RenderLightingSettings Lighting { get; init; } = RenderLightingSettings.Default;

    public bool EnableHatchFills { get; init; } = true;
    public bool EnableHatchPatterns { get; init; } = true;
    public bool EnableHatchGradients { get; init; } = true;

    /// <summary>
    /// Gets the settings used to render obscured lines in hidden-line mode.
    /// </summary>
    public RenderHiddenLineSettings HiddenLineSettings { get; init; } = RenderHiddenLineSettings.Default;

    /// <summary>
    /// Gets the shade edge mode (SHADEDGE).
    /// </summary>
    public ShadeEdgeType ShadeEdge { get; init; } = ShadeEdgeType.FacesInEntityColorEdgesInBlack;

    /// <summary>
    /// Gets the diffuse-to-ambient percentage (SHADEDIF).
    /// </summary>
    public short ShadeDiffuseToAmbientPercentage { get; init; } = 70;

    public RenderColor Background { get; init; } = RenderColor.DefaultBackground;
    public RenderColor FallbackColor { get; init; } = RenderColor.DefaultForeground;

    public float MillimetersPerUnit { get; init; } = 1f;
    public float DefaultLineWeightMm { get; init; } = 0.25f;
    public float MinLineWeightMm { get; init; } = 0.05f;
    /// <summary>
    /// Gets a value indicating whether line weights should be displayed.
    /// </summary>
    public bool DisplayLineWeight { get; init; } = true;
    /// <summary>
    /// Default dot length used for zero-length linetype segments, in millimeters.
    /// </summary>
    public float LineTypeDotLengthMm { get; init; } = 0.25f;

    public int PolylineArcPrecision { get; init; } = 24;
    public int SplinePrecision { get; init; } = 64;
    public int CirclePrecision { get; init; } = 96;

    /// <summary>
    /// Approximate width factor used by the default text shaper.
    /// </summary>
    public float TextWidthFactor { get; init; } = 0.6f;

    /// <summary>
    /// Gets the point display mode (PDMODE).
    /// </summary>
    public short PointDisplayMode { get; init; } = 0;

    /// <summary>
    /// Gets the point display size (PDSIZE).
    /// </summary>
    public double PointDisplaySize { get; init; } = 0.0;

    /// <summary>
    /// Gets a value indicating whether text should be shown as boxes (QTEXT).
    /// </summary>
    public bool QuickTextMode { get; init; }

    /// <summary>
    /// Gets a value indicating whether filled entities should be rendered filled (FILLMODE).
    /// </summary>
    public bool FillMode { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether linetype generation should be continuous across polylines (PLINEGEN).
    /// </summary>
    public bool PolylineLineTypeGeneration { get; init; }

    /// <summary>
    /// Gets a value indicating whether mirrored text should be displayed mirrored (MIRRTEXT).
    /// </summary>
    public bool MirrorText { get; init; } = true;

    /// <summary>
    /// Gets the frame visibility for XClip boundaries (XCLIPFRAME).
    /// </summary>
    public RenderFrameVisibility XClipFrameVisibility { get; init; } = RenderFrameVisibility.DisplayNotPlot;

    /// <summary>
    /// Gets the frame visibility for wipeouts (WIPEOUTFRAME).
    /// </summary>
    public RenderFrameVisibility WipeoutFrameVisibility { get; init; } = RenderFrameVisibility.DisplayAndPlot;

    /// <summary>
    /// Gets the frame visibility for underlays (PDFFRAME/DGNFRAME/DWFFRAME).
    /// </summary>
    public RenderFrameVisibility UnderlayFrameVisibility { get; init; } = RenderFrameVisibility.DisplayAndPlot;

    /// <summary>
    /// Gets a value indicating whether paper space linetype scaling is applied.
    /// </summary>
    public bool IsPaperSpace { get; init; }

    /// <summary>
    /// Gets the paper space layout name to render when in paper space.
    /// </summary>
    public string? LayoutName { get; init; }

    /// <summary>
    /// Gets the layout-level paper space linetype scaling override, when available.
    /// </summary>
    public SpaceLineTypeScaling? PaperSpaceLineTypeScalingOverride { get; init; }

    /// <summary>
    /// Gets the viewport scale used for linetype scaling in layouts.
    /// </summary>
    public float ViewportScale { get; init; } = 1f;

    /// <summary>
    /// Gets a value indicating whether model space linetype scaling is applied.
    /// </summary>
    public bool ModelSpaceLineTypeScaling { get; init; } = true;

    /// <summary>
    /// Gets the annotation scale factor applied to annotative text in model space.
    /// </summary>
    public float AnnotationScaleFactor { get; init; } = 1f;

    public bool IncludeInvisible { get; init; }
    public bool IncludeOffLayers { get; init; }
    public bool IncludeUnsupportedAsPoints { get; init; } = true;

    /// <summary>
    /// Optional performance budget for render builds.
    /// </summary>
    public RenderPerformanceBudget? PerformanceBudget { get; init; }

    /// <summary>
    /// Optional plot style table for CTB/STB overrides.
    /// </summary>
    public RenderPlotStyleTable? PlotStyleTable { get; init; }

    public int ResolvePolylineArcPrecision() => ResolvePrecision(PolylineArcPrecision, min: 4);

    public int ResolveSplinePrecision() => ResolvePrecision(SplinePrecision, min: 4);

    public int ResolveCirclePrecision() => ResolvePrecision(CirclePrecision, min: 8);

    private int ResolvePrecision(int basePrecision, int min)
    {
        var precision = basePrecision;
        switch (Quality)
        {
            case RenderQuality.Draft:
                precision = basePrecision / 4;
                break;
            case RenderQuality.Medium:
                precision = basePrecision / 2;
                break;
        }

        if (precision < min)
        {
            precision = min;
        }

        return precision;
    }
}
