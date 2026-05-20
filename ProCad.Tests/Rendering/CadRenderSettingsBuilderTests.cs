using System;
using System.IO;
using ProCad.Rendering;
using ACadSharp;
using ACadSharp.Objects;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class CadRenderSettingsBuilderTests
{
    [Fact]
    public void Build_RespectsShowPlotStylesFlag()
    {
        var document = new CadDocument();
        var layout = document.PaperSpace.Layout;
        layout.StyleSheet = "test.ctb";
        layout.Flags = PlotFlags.PlotPlotStyles;

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var stylePath = Path.Combine(tempDir, "test.ctb");
        File.WriteAllText(stylePath, "plot_style {\nindex=1\ncolor=0,0,0\nlineweight=0.25\n}\n");

        try
        {
            var selection = new CadRenderLayoutSelection(true, layout.Name);
            var baseSettings = new CadRenderSceneSettings();
            var documentPath = Path.Combine(tempDir, "file.dwg");

            var settings = CadRenderSettingsBuilder.Build(document, documentPath, baseSettings, selection);
            Assert.Null(settings.PlotStyleTable);

            layout.Flags |= PlotFlags.ShowPlotStyles;
            var settingsWithPlotStyles = CadRenderSettingsBuilder.Build(document, documentPath, baseSettings, selection);
            Assert.NotNull(settingsWithPlotStyles.PlotStyleTable);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Build_ResolvesImageFrameVisibilityFromDictionaryVariable()
    {
        var document = new CadDocument();
        document.DictionaryVariables.AddOrUpdateVariable("IMAGEFRAME", "2");
        var baseSettings = new CadRenderSceneSettings
        {
            ImageFrameVisibility = RenderFrameVisibility.Hidden
        };

        var settings = CadRenderSettingsBuilder.Build(
            document,
            documentPath: null,
            baseSettings,
            CadRenderLayoutSelection.ModelSpace);

        Assert.Equal(RenderFrameVisibility.DisplayNotPlot, settings.ImageFrameVisibility);
    }

    [Fact]
    public void Build_UsesDisplayAndPlotImageFrameVisibilityByDefault()
    {
        var document = new CadDocument();

        var settings = CadRenderSettingsBuilder.Build(
            document,
            documentPath: null,
            new CadRenderSceneSettings(),
            CadRenderLayoutSelection.ModelSpace);

        Assert.Equal(RenderFrameVisibility.DisplayAndPlot, settings.ImageFrameVisibility);
    }

    [Fact]
    public void Build_ResolvesOleFrameVisibilityFromDictionaryVariable()
    {
        var document = new CadDocument();
        document.DictionaryVariables.AddOrUpdateVariable("OLEFRAME", "0");

        var settings = CadRenderSettingsBuilder.Build(
            document,
            documentPath: null,
            new CadRenderSceneSettings(),
            CadRenderLayoutSelection.ModelSpace);

        Assert.Equal(RenderFrameVisibility.Hidden, settings.OleFrameVisibility);
    }

    [Fact]
    public void Build_GlobalFrameVisibilityOverridesIndividualFrameVariables()
    {
        var document = new CadDocument();
        document.DictionaryVariables.AddOrUpdateVariable("FRAME", "2");
        document.DictionaryVariables.AddOrUpdateVariable("XCLIPFRAME", "0");
        document.DictionaryVariables.AddOrUpdateVariable(DictionaryVariable.WipeoutFrame, "0");
        document.DictionaryVariables.AddOrUpdateVariable("PDFFRAME", "0");
        document.DictionaryVariables.AddOrUpdateVariable("IMAGEFRAME", "0");

        var settings = CadRenderSettingsBuilder.Build(
            document,
            documentPath: null,
            new CadRenderSceneSettings(),
            CadRenderLayoutSelection.ModelSpace);

        Assert.Equal(RenderFrameVisibility.DisplayNotPlot, settings.XClipFrameVisibility);
        Assert.Equal(RenderFrameVisibility.DisplayNotPlot, settings.WipeoutFrameVisibility);
        Assert.Equal(RenderFrameVisibility.DisplayNotPlot, settings.UnderlayFrameVisibility);
        Assert.Equal(RenderFrameVisibility.DisplayNotPlot, settings.ImageFrameVisibility);
    }

    [Fact]
    public void Build_MixedGlobalFrameStateFallsBackToIndividualFrameVariables()
    {
        var document = new CadDocument();
        document.DictionaryVariables.AddOrUpdateVariable("FRAME", "3");
        document.DictionaryVariables.AddOrUpdateVariable("XCLIPFRAME", "0");
        document.DictionaryVariables.AddOrUpdateVariable(DictionaryVariable.WipeoutFrame, "1");
        document.DictionaryVariables.AddOrUpdateVariable("PDFFRAME", "2");
        document.DictionaryVariables.AddOrUpdateVariable("IMAGEFRAME", "1");

        var baseSettings = new CadRenderSceneSettings
        {
            XClipFrameVisibility = RenderFrameVisibility.DisplayAndPlot,
            WipeoutFrameVisibility = RenderFrameVisibility.Hidden,
            UnderlayFrameVisibility = RenderFrameVisibility.Hidden,
            ImageFrameVisibility = RenderFrameVisibility.Hidden
        };

        var settings = CadRenderSettingsBuilder.Build(
            document,
            documentPath: null,
            baseSettings,
            CadRenderLayoutSelection.ModelSpace);

        Assert.Equal(RenderFrameVisibility.Hidden, settings.XClipFrameVisibility);
        Assert.Equal(RenderFrameVisibility.DisplayAndPlot, settings.WipeoutFrameVisibility);
        Assert.Equal(RenderFrameVisibility.DisplayNotPlot, settings.UnderlayFrameVisibility);
        Assert.Equal(RenderFrameVisibility.DisplayAndPlot, settings.ImageFrameVisibility);
    }
}
