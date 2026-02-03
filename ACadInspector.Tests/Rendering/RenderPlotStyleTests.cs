using System;
using System.IO;
using ACadInspector.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderPlotStyleTests
{
    [Fact]
    public void ResolveEntityColor_UsesCtbPlotStyle()
    {
        var document = new CadDocument();
        document.Header.PlotStyleMode = 0;

        var line = new Line
        {
            Color = new ACadSharp.Color(1)
        };

        string? path = null;
        try
        {
            path = WritePlotStyleFile(".ctb", "plot_style {\nindex=1\ncolor=0,255,0\nlineweight=0.5\n}\n");
            var table = RenderPlotStyleTable.TryLoad(path);
            Assert.NotNull(table);

            var settings = new CadRenderSceneSettings { PlotStyleTable = table };
            var context = CreateContext(document, settings);

            var color = context.ResolveEntityColor(line);
            var weight = context.ResolveLineWeight(line);

            Assert.Equal(0, color.R);
            Assert.Equal(255, color.G);
            Assert.Equal(0, color.B);
            Assert.Equal(0.5f, weight, 3);
        }
        finally
        {
            if (path is not null && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ResolveEntityColor_UsesStbDefaultPlotStyle()
    {
        var document = new CadDocument();
        document.Header.PlotStyleMode = 1;

        var plotStyleDict = new CadDictionaryWithDefault(CadDictionary.AcadPlotStyleName, new PlotSettings("MyStyle"));
        document.RootDictionary.Add(CadDictionary.AcadPlotStyleName, plotStyleDict);

        var line = new Line
        {
            Color = new ACadSharp.Color(3)
        };

        string? path = null;
        try
        {
            path = WritePlotStyleFile(".stb", "plot_style {\nname=\"MyStyle\"\ncolor=128,0,128\nlineweight=0.25\n}\n");
            var table = RenderPlotStyleTable.TryLoad(path);
            Assert.NotNull(table);

            var settings = new CadRenderSceneSettings { PlotStyleTable = table };
            var context = CreateContext(document, settings);

            var color = context.ResolveEntityColor(line);
            var weight = context.ResolveLineWeight(line);

            Assert.Equal(128, color.R);
            Assert.Equal(0, color.G);
            Assert.Equal(128, color.B);
            Assert.Equal(0.25f, weight, 3);
        }
        finally
        {
            if (path is not null && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static RenderBuildContext CreateContext(CadDocument document, CadRenderSceneSettings settings)
    {
        return new RenderBuildContext(
            document,
            settings,
            new DefaultRenderStyleResolver(),
            new DefaultRenderLinePatternResolver(),
            new DefaultRenderShapeResolver(),
            new DefaultRenderTextShaper(),
            new DefaultRenderEntityVisibilityResolver(),
            new DefaultRenderGeometrySampler(),
            new DefaultRenderEntityOrderResolver(),
            new RenderEntityDispatcher(Array.Empty<IRenderEntityHandler>()),
            new RenderDiagnostics(),
            new RenderStatsAccumulator());
    }

    private static string WritePlotStyleFile(string extension, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content);
        return path;
    }
}
