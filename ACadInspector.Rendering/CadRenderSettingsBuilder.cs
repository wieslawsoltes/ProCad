using System;
using System.Collections.Generic;
using System.Globalization;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Header;
using ACadSharp.Objects;
using ACadSharp.Tables;

namespace ACadInspector.Rendering;

public readonly record struct CadRenderLayoutSelection(bool IsPaperSpace, string? LayoutName)
{
    public static CadRenderLayoutSelection ModelSpace => new(false, null);
}

public static class CadRenderSettingsBuilder
{
    public static CadRenderLayoutSelection ResolveDefaultLayout(CadDocument document)
    {
        if (document.Header is not null && !document.Header.ShowModelSpace)
        {
            var layout = ResolvePaperSpaceLayout(document, layoutName: null);
            return new CadRenderLayoutSelection(true, layout?.Name);
        }

        return CadRenderLayoutSelection.ModelSpace;
    }

    public static CadRenderSceneSettings Build(
        CadDocument document,
        string? documentPath,
        CadRenderSceneSettings baseSettings,
        CadRenderLayoutSelection selection)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (baseSettings is null)
        {
            throw new ArgumentNullException(nameof(baseSettings));
        }

        var header = document.Header;
        var isPaperSpace = selection.IsPaperSpace;
        var layout = isPaperSpace ? ResolvePaperSpaceLayout(document, selection.LayoutName) : null;
        var viewport = isPaperSpace ? ResolveLayoutViewport(layout) : null;
        var viewportScale = isPaperSpace
            ? NormalizeScale(viewport?.ScaleFactor ?? 1.0)
            : ResolveModelSpaceViewportScale(document);
        var annotationScale = ResolveAnnotationScaleFactor(document);
        var supportPaths = BuildSupportPaths(documentPath);
        var visualStyle = ResolveVisualStyle(document, baseSettings.VisualStyle, selection, layout);
        var millimetersPerUnit = ResolveMillimetersPerUnit(header, baseSettings.MillimetersPerUnit);
        var splinePrecision = ResolveSplinePrecision(header, baseSettings.SplinePrecision);
        var hiddenLineSettings = ResolveHiddenLineSettings(header, baseSettings.HiddenLineSettings);
        var shadeEdge = header?.ShadeEdge ?? baseSettings.ShadeEdge;
        var shadeDiffuse = header?.ShadeDiffuseToAmbientPercentage ?? baseSettings.ShadeDiffuseToAmbientPercentage;
        var xclipFrameVisibility = ResolveXClipFrameVisibility(document, header, baseSettings.XClipFrameVisibility);
        var wipeoutFrameVisibility = ResolveWipeoutFrameVisibility(document, baseSettings.WipeoutFrameVisibility);
        var underlayFrameVisibility = ResolveUnderlayFrameVisibility(document, header, baseSettings.UnderlayFrameVisibility);
        var layoutScaling = ResolvePaperSpaceLineTypeScaling(layout);
        var plotStyleTable = ResolvePlotStyleTable(document, documentPath, layout, selection, supportPaths);

