using ProCad.Rendering;
using ProCad.TraceCli;

namespace ProCad.Tests.TraceCli;

public sealed class RenderTraceOptionsTests
{
    [Fact]
    public void TryParse_ReturnsError_WhenNoInputAndNotHelp()
    {
        var success = RenderTraceOptions.TryParse(Array.Empty<string>(), out var options, out var error);

        Assert.False(success);
        Assert.Null(options);
        Assert.Contains("At least one CAD input file is required", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_ReturnsOptions_ForPositionalInputs()
    {
        var args = new[]
        {
            "--width", "1024",
            "--height", "768",
            "--iterations", "3",
            "--warmup", "0",
            "--visual-style", "hiddenline",
            "a.dxf",
            "b.dwg"
        };

        var success = RenderTraceOptions.TryParse(args, out var options, out var error);

        Assert.True(success, error);
        Assert.NotNull(options);
        Assert.Equal(2, options.InputFiles.Count);
        Assert.Equal("a.dxf", options.InputFiles[0]);
        Assert.Equal("b.dwg", options.InputFiles[1]);
        Assert.Equal(1024, options.Width);
        Assert.Equal(768, options.Height);
        Assert.Equal(3, options.TimedIterations);
        Assert.Equal(0, options.WarmupIterations);
        Assert.Equal(RenderVisualStyle.HiddenLine, options.VisualStyle);
    }

    [Fact]
    public void TryParse_EnablesRebuild_WhenLoadEachIterationIsSpecified()
    {
        var args = new[]
        {
            "--load-each-iteration",
            "file.dxf"
        };

        var success = RenderTraceOptions.TryParse(args, out var options, out var error);

        Assert.True(success, error);
        Assert.NotNull(options);
        Assert.True(options.LoadDocumentEachIteration);
        Assert.True(options.RebuildSceneEachIteration);
    }

    [Fact]
    public void TryParse_SucceedsForHelp_WithoutInput()
    {
        var success = RenderTraceOptions.TryParse(new[] { "--help" }, out var options, out var error);

        Assert.True(success, error);
        Assert.NotNull(options);
        Assert.True(options.ShowHelp);
        Assert.Empty(options.InputFiles);
    }
}
