using ProCad.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderMaterialResolverTests
{
    [Fact]
    public void ResolveEntityMaterial_UsesMaterialOverrides()
    {
        var line = new Line
        {
            Color = new Color(10, 20, 30),
            Transparency = new Transparency(50)
        };

        var material = new Material("Test")
        {
            DiffuseColor = new Color(200, 100, 50),
            DiffuseColorMethod = ColorMethod.Override,
            AmbientColor = new Color(30, 40, 50),
            AmbientColorMethod = ColorMethod.Override,
            SpecularColor = new Color(120, 130, 140),
            SpecularColorMethod = ColorMethod.Override,
            DiffuseColorFactor = 0.6,
            AmbientColorFactor = 0.2,
            SpecularColorFactor = 0.4,
            SpecularGlossFactor = 0.7,
            Opacity = 0.5
        };

        line.Material = material;

        var resolver = new DefaultRenderStyleResolver();
        var settings = new CadRenderSceneSettings();
        var resolved = resolver.ResolveEntityMaterial(line, settings);

        Assert.Equal((byte)200, resolved.DiffuseColor.R);
        Assert.Equal((byte)30, resolved.AmbientColor.R);
        Assert.Equal((byte)120, resolved.SpecularColor.R);
        Assert.InRange(resolved.DiffuseFactor, 0.59f, 0.61f);
        Assert.InRange(resolved.AmbientFactor, 0.19f, 0.21f);
        Assert.InRange(resolved.SpecularFactor, 0.39f, 0.41f);
        Assert.InRange(resolved.Glossiness, 0.69f, 0.71f);

        var expectedAlpha = (byte)System.Math.Clamp((int)System.Math.Round(127 * 0.5), 0, 255);
        Assert.Equal(expectedAlpha, resolved.Alpha);
    }

    [Fact]
    public void ResolveEntityMaterial_FallsBackToEntityColor()
    {
        var line = new Line
        {
            Color = new Color(40, 50, 60),
            Transparency = new Transparency(0)
        };

        var resolver = new DefaultRenderStyleResolver();
        var settings = new CadRenderSceneSettings();
        var resolved = resolver.ResolveEntityMaterial(line, settings);

        Assert.Equal((byte)40, resolved.DiffuseColor.R);
        Assert.Equal((byte)50, resolved.DiffuseColor.G);
        Assert.Equal((byte)60, resolved.DiffuseColor.B);
        Assert.Equal((byte)255, resolved.Alpha);
    }
}
