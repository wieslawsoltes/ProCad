using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using ProCad.Controls;
using ProCad.Rendering;
using Xunit;
using Xunit.Sdk;

namespace ProCad.Tests.Rendering;

internal static class RenderSnapshotHarness
{
    private const string UpdateGoldenEnvVar = "PROCAD_UPDATE_GOLDEN";

    public static Bitmap Capture(RenderScene scene, PixelSize size)
    {
        var control = new CadRenderControl
        {
            Scene = scene,
            ShowGrid = false,
            ShowAxes = false,
            FitOnSceneChange = true,
            Width = size.Width,
            Height = size.Height
        };

        var window = new Window
        {
            Width = size.Width,
            Height = size.Height,
            Content = control
        };

        window.Show();
        var rect = new Rect(0, 0, size.Width, size.Height);
        window.Measure(rect.Size);
        window.Arrange(rect);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        var frame = window.CaptureRenderedFrame();
        window.Close();

        return frame ?? throw new InvalidOperationException("No rendered frame captured.");
    }

    public static void AssertMatches(Bitmap actual, string baselinePath)
    {
        if (ShouldUpdateBaseline())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath) ?? ".");
            actual.Save(baselinePath);
            return;
        }

        Assert.True(File.Exists(baselinePath),
            $"Missing baseline image: {baselinePath}. Set {UpdateGoldenEnvVar}=1 to generate.");

        using var baseline = new Bitmap(baselinePath);
        if (baseline.PixelSize != actual.PixelSize)
        {
            throw new XunitException($"Baseline size {baseline.PixelSize} does not match actual {actual.PixelSize}.");
        }

        var baselineBytes = File.ReadAllBytes(baselinePath);
        var actualBytes = EncodePng(actual);
        if (!baselineBytes.AsSpan().SequenceEqual(actualBytes))
        {
            var actualPath = Path.ChangeExtension(baselinePath, ".actual.png");
            actual.Save(actualPath);
            throw new XunitException($"Render output differs from baseline. Actual saved to {actualPath}.");
        }
    }

    public static string GetBaselinePath(string fileName)
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Render", fileName);
        if (ShouldUpdateBaseline())
        {
            return Path.Combine(GetProjectRoot(), "Assets", "Render", fileName);
        }

        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        return Path.Combine(GetProjectRoot(), "Assets", "Render", fileName);
    }

    private static string GetProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    }

    private static bool ShouldUpdateBaseline()
    {
        var value = Environment.GetEnvironmentVariable(UpdateGoldenEnvVar);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }
}
