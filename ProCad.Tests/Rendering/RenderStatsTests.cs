using System.IO;
using System.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using ProCad.Core;
using ProCad.Rendering;
using ProCad.Services;
using ProCad.ViewModels;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderStatsTests
{
    [Fact]
    public void BuildScene_TracksStatsAndBudgets()
    {
        var document = new ACadSharp.CadDocument();
        document.Entities.Add(new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(3, 0, 0) });
        document.Entities.Add(new Line { StartPoint = new XYZ(0, 1, 0), EndPoint = new XYZ(3, 1, 0) });

        var settings = new CadRenderSceneSettings
        {
            PerformanceBudget = new RenderPerformanceBudget { MaxPrimitives = 1 }
        };

        var scene = CreateSceneBuilder().Build(document, settings);

        Assert.True(scene.Stats.PrimitiveCount >= 2);
        Assert.NotEmpty(scene.Stats.BudgetViolations);

        var json = RenderStatsExporter.ToJson(scene.Stats);
        Assert.Contains("\"PrimitiveCount\"", json);
    }

    [Fact]
    public async Task ExportStatsCommand_WritesJson()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"acad-render-stats-{Path.GetRandomFileName()}.json");
        try
        {
            var service = new FileRenderStatsExportService(tempPath);
            var scene = RenderSceneSamples.CreateBaselineScene();
            var document = new ACadSharp.CadDocument();
            var viewModel = new CadRenderViewModel(
                document,
                scene,
                CreateSceneBuilder(),
                new CadRenderSceneSettings(),
                CadRenderLayoutSelection.ModelSpace,
                documentPath: null,
                dynamicBlockOverrides: null,
                dynamicBlockOverrideChanges: null,
                new CadSelectionService(),
                new CadSelectionFocusService(),
                service,
                "render-stats.json");

            await viewModel.ExportStatsCommand.Execute().ToTask();

            var json = await File.ReadAllTextAsync(tempPath, CancellationToken.None);
            Assert.Contains("\"PrimitiveCount\"", json);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new LineRenderHandler(),
            new FallbackRenderHandler()
        };

        return new CadRenderSceneBuilder(
            new RenderEntityDispatcher(handlers),
            new DefaultRenderStyleResolver(),
            new DefaultRenderLinePatternResolver(),
            new DefaultRenderShapeResolver(),
            new DefaultRenderTextShaper(),
            new DefaultRenderEntityVisibilityResolver(),
            new DefaultRenderGeometrySampler(),
            new DefaultRenderEntityOrderResolver(),
            new RenderCacheStampProvider());
    }

    private sealed class FileRenderStatsExportService : IRenderStatsExportService
    {
        private readonly string _path;

        public FileRenderStatsExportService(string path)
        {
            _path = path;
        }

        public Task<RenderStatsExportResult?> SaveStatsAsync(string? suggestedFileName, CancellationToken cancellationToken)
        {
            var result = new RenderStatsExportResult(
                _path,
                Path.GetFileName(_path),
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    Stream stream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.None);
                    return new ValueTask<Stream>(stream);
                });

            return Task.FromResult<RenderStatsExportResult?>(result);
        }
    }
}
