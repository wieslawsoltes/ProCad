using System;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Extensions;
using ACadSharp.Tables;
using ACadSharp.Objects;

namespace ACadInspector.Rendering;

public sealed class DefaultRenderStyleResolver : IRenderStyleResolver
{
    public RenderColor ResolveEntityColor(Entity entity, CadRenderSceneSettings settings)
    {
        var color = entity.GetActiveColor();
        var baseColor = RenderStyleUtils.ResolveColorOrFallback(color, settings, settings.FallbackColor);

        var alpha = ResolveEntityAlpha(entity);
        var combinedAlpha = CombineAlpha(baseColor.A, alpha);
        return new RenderColor(baseColor.R, baseColor.G, baseColor.B, combinedAlpha);
    }

    public RenderColor ResolveLayerColor(Layer layer, CadRenderSceneSettings settings)
    {
        return RenderStyleUtils.ResolveColorOrFallback(layer.Color, settings, settings.FallbackColor);
    }

    public float ResolveLineWeight(Entity entity, CadRenderSceneSettings settings)
    {
        if (!settings.DisplayLineWeight)
        {
            return 0f;
        }

        var weight = entity.GetActiveLineWeightType();
        var weightValue = (short)weight;
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

    public RenderLineCap ResolveLineCap(Entity entity, CadRenderSceneSettings settings)
    {
        var value = entity.Document?.Header.EndCaps ?? 1;
        var normalized = NormalizeEndCaps(value);
        return normalized switch
        {
            1 => RenderLineCap.Round,
            2 => RenderLineCap.Square,
            _ => RenderLineCap.Flat
        };
    }

    public RenderLineJoin ResolveLineJoin(Entity entity, CadRenderSceneSettings settings)
    {
        var value = entity.Document?.Header.JoinStyle ?? 1;
        var normalized = NormalizeJoinStyle(value);
        return normalized switch
        {
            1 => RenderLineJoin.Round,
            2 => RenderLineJoin.Bevel,
            _ => RenderLineJoin.Miter
        };
    }

    public RenderMaterial ResolveEntityMaterial(Entity entity, CadRenderSceneSettings settings)
    {
        var baseColor = ResolveEntityColor(entity, settings);
        var material = ResolveMaterial(entity);
        if (material is null)
        {
            return RenderMaterial.FromColor(baseColor);
        }

        var diffuse = ResolveMaterialColor(material.DiffuseColorMethod, material.DiffuseColor, baseColor);
        var ambient = ResolveMaterialColor(material.AmbientColorMethod, material.AmbientColor, diffuse);
        var specular = ResolveMaterialColor(material.SpecularColorMethod, material.SpecularColor, diffuse);
        var diffuseFactor = (float)material.DiffuseColorFactor;
        var ambientFactor = (float)material.AmbientColorFactor;
        var specularFactor = (float)material.SpecularColorFactor;
        var glossiness = (float)material.SpecularGlossFactor;
        var opacity = Clamp01((float)material.Opacity);
        var alpha = ApplyOpacity(baseColor.A, opacity);

        return new RenderMaterial(
            diffuse,
            ambient,
            specular,
            diffuseFactor,
            ambientFactor,
            specularFactor,
            glossiness,
            alpha);
    }

    private static short NormalizeEndCaps(short value)
    {
        return value >= 0x20 ? (short)(value >> 5) : value;
    }

    private static short NormalizeJoinStyle(short value)
    {
        return value >= 0x80 ? (short)(value >> 7) : value;
    }

    private static Material? ResolveMaterial(Entity entity)
    {
        var material = entity.Material;
        if (material is null)
        {
            return null;
        }

        if (string.Equals(material.Name, "ByLayer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(material.Name, "ByBlock", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return material;
    }

    private static RenderColor ResolveMaterialColor(ColorMethod method, Color materialColor, RenderColor fallback)
    {
        if (method != ColorMethod.Override)
        {
            return fallback;
        }

        return new RenderColor(materialColor.R, materialColor.G, materialColor.B, fallback.A);
    }

    private static byte ApplyOpacity(byte baseAlpha, float opacity)
    {
        var combined = baseAlpha * opacity;
        return (byte)Math.Clamp((int)Math.Round(combined), 0, 255);
    }

    private static float Clamp01(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0f;
        }

        return Math.Clamp(value, 0f, 1f);
    }

    private static byte ResolveEntityAlpha(Entity entity)
    {
        var transparency = ResolveTransparency(entity);
        if (transparency.IsByLayer || transparency.IsByBlock)
        {
            return 255;
        }

        var value = Math.Clamp(transparency.Value, (short)0, (short)90);
        var alpha = (int)(255 * (100 - value) / 100.0);
        return (byte)Math.Clamp(alpha, 0, 255);
    }

    private static Transparency ResolveTransparency(Entity entity)
    {
        if (entity.Transparency.IsByLayer)
        {
            return Transparency.Opaque;
        }

        if (entity.Transparency.IsByBlock && entity.Owner is BlockRecord record && record.BlockEntity is not null)
        {
            return record.BlockEntity.Transparency.IsByLayer
                ? Transparency.Opaque
                : record.BlockEntity.Transparency;
        }

        return entity.Transparency;
    }

    private static byte CombineAlpha(byte baseAlpha, byte overlayAlpha)
    {
        if (overlayAlpha >= 255)
        {
            return baseAlpha;
        }

        var combined = baseAlpha * overlayAlpha / 255f;
        return (byte)Math.Clamp((int)Math.Round(combined), 0, 255);
    }

}
