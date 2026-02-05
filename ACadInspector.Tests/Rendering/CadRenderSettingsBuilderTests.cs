using System;
using System.IO;
using ACadInspector.Rendering;
using ACadSharp;
using ACadSharp.Objects;
using Xunit;

namespace ACadInspector.Tests.Rendering;

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
}
