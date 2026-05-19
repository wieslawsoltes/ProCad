using System;
using CSMath;

namespace ProCad.Rendering;

/// <summary>
/// Describes supported light types in the rendering pipeline.
/// </summary>
public enum RenderLightType
{
    Directional = 0,
    Point = 1,
    Spot = 2
}

/// <summary>
/// Represents a light source used by the shading pipeline.
/// </summary>
public readonly struct RenderLight
{
    /// <summary>
    /// Gets the light type.
    /// </summary>
    public RenderLightType Type { get; }

    /// <summary>
    /// Gets the normalized direction. For directional lights, this is the vector from the surface toward the light.
    /// For spot lights, this is the cone direction pointing from the light towards the scene.
    /// </summary>
    public XYZ Direction { get; }

    /// <summary>
    /// Gets the light position for point and spot lights.
    /// </summary>
    public XYZ Position { get; }

    /// <summary>
    /// Gets the light intensity in the range [0, 1].
    /// </summary>
    public float Intensity { get; }

    /// <summary>
    /// Gets the light color (RGB only).
    /// </summary>
    public RenderColor Color { get; }

    /// <summary>
    /// Gets the maximum range for point and spot lights. Values less than or equal to zero disable attenuation.
    /// </summary>
    public float Range { get; }

    /// <summary>
    /// Gets the inner cone angle in radians for spot lights.
    /// </summary>
    public float InnerConeAngle { get; }

    /// <summary>
    /// Gets the outer cone angle in radians for spot lights.
    /// </summary>
    public float OuterConeAngle { get; }

    /// <summary>
    /// Initializes a directional light using the supplied direction.
    /// </summary>
    public RenderLight(XYZ direction, float intensity)
        : this(direction, intensity, new RenderColor(255, 255, 255, 255))
    {
    }

    /// <summary>
    /// Initializes a directional light using the supplied direction and color.
    /// </summary>
    public RenderLight(XYZ direction, float intensity, RenderColor color)
        : this(RenderLightType.Directional, direction, XYZ.Zero, intensity, color, 0f, 0f, 0f)
    {
    }

    /// <summary>
    /// Creates a directional light.
    /// </summary>
    public static RenderLight Directional(XYZ direction, float intensity, RenderColor color)
    {
        return new RenderLight(RenderLightType.Directional, direction, XYZ.Zero, intensity, color, 0f, 0f, 0f);
    }

    /// <summary>
    /// Creates a point light at the specified position.
    /// </summary>
    public static RenderLight Point(XYZ position, float intensity, RenderColor color, float range = 0f)
    {
        return new RenderLight(RenderLightType.Point, XYZ.AxisZ, position, intensity, color, range, 0f, 0f);
    }

    /// <summary>
    /// Creates a spot light at the specified position with a cone in radians.
    /// </summary>
    public static RenderLight Spot(
        XYZ position,
        XYZ direction,
        float intensity,
        float innerConeAngle,
        float outerConeAngle,
        RenderColor color,
        float range = 0f)
    {
        return new RenderLight(
            RenderLightType.Spot,
            direction,
            position,
            intensity,
            color,
            range,
            innerConeAngle,
            outerConeAngle);
    }

    private RenderLight(
        RenderLightType type,
        XYZ direction,
        XYZ position,
        float intensity,
        RenderColor color,
        float range,
        float innerConeAngle,
        float outerConeAngle)
    {
        Type = type;
        Direction = NormalizeDirection(direction);
        Position = position;
        Intensity = Clamp01(intensity);
        Color = new RenderColor(color.R, color.G, color.B, 255);
        Range = ClampNonNegative(range);
        InnerConeAngle = ClampAngle(innerConeAngle);
        OuterConeAngle = ClampAngle(MathF.Max(innerConeAngle, outerConeAngle));
    }

    private static XYZ NormalizeDirection(XYZ direction)
    {
        if (direction.IsZero())
        {
            return XYZ.AxisZ;
        }

        var normalized = direction.Normalize();
        if (normalized.IsZero())
        {
            return XYZ.AxisZ;
        }

        return normalized;
    }

    private static float Clamp01(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0f;
        }

        return Math.Clamp(value, 0f, 1f);
    }

    private static float ClampNonNegative(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0f;
        }

        return MathF.Max(0f, value);
    }

    private static float ClampAngle(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0f;
        }

        return Math.Clamp(value, 0f, MathF.PI);
    }
}
