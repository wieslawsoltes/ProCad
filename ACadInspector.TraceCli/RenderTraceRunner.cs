using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using ACadInspector.Core;
using ACadInspector.IO;
using ACadInspector.Rendering;
using ACadSharp;
using Avalonia;
using SkiaSharp;

namespace ACadInspector.TraceCli;

internal sealed class RenderTraceRunner
{
    private readonly RenderTraceOptions _options;
    private readonly ICadDocumentService _documentService;
    private readonly ICadRenderSceneBuilder _sceneBuilder;
    private readonly CadSkiaRenderService _renderer;
    private readonly CadRenderSceneSettings _baseSettings;

    public RenderTraceRunner(RenderTraceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _documentService = new AcAdSharpDocumentService();
        _sceneBuilder = BuildSceneBuilder(_documentService);
        _renderer = new CadSkiaRenderService();
        _baseSettings = new CadRenderSceneSettings
        {
            VisualStyle = options.VisualStyle,
            HiddenLineSettings = new RenderHiddenLineSettings(
                RenderObscuredLineType.Off,
                RenderHiddenLineColorMode.Entity,
                RenderColor.DefaultForeground)
        };
    }

    public RenderTraceResult Run(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path cannot be empty.", nameof(inputPath));
        }

        string fullInputPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullInputPath))
        {
            throw new FileNotFoundException("Input CAD file was not found.", fullInputPath);
        }

        Directory.CreateDirectory(_options.OutputDirectory);

        string? outputImagePath = _options.SaveImage
            ? BuildOutputImagePath(fullInputPath, _options.OutputDirectory, _options.VisualStyle, _options.Width, _options.Height)
            : null;

        CadDocument? document = null;
        RenderScene? scene = null;
        double initialLoadMs = 0d;
        double initialBuildMs = 0d;

        if (!_options.LoadDocumentEachIteration)
        {
            TimeSpan elapsed = MeasurePhase("load", fullInputPath, iteration: -1, () => document = LoadDocument(fullInputPath));
            initialLoadMs = elapsed.TotalMilliseconds;
        }

        if (!_options.RebuildSceneEachIteration)
        {
            if (document is null)
            {
                document = LoadDocument(fullInputPath);
            }

            TimeSpan elapsed = MeasurePhase("build-scene", fullInputPath, iteration: -1, () => scene = BuildScene(document, fullInputPath));
            initialBuildMs = elapsed.TotalMilliseconds;
        }

        for (int i = 0; i < _options.WarmupIterations; i++)
        {
            ExecuteIteration(
                fullInputPath,
                iteration: i,
                saveImage: false,
                ref document,
                ref scene,
                out _,
                out _,
                out _,
                out _);
        }

        List<double> loadSamples = new(_options.TimedIterations);
        List<double> buildSamples = new(_options.TimedIterations);
        List<double> renderSamples = new(_options.TimedIterations);
        List<double> encodeSamples = new(_options.TimedIterations);

        for (int i = 0; i < _options.TimedIterations; i++)
        {
            bool saveImage = outputImagePath is not null && i == _options.TimedIterations - 1;
            ExecuteIteration(
                fullInputPath,
                iteration: i,
                saveImage: saveImage,
                ref document,
                ref scene,
                out double loadMs,
                out double buildMs,
                out double renderMs,
                out double encodeMs);

            if (loadMs > 0d)
            {
                loadSamples.Add(loadMs);
            }

            if (buildMs > 0d)
            {
                buildSamples.Add(buildMs);
            }

            renderSamples.Add(renderMs);

            if (encodeMs > 0d)
            {
                encodeSamples.Add(encodeMs);
            }
        }

        if (scene is null)
        {
            throw new InvalidOperationException("Render scene is not available after timed iterations.");
        }

        int unsupportedEntityCount = CountUnsupported(scene.Diagnostics);
        return new RenderTraceResult(
            fullInputPath,
            outputImagePath,
            _options.VisualStyle,
            _options.Width,
            _options.Height,
            _options.WarmupIterations,
            _options.TimedIterations,
            initialLoadMs,
            initialBuildMs,
            loadSamples,
            buildSamples,
            renderSamples,
            encodeSamples,
            scene.Stats.EntityCount,
            scene.Stats.VisibleEntityCount,
            scene.Stats.LayerCount,
            scene.Stats.PrimitiveCount,
            unsupportedEntityCount,
            scene.Stats.BuildMilliseconds);
    }

    private void ExecuteIteration(
        string inputPath,
        int iteration,
        bool saveImage,
        ref CadDocument? document,
        ref RenderScene? scene,
        out double loadMs,
        out double buildMs,
        out double renderMs,
        out double encodeMs)
    {
        loadMs = 0d;
        buildMs = 0d;
        renderMs = 0d;
        encodeMs = 0d;

        if (_options.LoadDocumentEachIteration || document is null)
        {
            CadDocument? loadedDocument = null;
            TimeSpan elapsed = MeasurePhase("load", inputPath, iteration, () => loadedDocument = LoadDocument(inputPath));
            document = loadedDocument;
            loadMs = elapsed.TotalMilliseconds;
        }

        if (_options.RebuildSceneEachIteration || scene is null)
        {
            if (document is null)
            {
                throw new InvalidOperationException("Document is missing before scene build.");
            }

            CadDocument currentDocument = document;
            RenderScene? builtScene = null;
            TimeSpan elapsed = MeasurePhase("build-scene", inputPath, iteration, () => builtScene = BuildScene(currentDocument, inputPath));
            scene = builtScene;
            buildMs = elapsed.TotalMilliseconds;
        }

        if (scene is null)
        {
            throw new InvalidOperationException("Scene is missing before render.");
        }

        string? outputPath = saveImage ? BuildOutputImagePath(inputPath, _options.OutputDirectory, _options.VisualStyle, _options.Width, _options.Height) : null;
        RenderFrameTiming frame = RenderFrame(scene, inputPath, iteration, outputPath);
        renderMs = frame.RenderMilliseconds;
        encodeMs = frame.EncodeMilliseconds;
    }

    private CadDocument LoadDocument(string inputPath)
    {
        CadReadOptions options = new();
        return _documentService.Load(inputPath, options);
    }

    private RenderScene BuildScene(CadDocument document, string inputPath)
    {
        CadRenderLayoutSelection selection = CadRenderSettingsBuilder.ResolveDefaultLayout(document);
        CadRenderSceneSettings settings = CadRenderSettingsBuilder.Build(document, inputPath, _baseSettings, selection);
        return _sceneBuilder.Build(document, settings);
    }

    private RenderFrameTiming RenderFrame(RenderScene scene, string inputPath, int iteration, string? outputPath)
    {
        CadRenderStateSnapshot snapshot = BuildSnapshot(scene, _options.Width, _options.Height, _options.Padding);
        SKImageInfo imageInfo = new(_options.Width, _options.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKSurface? surface = SKSurface.Create(imageInfo);
        if (surface is null)
        {
            throw new InvalidOperationException("Unable to allocate Skia surface.");
        }

        TimeSpan renderElapsed = MeasurePhase(
            "render",
            inputPath,
            iteration,
            () => _renderer.Render(surface.Canvas, new Size(_options.Width, _options.Height), snapshot, isInteractive: false));

        double encodeMilliseconds = 0d;
        if (outputPath is not null)
        {
            TimeSpan encodeElapsed = MeasurePhase("encode-png", inputPath, iteration, () => SavePng(surface, outputPath));
            encodeMilliseconds = encodeElapsed.TotalMilliseconds;
        }

        return new RenderFrameTiming(renderElapsed.TotalMilliseconds, encodeMilliseconds);
    }

    private static void SavePng(SKSurface surface, string outputPath)
    {
        using SKImage image = surface.Snapshot();
        using SKData? encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null)
        {
            throw new InvalidOperationException("Skia PNG encoding returned no data.");
        }

        File.WriteAllBytes(outputPath, encoded.ToArray());
    }

    private static CadRenderStateSnapshot BuildSnapshot(RenderScene scene, int width, int height, float padding)
    {
        Matrix3x2 viewTransform = BuildViewTransform(scene.Bounds, width, height, padding, out double baseScale);
        return new CadRenderStateSnapshot(
            scene,
            showGrid: false,
            showAxes: false,
            enableInteractionOptimization: false,
            layerVisibilityOverrides: null,
            entityTypeVisibilityOverrides: null,
            zoom: 1.0,
            minPixelThickness: 0.6,
            baseScale: baseScale,
            viewTransform: viewTransform,
            showDebugOverlay: false,
            hoverBounds: null,
            selectionBounds: null,
            hoverAnnotation: null,
            selectionAnnotation: null,
            debugBvhBounds: null);
    }

    private static Matrix3x2 BuildViewTransform(
        RenderBounds bounds,
        int width,
        int height,
        float padding,
        out double baseScale)
    {
        Vector2 sceneSize = bounds.Size;
        if (sceneSize.X <= 0f || sceneSize.Y <= 0f)
        {
            baseScale = 1.0;
            return Matrix3x2.Identity;
        }

        float availableWidth = MathF.Max(1f, width - (padding * 2f));
        float availableHeight = MathF.Max(1f, height - (padding * 2f));
        float scaleX = availableWidth / sceneSize.X;
        float scaleY = availableHeight / sceneSize.Y;
        float scale = MathF.Max(0.00001f, MathF.Min(scaleX, scaleY));
        baseScale = scale;

        Vector2 sceneCenter = (bounds.Min + bounds.Max) * 0.5f;
        Vector2 viewportCenter = new(width * 0.5f, height * 0.5f);

        return Matrix3x2.CreateTranslation(-sceneCenter)
            * Matrix3x2.CreateScale(scale, -scale)
            * Matrix3x2.CreateTranslation(viewportCenter);
    }

    private static TimeSpan MeasurePhase(string phaseName, string inputPath, int iteration, Action action)
    {
        RenderTraceEventSource.Log.PhaseStart(phaseName, inputPath, iteration);
        Stopwatch stopwatch = Stopwatch.StartNew();
        int success = 0;
        try
        {
            action();
            success = 1;
        }
        finally
        {
            stopwatch.Stop();
            RenderTraceEventSource.Log.PhaseStop(
                phaseName,
                inputPath,
                iteration,
                stopwatch.Elapsed.TotalMilliseconds,
                success);
        }

        return stopwatch.Elapsed;
    }

    private static string BuildOutputImagePath(string inputPath, string outputDirectory, RenderVisualStyle style, int width, int height)
    {
        string baseName = Path.GetFileNameWithoutExtension(inputPath);
        string styleName = style.ToString().ToLowerInvariant();
        string fileName = $"{baseName}-{styleName}-{width}x{height}.png";
        return Path.Combine(outputDirectory, fileName);
    }

    private static int CountUnsupported(RenderDiagnostics diagnostics)
    {
        int total = 0;
        foreach (KeyValuePair<string, RenderUnsupportedEntityInfo> pair in diagnostics.Unsupported)
        {
            total += pair.Value.Count;
        }

        return total;
    }

    private static ICadRenderSceneBuilder BuildSceneBuilder(ICadDocumentService documentService)
    {
        IRenderCache cache = new RenderCache();
        IRenderCacheStampProvider stampProvider = new RenderCacheStampProvider();
        IRenderTextShaper fallbackTextShaper = new DefaultRenderTextShaper();
        IShxFontResolver shxFontResolver = new DefaultShxFontResolver();
        IRenderTextShaper textShaper = new CachedRenderTextShaper(
            new ShxRenderTextShaper(shxFontResolver, fallbackTextShaper),
            cache);
        IRenderLinePatternResolver linePatternResolver = new DefaultRenderLinePatternResolver(textShaper);
        IRenderGeometrySampler geometrySampler = new CachedRenderGeometrySampler(
            new DefaultRenderGeometrySampler(),
            cache,
            stampProvider);
        IRenderXRefResolver xrefResolver = new DefaultRenderXRefResolver(documentService);

        IRenderEntityHandler[] handlers =
        {
            new TableRenderHandler(),
            new InsertRenderHandler(xrefResolver),
            new DimensionRenderHandler(),
            new LeaderRenderHandler(),
            new MultiLeaderRenderHandler(),
            new MLineRenderHandler(),
            new LineRenderHandler(),
            new RayRenderHandler(),
            new XLineRenderHandler(),
            new PointRenderHandler(),
            new ArcRenderHandler(),
            new CircleRenderHandler(),
            new EllipseRenderHandler(),
            new SplineRenderHandler(),
            new PolylineRenderHandler(),
            new PolygonMeshRenderHandler(),
            new ModelerGeometryRenderHandler(),
            new Face3DRenderHandler(),
            new MeshRenderHandler(),
            new PolyfaceMeshRenderHandler(),
            new SolidRenderHandler(),
            new HatchRenderHandler(),
            new WipeoutRenderHandler(),
            new RasterImageRenderHandler(),
            new PdfUnderlayRenderHandler(),
            new ViewportRenderHandler(),
            new ShapeRenderHandler(),
            new TextEntityRenderHandler(),
            new MTextRenderHandler(),
            new Ole2FrameRenderHandler(),
            new ToleranceRenderHandler(),
            new ProxyEntityRenderHandler(),
            new FallbackRenderHandler()
        };

        IRenderEntityDispatcher dispatcher = new RenderEntityDispatcher(handlers);
        return new CadRenderSceneBuilder(
            dispatcher,
            new DefaultRenderStyleResolver(),
            linePatternResolver,
            new DefaultRenderShapeResolver(),
            textShaper,
            new DefaultRenderEntityVisibilityResolver(),
            geometrySampler,
            new DefaultRenderEntityOrderResolver(),
            stampProvider);
    }
}