        return new CadRenderSceneSettings
        {
            SupportPaths = supportPaths,
            Quality = baseSettings.Quality,
            VisualStyle = visualStyle,
            Lighting = baseSettings.Lighting,
            EnableHatchFills = baseSettings.EnableHatchFills,
            EnableHatchPatterns = baseSettings.EnableHatchPatterns,
            EnableHatchGradients = baseSettings.EnableHatchGradients,
            HiddenLineSettings = hiddenLineSettings,
            ShadeEdge = shadeEdge,
            ShadeDiffuseToAmbientPercentage = shadeDiffuse,
            Background = baseSettings.Background,
            FallbackColor = baseSettings.FallbackColor,
            MillimetersPerUnit = millimetersPerUnit,
            DefaultLineWeightMm = baseSettings.DefaultLineWeightMm,
            MinLineWeightMm = baseSettings.MinLineWeightMm,
            DisplayLineWeight = header?.DisplayLineWeight ?? baseSettings.DisplayLineWeight,
            LineTypeDotLengthMm = baseSettings.LineTypeDotLengthMm,
            PolylineArcPrecision = baseSettings.PolylineArcPrecision,
            SplinePrecision = splinePrecision,
            CirclePrecision = baseSettings.CirclePrecision,
            TextWidthFactor = baseSettings.TextWidthFactor,
            PointDisplayMode = header?.PointDisplayMode ?? baseSettings.PointDisplayMode,
            PointDisplaySize = header?.PointDisplaySize ?? baseSettings.PointDisplaySize,
            QuickTextMode = header?.QuickTextMode ?? baseSettings.QuickTextMode,
            FillMode = header?.FillMode ?? baseSettings.FillMode,
            PolylineLineTypeGeneration = header?.PolylineLineTypeGeneration ?? baseSettings.PolylineLineTypeGeneration,
            MirrorText = header?.MirrorText ?? baseSettings.MirrorText,
            RenderAttributes = baseSettings.RenderAttributes,
            RenderAttributeDefinitions = baseSettings.RenderAttributeDefinitions,
            XClipFrameVisibility = xclipFrameVisibility,
            WipeoutFrameVisibility = wipeoutFrameVisibility,
            UnderlayFrameVisibility = underlayFrameVisibility,
            IsPaperSpace = isPaperSpace,
            LayoutName = selection.LayoutName,
            PaperSpaceLineTypeScalingOverride = layoutScaling,
            ViewportScale = viewportScale,
            ModelSpaceLineTypeScaling = baseSettings.ModelSpaceLineTypeScaling,
            AnnotationScaleFactor = annotationScale,
            IncludeInvisible = baseSettings.IncludeInvisible,
            IncludeOffLayers = baseSettings.IncludeOffLayers,
            IncludeUnsupportedAsPoints = baseSettings.IncludeUnsupportedAsPoints,
            PerformanceBudget = baseSettings.PerformanceBudget
            ,
            PlotStyleTable = plotStyleTable
        };
    }

    private static float ResolveMillimetersPerUnit(CadHeader? header, float fallback)
    {
        if (header is null)
        {
            return fallback;
        }

        return ResolveMillimetersPerUnit(header.InsUnits, fallback);
    }

    private static int ResolveSplinePrecision(CadHeader? header, int fallback)
    {
        if (header is null)
        {
            return fallback;
        }

        var segments = header.NumberOfSplineSegments;
        if (segments > 0)
        {
            return segments;
        }

        return fallback;
    }

    private static RenderHiddenLineSettings ResolveHiddenLineSettings(
        CadHeader? header,
        RenderHiddenLineSettings fallback)
    {
        if (header is null)
        {
            return fallback;
        }

        var lineType = ResolveObscuredLineType(header.ObscuredType, fallback.LineType);
        var (colorMode, color) = ResolveObscuredColor(header.ObscuredColor, fallback);
        return new RenderHiddenLineSettings(lineType, colorMode, color);
    }

    private static RenderObscuredLineType ResolveObscuredLineType(
        byte value,
        RenderObscuredLineType fallback)
    {
        return value switch
        {
            0 => RenderObscuredLineType.Off,
            1 => RenderObscuredLineType.Solid,
            2 => RenderObscuredLineType.Dotted,
            3 => RenderObscuredLineType.Dashed,
            4 => RenderObscuredLineType.ShortDash,
            5 => RenderObscuredLineType.MediumDash,
            6 => RenderObscuredLineType.LongDash,
            7 => RenderObscuredLineType.DoubleShortDash,
            8 => RenderObscuredLineType.DoubleMediumDash,
            9 => RenderObscuredLineType.DoubleLongDash,
            10 => RenderObscuredLineType.MediumDashShortDashShortDash,
            11 => RenderObscuredLineType.LongDashShortDashShortDash,
            _ => fallback
        };
    }

    private static (RenderHiddenLineColorMode Mode, RenderColor Color) ResolveObscuredColor(
        ACadSharp.Color color,
        RenderHiddenLineSettings fallback)
    {
        if (color.IsByLayer)
        {
            return (RenderHiddenLineColorMode.Layer, fallback.Color);
        }

        if (color.IsByBlock || color.Index == 0 || color.Index == 257)
        {
            return (RenderHiddenLineColorMode.Entity, fallback.Color);
        }

        return (RenderHiddenLineColorMode.Fixed, new RenderColor(color.R, color.G, color.B, 255));
    }

    private static RenderFrameVisibility ResolveXClipFrameVisibility(
        CadDocument document,
        CadHeader? header,
        RenderFrameVisibility fallback)
    {
        if (document.DictionaryVariables is not null &&
            TryParseFrameVisibility(document.DictionaryVariables.GetValue("XCLIPFRAME"), out var overrideValue))
        {
            return overrideValue;
        }

        if (header is null)
        {
            return fallback;
        }

        return header.ExternalReferenceClippingBoundaryType switch
        {
            XClipFrameType.None => RenderFrameVisibility.Hidden,
            XClipFrameType.DisplayAndPlot => RenderFrameVisibility.DisplayAndPlot,
            XClipFrameType.DisplayNotPlot => RenderFrameVisibility.DisplayNotPlot,
            _ => fallback
        };
    }

    private static RenderFrameVisibility ResolveWipeoutFrameVisibility(
        CadDocument document,
        RenderFrameVisibility fallback)
    {
        if (document.DictionaryVariables is null)
        {
            return fallback;
        }

        return TryParseFrameVisibility(document.DictionaryVariables.GetValue(DictionaryVariable.WipeoutFrame), out var value)
            ? value
            : fallback;
    }

    private static RenderFrameVisibility ResolveUnderlayFrameVisibility(
        CadDocument document,
        CadHeader? header,
        RenderFrameVisibility fallback)
    {
        if (document.DictionaryVariables is not null)
        {
            if (TryParseFrameVisibility(document.DictionaryVariables.GetValue("PDFFRAME"), out var value))
            {
                return value;
            }

            if (TryParseFrameVisibility(document.DictionaryVariables.GetValue("DWFFRAME"), out value))
            {
                return value;
            }

            if (TryParseFrameVisibility(document.DictionaryVariables.GetValue("DGNFRAME"), out value))
            {
                return value;
            }
        }

        if (header is null)
        {
            return fallback;
        }

        if (TryParseFrameVisibility(header.DwgUnderlayFramesVisibility, out var dwgValue))
        {
            return dwgValue;
        }

        if (TryParseFrameVisibility(header.DgnUnderlayFramesVisibility, out var dgnValue))
        {
            return dgnValue;
        }

        return fallback;
    }

    private static RenderPlotStyleTable? ResolvePlotStyleTable(
        CadDocument document,
        string? documentPath,
        Layout? layout,
        CadRenderLayoutSelection selection,
        IReadOnlyList<string> supportPaths)
    {
        var styleSheet = layout?.StyleSheet;
        if (string.IsNullOrWhiteSpace(styleSheet))
        {
            return null;
        }

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(documentPath))
        {
            var directory = System.IO.Path.GetDirectoryName(documentPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                candidates.Add(System.IO.Path.Combine(directory, styleSheet));
            }
        }

        foreach (var path in supportPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            candidates.Add(System.IO.Path.Combine(path, styleSheet));
        }

        candidates.Add(styleSheet);

        foreach (var candidate in candidates)
        {
            var table = RenderPlotStyleTable.TryLoad(candidate);
            if (table is not null)
            {
                return table;
            }
        }

        return null;
    }

    private static bool TryParseFrameVisibility(string? value, out RenderFrameVisibility visibility)
    {
        visibility = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        return TryParseFrameVisibility(parsed, out visibility);
    }

    private static bool TryParseFrameVisibility(char value, out RenderFrameVisibility visibility)
    {
        var raw = (int)value;
        if (raw <= 9)
        {
            return TryParseFrameVisibility(raw, out visibility);
        }

        if (value >= '0' && value <= '9')
        {
            return TryParseFrameVisibility(value - '0', out visibility);
        }

        visibility = default;
        return false;
    }

    private static bool TryParseFrameVisibility(int value, out RenderFrameVisibility visibility)
    {
        switch (value)
        {
            case 0:
                visibility = RenderFrameVisibility.Hidden;
                return true;
            case 1:
                visibility = RenderFrameVisibility.DisplayAndPlot;
                return true;
            case 2:
                visibility = RenderFrameVisibility.DisplayNotPlot;
                return true;
            default:
                visibility = default;
                return false;
        }
    }

    private static float ResolveMillimetersPerUnit(ACadSharp.Types.Units.UnitsType units, float fallback)
    {
        if (units == ACadSharp.Types.Units.UnitsType.Unitless)
        {
            return fallback;
        }

        var mm = units switch
        {
            ACadSharp.Types.Units.UnitsType.Inches => 25.4,
            ACadSharp.Types.Units.UnitsType.Feet => 304.8,
            ACadSharp.Types.Units.UnitsType.Miles => 1_609_344.0,
            ACadSharp.Types.Units.UnitsType.Millimeters => 1.0,
            ACadSharp.Types.Units.UnitsType.Centimeters => 10.0,
            ACadSharp.Types.Units.UnitsType.Meters => 1_000.0,
            ACadSharp.Types.Units.UnitsType.Kilometers => 1_000_000.0,
            ACadSharp.Types.Units.UnitsType.Microinches => 0.0000254,
            ACadSharp.Types.Units.UnitsType.Mils => 0.0254,
            ACadSharp.Types.Units.UnitsType.Yards => 914.4,
            ACadSharp.Types.Units.UnitsType.Angstroms => 0.0000001,
            ACadSharp.Types.Units.UnitsType.Nanometers => 0.000001,
            ACadSharp.Types.Units.UnitsType.Microns => 0.001,
            ACadSharp.Types.Units.UnitsType.Decimeters => 100.0,
            ACadSharp.Types.Units.UnitsType.Decameters => 10_000.0,
            ACadSharp.Types.Units.UnitsType.Hectometers => 100_000.0,
            ACadSharp.Types.Units.UnitsType.Gigameters => 1_000_000_000_000.0,
            ACadSharp.Types.Units.UnitsType.AstronomicalUnits => 149_597_870_700_000.0,
            ACadSharp.Types.Units.UnitsType.LightYears => 9_460_730_472_580_800_000.0,
            ACadSharp.Types.Units.UnitsType.Parsecs => 30_856_775_814_913_700_000.0,
            ACadSharp.Types.Units.UnitsType.USSurveyFeet => 304.800609601,
            ACadSharp.Types.Units.UnitsType.USSurveyInches => 25.4000508001,
            ACadSharp.Types.Units.UnitsType.USSurveyYards => 914.401828803,
            ACadSharp.Types.Units.UnitsType.USSurveyMiles => 1_609_347.218694,
            _ => fallback
        };

        if (double.IsNaN(mm) || double.IsInfinity(mm) || mm <= 0)
        {
            return fallback;
        }

        return (float)mm;
    }

    private static IReadOnlyList<string> BuildSupportPaths(string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(documentPath))
        {
            return Array.Empty<string>();
        }

        var directory = System.IO.Path.GetDirectoryName(documentPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Array.Empty<string>();
        }

        return new[] { directory };
    }

    private static Layout? ResolvePaperSpaceLayout(CadDocument document, string? layoutName)
    {
        if (!string.IsNullOrWhiteSpace(layoutName) && document.Layouts is not null)
        {
            foreach (var layout in document.Layouts)
            {
                if (!layout.IsPaperSpace)
                {
                    continue;
                }

                if (string.Equals(layout.Name, layoutName, StringComparison.OrdinalIgnoreCase))
                {
                    return layout;
                }
            }
        }

        if (document.Layouts is not null)
        {
            Layout? best = null;
            foreach (var layout in document.Layouts)
            {
                if (!layout.IsPaperSpace)
                {
                    continue;
                }

                if (best is null || layout.TabOrder < best.TabOrder)
                {
                    best = layout;
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        return document.PaperSpace?.Layout;
    }

    private static Viewport? ResolveLayoutViewport(Layout? layout)
    {
        if (layout is null)
        {
            return null;
        }

        if (layout.Viewports is not null)
        {
            foreach (var viewport in layout.Viewports)
            {
                if (!viewport.RepresentsPaper)
                {
                    return viewport;
                }
            }
        }

        if (layout.Viewport is not null && !layout.Viewport.RepresentsPaper)
        {
            return layout.Viewport;
        }

        return null;
    }

    private static float ResolveModelSpaceViewportScale(CadDocument document)
    {
        var vport = ResolveModelSpaceVPort(document);
        if (vport is null)
        {
            return 1f;
        }

        var height = vport.TopRight.Y - vport.BottomLeft.Y;
        if (height <= 0 || vport.ViewHeight <= 0)
        {
            return 1f;
        }

        return NormalizeScale(height / vport.ViewHeight);
    }

    private static VPort? ResolveModelSpaceVPort(CadDocument document)
    {
        if (document.VPorts is null)
        {
            return null;
        }

        if (!document.VPorts.TryGetValue(VPort.DefaultName, out var vport))
        {
            foreach (var candidate in document.VPorts)
            {
                vport = candidate;
                break;
            }
        }

        return vport;
    }

    private static RenderVisualStyle ResolveVisualStyle(
        CadDocument document,
        RenderVisualStyle fallback,
        CadRenderLayoutSelection selection,
        Layout? layout)
    {
        if (fallback != RenderVisualStyle.Wireframe)
        {
            return fallback;
        }

        if (selection.IsPaperSpace)
        {
            var viewport = ResolveLayoutViewport(layout);
            var layoutStyle = MapVisualStyle(viewport?.RenderMode);
            if (layoutStyle != RenderVisualStyle.Wireframe)
            {
                return layoutStyle;
            }
        }

        var vport = ResolveModelSpaceVPort(document);
        return MapVisualStyle(vport?.RenderMode);
    }

    private static RenderVisualStyle MapVisualStyle(RenderMode? mode)
    {
        return mode switch
        {
            RenderMode.HiddenLine => RenderVisualStyle.HiddenLine,
            RenderMode.FlatShaded => RenderVisualStyle.Shaded,
            RenderMode.GouraudShaded => RenderVisualStyle.Shaded,
            RenderMode.FlatShadedWithWireframe => RenderVisualStyle.Shaded,
            RenderMode.GouraudShadedWithWireframe => RenderVisualStyle.Shaded,
            _ => RenderVisualStyle.Wireframe
        };
    }

    private static float ResolveAnnotationScaleFactor(CadDocument document)
    {
        if (document.DictionaryVariables is null)
        {
            return 1f;
        }

        var scaleName = document.DictionaryVariables.GetValue(DictionaryVariable.CurrentAnnotationScale);
        if (string.IsNullOrWhiteSpace(scaleName))
        {
            return 1f;
        }

        if (document.Scales is null || !document.Scales.TryGet(scaleName, out var scale) || scale is null)
        {
            return 1f;
        }

        var factor = scale.ScaleFactor;
        if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0.0)
        {
            return 1f;
        }

        var annotationScale = 1.0 / factor;
        if (double.IsNaN(annotationScale) || double.IsInfinity(annotationScale) || annotationScale <= 0.0)
        {
            return 1f;
        }

        return (float)annotationScale;
    }

    private static float NormalizeScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            return 1f;
        }

        return (float)scale;
    }

    private static SpaceLineTypeScaling? ResolvePaperSpaceLineTypeScaling(Layout? layout)
    {
        if (layout is null)
        {
            return null;
        }

        return layout.LayoutFlags.HasFlag(LayoutFlags.PaperSpaceLinetypeScaling)
            ? SpaceLineTypeScaling.Normal
            : SpaceLineTypeScaling.Viewport;
    }
}
