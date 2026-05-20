using System;
using System.IO;
using ProCad.Rendering;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderShapeResolverTests
{
    [Fact]
    public void TryResolveShape_LoadsShxShape()
    {
        var root = FindRepositoryRoot();
        var supportPath = Path.Combine(root, "external", "ACadSharp", "samples");
        var settings = new CadRenderSceneSettings { SupportPaths = new[] { supportPath } };
        var resolver = new DefaultRenderShapeResolver();

        var resolved = resolver.TryResolveShape("test_shape.shx", 1, settings, out var geometry);

        Assert.True(resolved);
        Assert.NotNull(geometry);
        Assert.NotEmpty(geometry!.Contours);
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
