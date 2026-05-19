using ProCad.Rendering;
using CSMath;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderLightingUtilsTests
{
    [Fact]
    public void ComputeLitColor_UsesAmbientTint()
    {
        var lighting = new RenderLightingSettings(
            lights: System.Array.Empty<RenderLight>(),
            ambientIntensity: 1f,
            ambientColor: new RenderColor(255, 0, 0, 255));

        var material = new RenderMaterial(
            diffuseColor: new RenderColor(255, 255, 255, 255),
            ambientColor: new RenderColor(255, 255, 255, 255),
            diffuseFactor: 1f,
            ambientFactor: 1f,
            alpha: 255);

        var color = RenderLightingUtils.ComputeLitColor(
            new XYZ(0, 0, 0),
            new XYZ(1, 0, 0),
            new XYZ(0, 1, 0),
            lighting,
            material);

        Assert.True(color.R > 200);
        Assert.True(color.G < 20);
        Assert.True(color.B < 20);
    }

    [Fact]
    public void ComputeLitColor_UsesLightColor()
    {
        var lighting = new RenderLightingSettings(
            lights: new[]
            {
                new RenderLight(new XYZ(0, 0, 1), 1f, new RenderColor(0, 0, 255, 255))
            },
            ambientIntensity: 0f,
            ambientColor: new RenderColor(255, 255, 255, 255));

        var material = new RenderMaterial(
            diffuseColor: new RenderColor(255, 255, 255, 255),
            ambientColor: new RenderColor(255, 255, 255, 255),
            diffuseFactor: 1f,
            ambientFactor: 1f,
            alpha: 255);

        var color = RenderLightingUtils.ComputeLitColor(
            new XYZ(0, 0, 0),
            new XYZ(1, 0, 0),
            new XYZ(0, 1, 0),
            lighting,
            material);

        Assert.True(color.B > 200);
        Assert.True(color.R < 20);
        Assert.True(color.G < 20);
    }

    [Fact]
    public void ComputeLitColor_UsesSpecularFactor()
    {
        var lighting = new RenderLightingSettings(
            lights: new[]
            {
                new RenderLight(new XYZ(0, 0, 1), 1f, new RenderColor(255, 255, 255, 255))
            },
            ambientIntensity: 0f,
            ambientColor: new RenderColor(255, 255, 255, 255));

        var material = new RenderMaterial(
            diffuseColor: new RenderColor(0, 0, 0, 255),
            ambientColor: new RenderColor(0, 0, 0, 255),
            specularColor: new RenderColor(255, 0, 0, 255),
            diffuseFactor: 0f,
            ambientFactor: 0f,
            specularFactor: 1f,
            glossiness: 1f,
            alpha: 255);

        var color = RenderLightingUtils.ComputeLitColor(
            new XYZ(0, 0, 0),
            new XYZ(1, 0, 0),
            new XYZ(0, 1, 0),
            lighting,
            material);

        Assert.True(color.R > 200);
        Assert.True(color.G < 20);
        Assert.True(color.B < 20);
    }

    [Fact]
    public void ComputeLitColor_UsesSpotLightCone()
    {
        var insideLighting = new RenderLightingSettings(
            lights: new[]
            {
                RenderLight.Spot(
                    position: new XYZ(0, 0, 10),
                    direction: new XYZ(0, 0, -1),
                    intensity: 1f,
                    innerConeAngle: 0.1f,
                    outerConeAngle: 0.4f,
                    color: new RenderColor(255, 255, 255, 255),
                    range: 100f)
            },
            ambientIntensity: 0f,
            ambientColor: new RenderColor(255, 255, 255, 255));

        var outsideLighting = new RenderLightingSettings(
            lights: new[]
            {
                RenderLight.Spot(
                    position: new XYZ(0, 0, 10),
                    direction: new XYZ(1, 0, 0),
                    intensity: 1f,
                    innerConeAngle: 0.1f,
                    outerConeAngle: 0.4f,
                    color: new RenderColor(255, 255, 255, 255),
                    range: 100f)
            },
            ambientIntensity: 0f,
            ambientColor: new RenderColor(255, 255, 255, 255));

        var material = RenderMaterial.FromColor(new RenderColor(255, 255, 255, 255));

        var points = new[]
        {
            new XYZ(0, 0, 0),
            new XYZ(1, 0, 0),
            new XYZ(0, 1, 0)
        };

        var inside = RenderLightingUtils.ComputeLitColor(
            points[0],
            points[1],
            points[2],
            insideLighting,
            material);
        var outside = RenderLightingUtils.ComputeLitColor(
            points[0],
            points[1],
            points[2],
            outsideLighting,
            material);

        var insideLuminance = inside.R + inside.G + inside.B;
        var outsideLuminance = outside.R + outside.G + outside.B;
        Assert.True(insideLuminance > outsideLuminance);
    }

    [Fact]
    public void ComputeLitColor_UsesPointLightRange()
    {
        var nearLighting = new RenderLightingSettings(
            lights: new[]
            {
                RenderLight.Point(
                    position: new XYZ(0, 0, 5),
                    intensity: 1f,
                    color: new RenderColor(255, 255, 255, 255),
                    range: 10f)
            },
            ambientIntensity: 0f,
            ambientColor: new RenderColor(255, 255, 255, 255));

        var farLighting = new RenderLightingSettings(
            lights: new[]
            {
                RenderLight.Point(
                    position: new XYZ(0, 0, 5),
                    intensity: 1f,
                    color: new RenderColor(255, 255, 255, 255),
                    range: 4f)
            },
            ambientIntensity: 0f,
            ambientColor: new RenderColor(255, 255, 255, 255));

        var material = RenderMaterial.FromColor(new RenderColor(255, 255, 255, 255));

        var points = new[]
        {
            new XYZ(0, 0, 0),
            new XYZ(1, 0, 0),
            new XYZ(0, 1, 0)
        };

        var near = RenderLightingUtils.ComputeLitColor(
            points[0],
            points[1],
            points[2],
            nearLighting,
            material);
        var far = RenderLightingUtils.ComputeLitColor(
            points[0],
            points[1],
            points[2],
            farLighting,
            material);

        var nearLuminance = near.R + near.G + near.B;
        var farLuminance = far.R + far.G + far.B;
        Assert.True(nearLuminance > farLuminance);
    }
}
