using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Core;
using ACadInspector.Rendering;
using ACadInspector.Services;
using ACadInspector.Tests.Rendering;
using ACadInspector.ViewModels;
using ACadSharp;
using Xunit;

namespace ACadInspector.Tests.ViewModels;

public sealed class CadLayerToolViewModelTests
{
    [Fact]
    public void LayerTool_TracksActiveDocumentLayerList()
    {
        var context = new CadDocumentContextService();
        var tool = new CadLayerToolViewModel(context);

        var firstDocument = new CadDocument();
        var firstRender = CreateRenderViewModel(firstDocument);
        var firstViewModel = new CadDocumentViewModel(firstDocument, CadFileFormat.Dxf, path: null, "First", firstRender);
        context.Register(firstViewModel);

        Assert.Same(firstRender.LayerList, tool.LayerList);

        var secondDocument = new CadDocument();
        var secondRender = CreateRenderViewModel(secondDocument);
        var secondViewModel = new CadDocumentViewModel(secondDocument, CadFileFormat.Dwg, path: null, "Second", secondRender);
        context.Register(secondViewModel);

        Assert.Same(secondRender.LayerList, tool.LayerList);
    }

    private sealed class NullRenderStatsExportService : IRenderStatsExportService
    {
        public Task<RenderStatsExportResult?> SaveStatsAsync(string? suggestedFileName, CancellationToken cancellationToken)
        {
            return Task.FromResult<RenderStatsExportResult?>(null);
        }
    }

    private static CadRenderViewModel CreateRenderViewModel(CadDocument document)
    {
        var scene = RenderSceneSamples.CreateBaselineScene();
        return new CadRenderViewModel(
            document,
            scene,
            new NullSceneBuilder(scene),
            new CadRenderSceneSettings(),
            CadRenderLayoutSelection.ModelSpace,
            documentPath: null,
            new NullRenderStatsExportService(),
            statsFileName: null);
    }

    private sealed class NullSceneBuilder : ICadRenderSceneBuilder
    {
        private readonly RenderScene _scene;

        public NullSceneBuilder(RenderScene scene)
        {
            _scene = scene;
        }

        public RenderScene Build(CadDocument document, CadRenderSceneSettings settings)
        {
            return _scene;
        }
    }
}
