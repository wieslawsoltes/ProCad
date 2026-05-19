using System;
using System.IO;
using ProCad.Rendering;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class ShxFontResolverTests
{
    [Fact]
    public void TryGetFont_LoadsAndCachesFont()
    {
        var root = FindRepositoryRoot();
        var supportPath = Path.Combine(root, "external", "ACadSharp", "samples");
        var settings = new CadRenderSceneSettings { SupportPaths = new[] { supportPath } };
        var resolver = new DefaultShxFontResolver();

        var loaded = resolver.TryGetFont("test_shape.shx", settings, out var font);
        var loadedAgain = resolver.TryGetFont("test_shape.shx", settings, out var font2);

        Assert.True(loaded);
        Assert.True(loadedAgain);
        Assert.NotNull(font);
        Assert.Same(font, font2);
        Assert.True(font!.TryGetGlyph(1, out var glyph));
        Assert.NotEmpty(glyph.Geometry.Contours);
    }

    [Fact]
    public void ShxRenderTextShaper_UsesGlyphMetrics()
    {
        var root = FindRepositoryRoot();
        var supportPath = Path.Combine(root, "external", "ACadSharp", "samples");
        var settings = new CadRenderSceneSettings { SupportPaths = new[] { supportPath } };
        var resolver = new DefaultShxFontResolver();
        var fallback = new DefaultRenderTextShaper();
        var shaper = new ShxRenderTextShaper(resolver, fallback);

        var style = new TextStyle("Shx") { Filename = "test_shape.shx" };
        var text = new TextEntity
        {
            Value = "\u0001\u0001",
            Height = 2.0,
            Style = style
        };

        var layout = shaper.Shape(text, settings);

        Assert.True(layout.Width > 0f);
        Assert.InRange(layout.Height, 1.9f, 2.1f);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProCad.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
