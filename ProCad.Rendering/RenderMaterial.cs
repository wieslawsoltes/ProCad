using System;

namespace ProCad.Rendering;

/// <summary>
/// Describes material properties used by the rendering pipeline.
/// </summary>
public readonly struct RenderMaterial
{
    /// <summary>
    /// Gets the diffuse (base) color without transparency.
    /// </summary>
    public RenderColor DiffuseColor { get; }
    /// <summary>
    /// Gets the ambient color without transparency.
    /// </summary>
    public RenderColor AmbientColor { get; }
    /// <summary>
    /// Gets the specular color without transparency.
    /// </summary>
    public RenderColor SpecularColor { get; }
    /// <summary>
    /// Gets the diffuse contribution factor in the range [0, 1].
    /// </summary>
    public float DiffuseFactor { get; }
    /// <summary>
    /// Gets the ambient contribution factor in the range [0, 1].
    /// </summary>
    public float AmbientFactor { get; }
    /// <summary>
    /// Gets the specular contribution factor in the range [0, 1].
    /// </summary>
    public float SpecularFactor { get; }
    /// <summary>
    /// Gets the glossiness factor in the range [0, 1].
    /// </summary>
    public float Glossiness { get; }
    /// <summary>
    /// Gets the combined alpha after entity transparency and material opacity.
    /// </summary>
    public byte Alpha { get; }

    /// <summary>
    /// Gets the diffuse color with the combined alpha applied.
    /// </summary>
    public RenderColor DiffuseWithAlpha => new(DiffuseColor.R, DiffuseColor.G, DiffuseColor.B, Alpha);
    /// <summary>
    /// Gets the specular color with the combined alpha applied.
    /// </summary>
    public RenderColor SpecularWithAlpha => new(SpecularColor.R, SpecularColor.G, SpecularColor.B, Alpha);

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderMaterial"/> struct.
    /// </summary>
    public RenderMaterial(
        RenderColor diffuseColor,
        RenderColor ambientColor,
        float diffuseFactor,
        float ambientFactor,
        byte alpha)
        : this(diffuseColor, ambientColor, diffuseColor, diffuseFactor, ambientFactor, 0f, 0.5f, alpha)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderMaterial"/> struct with specular properties.
    /// </summary>
    public RenderMaterial(
        RenderColor diffuseColor,
        RenderColor ambientColor,
        RenderColor specularColor,
        float diffuseFactor,
        float ambientFactor,
        float specularFactor,
        float glossiness,
        byte alpha)
    {
        DiffuseColor = new RenderColor(diffuseColor.R, diffuseColor.G, diffuseColor.B, 255);
        AmbientColor = new RenderColor(ambientColor.R, ambientColor.G, ambientColor.B, 255);
        SpecularColor = new RenderColor(specularColor.R, specularColor.G, specularColor.B, 255);
        DiffuseFactor = Clamp01(diffuseFactor);
        AmbientFactor = Clamp01(ambientFactor);
        SpecularFactor = Clamp01(specularFactor);
        Glossiness = Clamp01(glossiness);
        Alpha = alpha;
    }

    /// <summary>
    /// Creates a default material based on the provided color.
    /// </summary>
    public static RenderMaterial FromColor(RenderColor color)
    {
        return new RenderMaterial(color, color, color, 1f, 1f, 0f, 0.5f, color.A);
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
