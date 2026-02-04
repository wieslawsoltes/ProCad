using System;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Core;
using ACadInspector.Diagnostics;
using ACadInspector.Rendering;
using ACadInspector.Services;
using ACadInspector.Tests.Rendering;
using ACadInspector.ViewModels;
using ACadSharp;
using ACadSharp.Entities;
using Xunit;

namespace ACadInspector.Tests.ViewModels;

public sealed class ToolActivationTests
{
    [Fact]
    public void DxfRawViewModel_UpdatesOnlyWhenActive()
    {
        var selection = new CadSelectionService();
        var context = new CadDocumentContextService();
        var viewModel = new CadDxfRawViewModel(selection, context);

        viewModel.IsActive = false;
        selection.SelectedObject = new CadDocument();

        Assert.True(string.IsNullOrEmpty(viewModel.RawDxfDocument.Text));

        viewModel.IsActive = true;

        Assert.False(string.IsNullOrEmpty(viewModel.RawDxfDocument.Text));
    }

    [Fact]
    public void DxfSemanticsViewModel_UpdatesOnlyWhenActive()
    {
        var selection = new CadSelectionService();
        var diagnostics = new FastPathDiagnosticsService();
        var viewModel = new CadDxfSemanticsViewModel(selection, diagnostics);

        viewModel.IsActive = false;
        selection.SelectedObject = new Line();

        Assert.Empty(viewModel.PropertyRowsView);

        viewModel.IsActive = true;

        Assert.True(viewModel.PropertyRowsView.Count > 0);
    }

    [Fact]
    public void DwgSemanticsViewModel_UpdatesOnlyWhenActive()
    {
        var selection = new CadSelectionService();
        var context = new CadDocumentContextService();
        var diagnostics = new FastPathDiagnosticsService();
        var viewModel = new CadDwgSemanticsViewModel(selection, context, diagnostics);

        viewModel.IsActive = false;
        selection.SelectedObject = new CadDocument();

        Assert.Empty(viewModel.HeaderRowsView);

        viewModel.IsActive = true;

        Assert.True(viewModel.HeaderRowsView.Count > 0);
    }

    [Fact]
    public void PropertyGridViewModel_UpdatesOnlyWhenActive()
    {
        var selection = new CadSelectionService();
        var diagnostics = new FastPathDiagnosticsService();
        var pipeline = new CadPropertyEditPipeline(Array.Empty<ICadPropertyValidator>());
        var stampProvider = new RenderCacheStampProvider();
        var context = new CadDocumentContextService();
        var dockService = new CadDocumentDockService();
        var scene = RenderSceneSamples.CreateBaselineScene();
        var sceneBuilder = new NullSceneBuilder(scene);
        var focusService = new CadSelectionFocusService();
        var baseSettings = new CadRenderSceneSettings();
        var statsExport = new NullRenderStatsExportService();
        var blockEditorFactory = new CadBlockEditorViewModelFactory(
            sceneBuilder,
            baseSettings,
            selection,
            focusService,
            statsExport);
        var blockEditorService = new CadBlockEditorService(context, dockService, blockEditorFactory);
        var dynamicOverrides = new CadDynamicBlockOverrideService();
        var viewModel = new PropertyGridViewModel(
            pipeline,
            stampProvider,
            selection,
            blockEditorService,
            dynamicOverrides,
            diagnostics);

        viewModel.IsActive = false;
        selection.SelectedObject = new Line();

        Assert.Empty(viewModel.Rows);

        viewModel.IsActive = true;

        Assert.True(viewModel.Rows.Count > 0);
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
