using System;
using ACadSharp;

namespace ACadInspector.Rendering;

internal static class RenderStyleUtils
{
    public static float ResolveLineWeight(LineWeightType lineWeight, CadRenderSceneSettings settings)
    {
        if (!settings.DisplayLineWeight)
        {
            return 0f;
        }

        var weightValue = (short)lineWeight;
        float lineWeightMm;
        if (weightValue < 0)
        {
            lineWeightMm = settings.DefaultLineWeightMm;
        }
        else if (weightValue == 0)
        {
            lineWeightMm = settings.MinLineWeightMm;
        }
        else
        {
            lineWeightMm = Math.Max(settings.MinLineWeightMm, weightValue / 100f);
        }

        return lineWeightMm / settings.MillimetersPerUnit;
    }

    public static RenderColor ResolveColorOrFallback(Color color, CadRenderSceneSettings settings, RenderColor fallback)
    {
        if (color.IsByLayer || color.IsByBlock)
        {
            return fallback;
        }

        return ResolveColor(color, settings, 255);
    }

    public static RenderColor ResolveColor(Color color, CadRenderSceneSettings settings, byte alpha)
    {
        if (!color.IsTrueColor && color.Index == 7)
        {
            return IsLightBackground(settings)
                ? new RenderColor(0, 0, 0, alpha)
                : new RenderColor(255, 255, 255, alpha);
        }

        return new RenderColor(color.R, color.G, color.B, alpha);
    }

    public static byte ResolveTransparencyAlpha(Transparency transparency)
    {
        if (transparency.IsByLayer || transparency.IsByBlock)
        {
            return 255;
        }

        var value = Math.Clamp(transparency.Value, (short)0, (short)90);
        var alpha = (int)(255 * (100 - value) / 100.0);
        return (byte)Math.Clamp(alpha, 0, 255);
    }

    public static RenderColor ApplyBrightnessContrast(RenderColor color, CadRenderSceneSettings settings)
    {
        return ApplyBrightnessContrast(color, settings.ViewportBrightness, settings.ViewportContrast);
    }

    public static RenderMaterial ApplyBrightnessContrast(RenderMaterial material, CadRenderSceneSettings settings)
    {
        if (IsDefaultTone(settings.ViewportBrightness, settings.ViewportContrast))
        {
            return material;
        }

        var diffuse = ApplyBrightnessContrast(material.DiffuseColor, settings);
        var ambient = ApplyBrightnessContrast(material.AmbientColor, settings);
        var specular = ApplyBrightnessContrast(material.SpecularColor, settings);
        return new RenderMaterial(
            diffuse,
            ambient,
            specular,
            material.DiffuseFactor,
            material.AmbientFactor,
            material.SpecularFactor,
            material.Glossiness,
            material.Alpha);
    }

    public static RenderColor ApplyBrightnessContrast(RenderColor color, float brightness, float contrast)
    {
        if (IsDefaultTone(brightness, contrast))
        {
            return color;
        }

        var brightnessOffset = NormalizeBrightnessOffset(brightness);
        var contrastFactor = NormalizeContrastFactor(contrast);
        var r = AdjustChannel(color.R / 255f, brightnessOffset, contrastFactor);
        var g = AdjustChannel(color.G / 255f, brightnessOffset, contrastFactor);
        var b = AdjustChannel(color.B / 255f, brightnessOffset, contrastFactor);
        return new RenderColor(ToByte(r), ToByte(g), ToByte(b), color.A);
    }

    public static RenderColor ApplyScreening(RenderColor color, float screeningPercent)
    {
        var factor = Math.Clamp(screeningPercent, 0f, 100f) / 100f;
        if (factor >= 0.999f)
        {
            return color;
        }

        var r = color.R * factor + 255f * (1f - factor);
        var g = color.G * factor + 255f * (1f - factor);
        var b = color.B * factor + 255f * (1f - factor);
        return new RenderColor(ToByte(r / 255f), ToByte(g / 255f), ToByte(b / 255f), color.A);
    }

    public static byte ApplyTransparency(byte alpha, float transparencyPercent)
    {
        var factor = 1f - Math.Clamp(transparencyPercent, 0f, 100f) / 100f;
        var combined = (int)(alpha * factor);
        return (byte)Math.Clamp(combined, 0, 255);
    }

    private static bool IsLightBackground(CadRenderSceneSettings settings)
    {
        var background = settings.IsPaperSpace ? RenderColor.DefaultPaper : settings.Background;
        var luminance = (0.2126f * background.R + 0.7152f * background.G + 0.0722f * background.B) / 255f;
        return luminance >= 0.5f;
    }

    private static bool IsDefaultTone(float brightness, float contrast)
    {
        return Math.Abs(brightness - 50f) < 0.01f && Math.Abs(contrast - 50f) < 0.01f;
    }

    private static float NormalizeBrightnessOffset(float brightness)
    {
        var value = Math.Clamp(brightness, 0f, 100f);
        return (value - 50f) / 50f;
    }

    private static float NormalizeContrastFactor(float contrast)
    {
        var value = Math.Clamp(contrast, 0f, 100f);
        return value / 50f;
    }

    private static float AdjustChannel(float channel, float brightnessOffset, float contrastFactor)
    {
        var result = (channel - 0.5f) * contrastFactor + 0.5f + brightnessOffset;
        return Math.Clamp(result, 0f, 1f);
    }

    private static byte ToByte(float normalized)
    {
        return (byte)Math.Clamp((int)Math.Round(normalized * 255f), 0, 255);
    }
}
