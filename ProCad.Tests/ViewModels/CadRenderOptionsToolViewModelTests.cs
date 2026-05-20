using System.Threading;
using System.Threading.Tasks;
using ProCad.Core;
using ProCad.Rendering;
using ProCad.Services;
using ProCad.Tests.Rendering;
using ProCad.ViewModels;
using ACadSharp;
using Xunit;

namespace ProCad.Tests.ViewModels;

public sealed class CadRenderOptionsToolViewModelTests
{
    [Fact]
    public void UpdatesActiveRender_WhenOptionsChanged()
    {
        var document = new CadDocument();
        var scene = RenderSceneSamples.CreateBaselineScene();
        var render = new CadRenderViewModel(
            document,
            scene,
            new NullSceneBuilder(scene),
            new CadRenderSceneSettings(),
            CadRenderLayoutSelection.ModelSpace,
            documentPath: null,
            dynamicBlockOverrides: null,
            dynamicBlockOverrideChanges: null,
            new CadSelectionService(),
            new CadSelectionFocusService(),
            new NullRenderStatsExportService(),
            statsFileName: null,
            allowLayoutUpdates: false);

        var context = new CadDocumentContextService();
        var viewModel = new CadRenderOptionsToolViewModel(context);
        context.Register(new CadDocumentViewModel(document, CadFileFormat.Dxf, null, "test.dxf", render));

        Assert.True(viewModel.HasActiveRender);
        Assert.False(viewModel.ShowEmptyState);

        viewModel.EnableDashPatternRendering = false;
        viewModel.EnableColorRendering = false;
        viewModel.ShowGrid = false;

        Assert.False(render.EnableDashPatternRendering);
        Assert.False(render.EnableColorRendering);
        Assert.False(render.ShowGrid);
    }

    [Fact]
    public void ShowsEmptyState_WhenNoActiveDocument()
    {
        var context = new CadDocumentContextService();
        var viewModel = new CadRenderOptionsToolViewModel(context);

        Assert.True(viewModel.ShowEmptyState);
        Assert.False(viewModel.HasActiveRender);
        Assert.Equal("No active document", viewModel.ActiveDocumentTitle);
    }

    [Fact]
    public void SyncsFromActiveDocument_WhenActiveDocumentChanges()
    {
        var context = new CadDocumentContextService();
        var viewModel = new CadRenderOptionsToolViewModel(context);

        var firstDocument = new CadDocument();
        var firstRender = CreateRenderViewModel(firstDocument);
        firstRender.ShowGrid = false;
        firstRender.ShowAxes = false;
        firstRender.EnableDashPatternRendering = false;
        firstRender.EnableColorRendering = false;
        var firstViewModel = new CadDocumentViewModel(firstDocument, CadFileFormat.Dxf, null, "first.dxf", firstRender);
        context.Register(firstViewModel);

        var secondDocument = new CadDocument();
        var secondRender = CreateRenderViewModel(secondDocument);
        secondRender.ShowGrid = true;
        secondRender.ShowAxes = true;
        secondRender.EnableDashPatternRendering = true;
        secondRender.EnableColorRendering = true;
        var secondViewModel = new CadDocumentViewModel(secondDocument, CadFileFormat.Dwg, null, "second.dwg", secondRender);
        context.Register(secondViewModel);

        context.ActiveDocument = firstViewModel;
        Assert.Equal("first.dxf", viewModel.ActiveDocumentTitle);
        Assert.False(viewModel.ShowGrid);
        Assert.False(viewModel.ShowAxes);
        Assert.False(viewModel.EnableDashPatternRendering);
        Assert.False(viewModel.EnableColorRendering);

        context.ActiveDocument = secondViewModel;
        Assert.Equal("second.dwg", viewModel.ActiveDocumentTitle);
        Assert.True(viewModel.ShowGrid);
        Assert.True(viewModel.ShowAxes);
        Assert.True(viewModel.EnableDashPatternRendering);
        Assert.True(viewModel.EnableColorRendering);
    }

    [Fact]
    public void SyncsTitle_WhenActiveDocumentTitleChanges()
    {
        var context = new CadDocumentContextService();
        var viewModel = new CadRenderOptionsToolViewModel(context);
        var document = new CadDocument();
        var render = CreateRenderViewModel(document);
        var documentViewModel = new CadDocumentViewModel(document, CadFileFormat.Dxf, null, "original.dxf", render);

        context.Register(documentViewModel);
        Assert.Equal("original.dxf", viewModel.ActiveDocumentTitle);

        documentViewModel.UpdateLocation(CadFileFormat.Dwg, null, "renamed.dwg");
        Assert.Equal("renamed.dwg", viewModel.ActiveDocumentTitle);
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
            dynamicBlockOverrides: null,
            dynamicBlockOverrideChanges: null,
            new CadSelectionService(),
            new CadSelectionFocusService(),
            new NullRenderStatsExportService(),
            statsFileName: null,
            allowLayoutUpdates: false);
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

        public RenderScene BuildBlock(CadDocument document, ACadSharp.Tables.BlockRecord block, CadRenderSceneSettings settings)
        {
            return _scene;
        }
    }

    private sealed class NullRenderStatsExportService : IRenderStatsExportService
    {
        public Task<RenderStatsExportResult?> SaveStatsAsync(string? suggestedFileName, CancellationToken cancellationToken)
        {
            return Task.FromResult<RenderStatsExportResult?>(null);
        }
    }
}