internal sealed record RenderTraceResult(
    string InputPath,
    string? OutputImagePath,
    RenderVisualStyle VisualStyle,
    int Width,
    int Height,
    int WarmupIterations,
    int TimedIterations,
    double InitialLoadMilliseconds,
    double InitialBuildMilliseconds,
    IReadOnlyList<double> LoadSamplesMilliseconds,
    IReadOnlyList<double> BuildSamplesMilliseconds,
    IReadOnlyList<double> RenderSamplesMilliseconds,
    IReadOnlyList<double> EncodeSamplesMilliseconds,
    int EntityCount,
    int VisibleEntityCount,
    int LayerCount,
    int PrimitiveCount,
    int UnsupportedEntityCount,
    double SceneBuildMilliseconds);

internal static class RenderTraceReporter
{
    public static void Write(RenderTraceResult result)
    {
        Console.WriteLine($"Input: {result.InputPath}");
        Console.WriteLine($"Image: {(result.OutputImagePath ?? "(not written)")}");
        Console.WriteLine($"VisualStyle: {result.VisualStyle}");
        Console.WriteLine($"Resolution: {result.Width}x{result.Height}");
        Console.WriteLine($"WarmupIterations: {result.WarmupIterations}");
        Console.WriteLine($"TimedIterations: {result.TimedIterations}");
        Console.WriteLine(
            $"Scene: entities={result.VisibleEntityCount}/{result.EntityCount}, layers={result.LayerCount}, primitives={result.PrimitiveCount}, unsupported={result.UnsupportedEntityCount}");
        Console.WriteLine($"SceneBuilderStats.BuildMilliseconds: {result.SceneBuildMilliseconds:F2} ms");

        if (result.InitialLoadMilliseconds > 0d)
        {
            Console.WriteLine($"InitialLoad: {result.InitialLoadMilliseconds:F2} ms");
        }

        if (result.InitialBuildMilliseconds > 0d)
        {
            Console.WriteLine($"InitialBuild: {result.InitialBuildMilliseconds:F2} ms");
        }

        WriteStats("Load", result.LoadSamplesMilliseconds);
        WriteStats("BuildScene", result.BuildSamplesMilliseconds);
        WriteStats("Render", result.RenderSamplesMilliseconds);
        WriteStats("EncodePng", result.EncodeSamplesMilliseconds);
        Console.WriteLine();
    }

    private static void WriteStats(string name, IReadOnlyList<double> samples)
    {
        if (samples.Count == 0)
        {
            return;
        }

        SampleStatistics stats = SampleStatistics.Create(samples);
        Console.WriteLine(
            $"{name}: count={stats.Count}, avg={stats.Average:F2} ms, min={stats.Minimum:F2} ms, max={stats.Maximum:F2} ms, total={stats.Total:F2} ms");
    }
}

internal readonly record struct RenderFrameTiming(double RenderMilliseconds, double EncodeMilliseconds);

internal readonly record struct SampleStatistics(int Count, double Total, double Average, double Minimum, double Maximum)
{
    public static SampleStatistics Create(IReadOnlyList<double> values)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        if (values.Count == 0)
        {
            return new SampleStatistics(0, 0d, 0d, 0d, 0d);
        }

        double total = 0d;
        double minimum = double.MaxValue;
        double maximum = double.MinValue;
        for (int i = 0; i < values.Count; i++)
        {
            double value = values[i];
            total += value;
            if (value < minimum)
            {
                minimum = value;
            }

            if (value > maximum)
            {
                maximum = value;
            }
        }

        return new SampleStatistics(values.Count, total, total / values.Count, minimum, maximum);
    }
}
