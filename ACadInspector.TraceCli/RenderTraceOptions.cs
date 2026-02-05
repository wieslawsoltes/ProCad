using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ACadInspector.Rendering;

namespace ACadInspector.TraceCli;

internal sealed record RenderTraceOptions(
    IReadOnlyList<string> InputFiles,
    string OutputDirectory,
    int Width,
    int Height,
    float Padding,
    int WarmupIterations,
    int TimedIterations,
    bool RebuildSceneEachIteration,
    bool LoadDocumentEachIteration,
    bool SaveImage,
    RenderVisualStyle VisualStyle,
    bool ShowHelp)
{
    public static bool TryParse(
        string[] args,
        out RenderTraceOptions? options,
        out string? error)
    {
        options = null;
        error = null;

        if (args is null)
        {
            error = "Command-line arguments cannot be null.";
            return false;
        }

        List<string> inputFiles = new();
        string outputDirectory = Path.Combine(Environment.CurrentDirectory, "trace-output");
        int width = 1920;
        int height = 1080;
        float padding = 24f;
        int warmupIterations = 1;
        int timedIterations = 8;
        bool rebuildSceneEachIteration = false;
        bool loadDocumentEachIteration = false;
        bool saveImage = true;
        RenderVisualStyle visualStyle = RenderVisualStyle.Wireframe;
        bool showHelp = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                case "-i":
                case "--input":
                    if (!TryReadNext(args, ref i, arg, out string? inputPath, out error))
                    {
                        return false;
                    }

                    inputFiles.Add(inputPath!);
                    break;
                case "-o":
                case "--output-dir":
                    if (!TryReadNext(args, ref i, arg, out string? outputPath, out error))
                    {
                        return false;
                    }
                    outputDirectory = outputPath!;
                    break;
                case "--width":
                    if (!TryReadPositiveInt(args, ref i, arg, out width, out error))
                    {
                        return false;
                    }
                    break;
                case "--height":
                    if (!TryReadPositiveInt(args, ref i, arg, out height, out error))
                    {
                        return false;
                    }
                    break;
                case "--padding":
                    if (!TryReadNonNegativeFloat(args, ref i, arg, out padding, out error))
                    {
                        return false;
                    }
                    break;
                case "--warmup":
                    if (!TryReadNonNegativeInt(args, ref i, arg, out warmupIterations, out error))
                    {
                        return false;
                    }
                    break;
                case "--iterations":
                    if (!TryReadPositiveInt(args, ref i, arg, out timedIterations, out error))
                    {
                        return false;
                    }
                    break;
                case "--rebuild-each-iteration":
                    rebuildSceneEachIteration = true;
                    break;
                case "--load-each-iteration":
                    loadDocumentEachIteration = true;
                    rebuildSceneEachIteration = true;
                    break;
                case "--no-image":
                    saveImage = false;
                    break;
                case "--visual-style":
                    if (!TryReadNext(args, ref i, arg, out string? styleValue, out error))
                    {
                        return false;
                    }

                    if (!TryParseVisualStyle(styleValue!, out visualStyle))
                    {
                        error = $"Unsupported visual style '{styleValue}'. Expected: wireframe, hiddenline, shaded.";
                        return false;
                    }
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        error = $"Unknown argument '{arg}'.";
                        return false;
                    }

                    inputFiles.Add(arg);
                    break;
            }
        }

        if (!showHelp && inputFiles.Count == 0)
        {
            error = "At least one CAD input file is required.";
            return false;
        }

        options = new RenderTraceOptions(
            inputFiles,
            Path.GetFullPath(outputDirectory ?? "trace-output"),
            width,
            height,
            padding,
            warmupIterations,
            timedIterations,
            rebuildSceneEachIteration,
            loadDocumentEachIteration,
            saveImage,
            visualStyle,
            showHelp);
        return true;
    }

    public static string GetUsage()
    {
        return string.Join(
            Environment.NewLine,
            "Usage:",
            "  dotnet run --project ACadInspector.TraceCli -- [options] <cad-file> [<cad-file> ...]",
            string.Empty,
            "Options:",
            "  -i, --input <path>               Input CAD file (repeatable).",
            "  -o, --output-dir <path>          Output directory for PNG files (default: ./trace-output).",
            "      --width <px>                 Output image width in pixels (default: 1920).",
            "      --height <px>                Output image height in pixels (default: 1080).",
            "      --padding <px>               View padding in pixels (default: 24).",
            "      --warmup <n>                 Warmup render iterations (default: 1).",
            "      --iterations <n>             Timed render iterations (default: 8).",
            "      --rebuild-each-iteration     Rebuild render scene for every timed iteration.",
            "      --load-each-iteration        Reload document and rebuild scene for every timed iteration.",
            "      --visual-style <name>        wireframe | hiddenline | shaded (default: wireframe).",
            "      --no-image                   Skip PNG write.",
            "  -h, --help                       Show help.");
    }

    private static bool TryReadNext(
        string[] args,
        ref int index,
        string optionName,
        out string? value,
        out string? error)
    {
        value = null;
        error = null;
        int valueIndex = index + 1;
        if (valueIndex >= args.Length)
        {
            error = $"Option '{optionName}' requires a value.";
            return false;
        }

        value = args[valueIndex];
        index = valueIndex;
        return true;
    }

    private static bool TryReadPositiveInt(
        string[] args,
        ref int index,
        string optionName,
        out int value,
        out string? error)
    {
        value = 0;
        if (!TryReadNext(args, ref index, optionName, out string? text, out error))
        {
            return false;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value <= 0)
        {
            error = $"Option '{optionName}' requires a positive integer value.";
            return false;
        }

        return true;
    }

    private static bool TryReadNonNegativeInt(
        string[] args,
        ref int index,
        string optionName,
        out int value,
        out string? error)
    {
        value = 0;
        if (!TryReadNext(args, ref index, optionName, out string? text, out error))
        {
            return false;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0)
        {
            error = $"Option '{optionName}' requires a non-negative integer value.";
            return false;
        }

        return true;
    }

    private static bool TryReadNonNegativeFloat(
        string[] args,
        ref int index,
        string optionName,
        out float value,
        out string? error)
    {
        value = 0;
        if (!TryReadNext(args, ref index, optionName, out string? text, out error))
        {
            return false;
        }

        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 0f)
        {
            error = $"Option '{optionName}' requires a non-negative floating point value.";
            return false;
        }

        return true;
    }

    private static bool TryParseVisualStyle(string styleText, out RenderVisualStyle style)
    {
        switch (styleText.Trim().ToLowerInvariant())
        {
            case "wireframe":
                style = RenderVisualStyle.Wireframe;
                return true;
            case "hidden":
            case "hiddenline":
            case "hidden-line":
                style = RenderVisualStyle.HiddenLine;
                return true;
            case "shaded":
                style = RenderVisualStyle.Shaded;
                return true;
            default:
                style = RenderVisualStyle.Wireframe;
                return false;
        }
    }

}
