using System;
using System.Collections.Generic;

namespace ProCad.Rendering;

public readonly struct RenderLightingSettings
{
    public IReadOnlyList<RenderLight> Lights { get; }
    public float AmbientIntensity { get; }
    public RenderColor AmbientColor { get; }

    public static RenderLightingSettings Default =>
        new(
            new[]
            {
                new RenderLight(new CSMath.XYZ(0, 0, 1), 0.85f, new RenderColor(255, 255, 255, 255)),
                new RenderLight(new CSMath.XYZ(0.4, 0.2, 0.7), 0.35f, new RenderColor(255, 247, 235, 255))
            },
            0.2f,
            new RenderColor(255, 255, 255, 255));

    public RenderLightingSettings(IReadOnlyList<RenderLight> lights, float ambientIntensity)
        : this(lights, ambientIntensity, new RenderColor(255, 255, 255, 255))
    {
    }

    public RenderLightingSettings(IReadOnlyList<RenderLight> lights, float ambientIntensity, RenderColor ambientColor)
    {
        Lights = lights ?? Array.Empty<RenderLight>();
        AmbientIntensity = Clamp01(ambientIntensity);
        AmbientColor = new RenderColor(ambientColor.R, ambientColor.G, ambientColor.B, 255);
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
