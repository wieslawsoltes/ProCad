using System;
using System.Collections.Generic;
using ACadSharp.Header;
using CSMath;

namespace ACadInspector.Rendering;

internal readonly struct RenderShadeEdgePolicy
{
    public bool EmitSurfaces { get; }
    public bool EmitEdges { get; }
    public bool UseLitShading { get; }
    public RenderColor? EdgeColorOverride { get; }

    public RenderShadeEdgePolicy(
        bool emitSurfaces,
        bool emitEdges,
        bool useLitShading,
        RenderColor? edgeColorOverride)
    {
        EmitSurfaces = emitSurfaces;
        EmitEdges = emitEdges;
        UseLitShading = useLitShading;
        EdgeColorOverride = edgeColorOverride;
    }
}

internal static class RenderShadeEdgeUtils
{
    public static RenderShadeEdgePolicy Resolve(CadRenderSceneSettings settings)
    {
        var black = new RenderColor(0, 0, 0, 255);
        if (settings.VisualStyle == RenderVisualStyle.Shaded)
        {
            return settings.ShadeEdge switch
            {
                ShadeEdgeType.FacesShadedEdgesNotHighlighted => new RenderShadeEdgePolicy(
                    emitSurfaces: true,
                    emitEdges: false,
                    useLitShading: true,
                    edgeColorOverride: null),
                ShadeEdgeType.FacesShadedEdgesHighlightedInBlack => new RenderShadeEdgePolicy(
                    emitSurfaces: true,
                    emitEdges: true,
                    useLitShading: true,
                    edgeColorOverride: black),
                ShadeEdgeType.FacesNotFilledEdgesInEntityColor => new RenderShadeEdgePolicy(
                    emitSurfaces: false,
                    emitEdges: true,
                    useLitShading: false,
                    edgeColorOverride: null),
                ShadeEdgeType.FacesInEntityColorEdgesInBlack => new RenderShadeEdgePolicy(
                    emitSurfaces: true,
                    emitEdges: true,
                    useLitShading: false,
                    edgeColorOverride: black),
                _ => new RenderShadeEdgePolicy(
                    emitSurfaces: true,
                    emitEdges: true,
                    useLitShading: true,
                    edgeColorOverride: null)
            };
        }

        if (settings.VisualStyle == RenderVisualStyle.HiddenLine)
        {
            return new RenderShadeEdgePolicy(
                emitSurfaces: true,
                emitEdges: true,
                useLitShading: false,
                edgeColorOverride: null);
        }

        return new RenderShadeEdgePolicy(
            emitSurfaces: false,
            emitEdges: true,
            useLitShading: false,
            edgeColorOverride: null);
    }

    public static RenderColor ResolveEdgeColor(RenderColor entityColor, RenderShadeEdgePolicy policy)
    {
        if (policy.EdgeColorOverride is null)
        {
            return entityColor;
        }

        var overrideColor = policy.EdgeColorOverride.Value;
        return new RenderColor(overrideColor.R, overrideColor.G, overrideColor.B, entityColor.A);
    }

    public static RenderLightingSettings ResolveLighting(CadRenderSceneSettings settings, RenderShadeEdgePolicy policy)
    {
        if (!policy.UseLitShading)
        {
            return settings.Lighting;
        }

        var percentage = settings.ShadeDiffuseToAmbientPercentage;
        if (percentage < 0 || percentage > 100)
        {
            return settings.Lighting;
        }

        var diffuseRatio = percentage / 100f;
        var ambientRatio = 1f - diffuseRatio;
        var lights = settings.Lighting.Lights;
        var ambientIntensity = settings.Lighting.AmbientIntensity * ambientRatio;
        if (lights is null || lights.Count == 0)
        {
            return new RenderLightingSettings(Array.Empty<RenderLight>(), ambientIntensity, settings.Lighting.AmbientColor);
        }

        var scaled = new List<RenderLight>(lights.Count);
        foreach (var light in lights)
        {
            scaled.Add(ScaleLight(light, diffuseRatio));
        }

        return new RenderLightingSettings(scaled, ambientIntensity, settings.Lighting.AmbientColor);
    }

    public static RenderColor ResolveSurfaceColor(
        RenderColor entityColor,
        RenderMaterial material,
        RenderLightingSettings lighting,
        XYZ a,
        XYZ b,
        XYZ c,
        RenderShadeEdgePolicy policy)
    {
        if (policy.UseLitShading)
        {
            return RenderLightingUtils.ComputeLitColor(a, b, c, lighting, material);
        }

        return new RenderColor(entityColor.R, entityColor.G, entityColor.B, material.Alpha);
    }

    private static RenderLight ScaleLight(RenderLight light, float factor)
    {
        var intensity = light.Intensity * factor;
        return light.Type switch
        {
            RenderLightType.Point => RenderLight.Point(light.Position, intensity, light.Color, light.Range),
            RenderLightType.Spot => RenderLight.Spot(
                light.Position,
                light.Direction,
                intensity,
                light.InnerConeAngle,
                light.OuterConeAngle,
                light.Color,
                light.Range),
            _ => RenderLight.Directional(light.Direction, intensity, light.Color)
        };
    }
}
