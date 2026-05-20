using System;
using System.Numerics;
using CSMath;

namespace ProCad.Rendering;

internal static class RenderLightingUtils
{
    public static RenderColor ComputeLitColor(XYZ a, XYZ b, XYZ c, RenderLightingSettings lighting, RenderMaterial material)
    {
        var normal = XYZ.FindNormal(a, b, c);
        if (normal.IsZero())
        {
            return ApplyAmbientOnly(lighting, material);
        }

        normal = normal.Normalize();
        var diffuseLight = Vector3.Zero;
        var specularLight = Vector3.Zero;
        var centroid = new XYZ(
            (a.X + b.X + c.X) / 3.0,
            (a.Y + b.Y + c.Y) / 3.0,
            (a.Z + b.Z + c.Z) / 3.0);
        var viewDir = XYZ.AxisZ;
        if (lighting.Lights is not null)
        {
            foreach (var light in lighting.Lights)
            {
                if (!TryResolveLightVector(light, centroid, out var toLight, out var attenuation))
                {
                    continue;
                }

                var dot = normal.Dot(toLight);
                if (double.IsNaN(dot) || double.IsInfinity(dot) || dot <= 0.0)
                {
                    continue;
                }

                var intensity = (float)dot * light.Intensity * attenuation;
                diffuseLight += ToVector(light.Color) * intensity;

                if (material.SpecularFactor <= 0f || material.Glossiness <= 0f)
                {
                    continue;
                }

                var half = (toLight + viewDir).Normalize();
                if (half.IsZero())
                {
                    continue;
                }

                var specDot = normal.Dot(half);
                if (double.IsNaN(specDot) || double.IsInfinity(specDot) || specDot <= 0.0)
                {
                    continue;
                }

                var exponent = ResolveSpecularExponent(material.Glossiness);
                var specIntensity = MathF.Pow((float)specDot, exponent) * light.Intensity * attenuation;
                specularLight += ToVector(light.Color) * specIntensity;
            }
        }

        diffuseLight = Clamp01(diffuseLight);
        var ambient = Multiply(ToVector(material.AmbientColor), ToVector(lighting.AmbientColor));
        ambient *= lighting.AmbientIntensity * material.AmbientFactor;

        var diffuse = Multiply(ToVector(material.DiffuseColor), diffuseLight) * material.DiffuseFactor;
        var specular = Multiply(ToVector(material.SpecularColor), specularLight) * material.SpecularFactor;
        var combined = Clamp01(ambient + diffuse + specular);
        return ToRenderColor(combined, material.Alpha);
    }

    private static RenderColor ApplyAmbientOnly(RenderLightingSettings lighting, RenderMaterial material)
    {
        var ambient = Multiply(ToVector(material.AmbientColor), ToVector(lighting.AmbientColor));
        ambient *= lighting.AmbientIntensity * material.AmbientFactor;
        return ToRenderColor(Clamp01(ambient), material.Alpha);
    }

    private static bool TryResolveLightVector(RenderLight light, XYZ point, out XYZ toLight, out float attenuation)
    {
        toLight = XYZ.Zero;
        attenuation = 1f;
        switch (light.Type)
        {
            case RenderLightType.Directional:
                toLight = light.Direction;
                return !toLight.IsZero();
            case RenderLightType.Point:
                return TryResolvePointLight(light, point, out toLight, out attenuation);
            case RenderLightType.Spot:
                return TryResolveSpotLight(light, point, out toLight, out attenuation);
            default:
                return false;
        }
    }

    private static bool TryResolvePointLight(RenderLight light, XYZ point, out XYZ toLight, out float attenuation)
    {
        toLight = light.Position - point;
        var distance = toLight.GetLength();
        if (distance <= 1e-6)
        {
            attenuation = 0f;
            return false;
        }

        toLight /= distance;
        attenuation = ResolveAttenuation(light.Range, (float)distance);
        return attenuation > 0f;
    }

    private static bool TryResolveSpotLight(RenderLight light, XYZ point, out XYZ toLight, out float attenuation)
    {
        toLight = light.Position - point;
        var distance = toLight.GetLength();
        if (distance <= 1e-6)
        {
            attenuation = 0f;
            return false;
        }

        var fromLight = (-toLight) / distance;
        var coneDir = light.Direction.IsZero() ? XYZ.AxisZ : light.Direction.Normalize();
        var cosAngle = coneDir.Dot(fromLight);
        if (double.IsNaN(cosAngle) || double.IsInfinity(cosAngle))
        {
            attenuation = 0f;
            return false;
        }

        var cosInner = MathF.Cos(light.InnerConeAngle);
        var cosOuter = MathF.Cos(light.OuterConeAngle);
        if (cosAngle < cosOuter)
        {
            attenuation = 0f;
            return false;
        }

        var spotFactor = 1f;
        if (cosAngle < cosInner && cosInner > cosOuter)
        {
            spotFactor = (float)((cosAngle - cosOuter) / (cosInner - cosOuter));
        }

        toLight /= distance;
        attenuation = ResolveAttenuation(light.Range, (float)distance) * MathF.Max(0f, spotFactor);
        return attenuation > 0f;
    }

    private static float ResolveAttenuation(float range, float distance)
    {
        if (range <= 0f)
        {
            return 1f;
        }

        if (float.IsNaN(distance) || float.IsInfinity(distance))
        {
            return 0f;
        }

        var t = 1f - distance / range;
        return Math.Clamp(t, 0f, 1f);
    }

    private static float ResolveSpecularExponent(float glossiness)
    {
        if (float.IsNaN(glossiness) || float.IsInfinity(glossiness))
        {
            return 1f;
        }

        var clamped = Math.Clamp(glossiness, 0f, 1f);
        return 1f + clamped * 127f;
    }

    private static Vector3 ToVector(RenderColor color)
    {
        return new Vector3(color.R / 255f, color.G / 255f, color.B / 255f);
    }

    private static RenderColor ToRenderColor(Vector3 color, byte alpha)
    {
        var clamped = Clamp01(color);
        var r = (byte)Math.Clamp((int)Math.Round(clamped.X * 255f), 0, 255);
        var g = (byte)Math.Clamp((int)Math.Round(clamped.Y * 255f), 0, 255);
        var b = (byte)Math.Clamp((int)Math.Round(clamped.Z * 255f), 0, 255);
        return new RenderColor(r, g, b, alpha);
    }

    private static Vector3 Multiply(Vector3 left, Vector3 right)
    {
        return new Vector3(left.X * right.X, left.Y * right.Y, left.Z * right.Z);
    }

    private static Vector3 Clamp01(Vector3 value)
    {
        return new Vector3(
            Clamp01(value.X),
            Clamp01(value.Y),
            Clamp01(value.Z));
    }

    private static float Clamp01(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0f;
        }

        return Math.Clamp(value, 0f, 1f);
    }
}
