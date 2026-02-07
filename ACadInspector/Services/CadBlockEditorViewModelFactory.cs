using System;
using ACadInspector.Core;
using ACadInspector.Editing.Interaction;
using ACadInspector.Rendering;
using ACadInspector.ViewModels;
using ACadSharp.Tables;

namespace ACadInspector.Services;

public sealed class CadBlockEditorViewModelFactory
{
    private readonly ICadRenderSceneBuilder _sceneBuilder;
    private readonly CadRenderSceneSettings _baseSettings;
    private readonly CadSelectionService _selectionService;
    private readonly CadSelectionFocusService _focusService;
    private readonly IRenderStatsExportService _statsExportService;
    private readonly CadEditorSessionHostService? _sessionHost;
    private readonly CadEditorControllerHostService? _controllerHost;
    private readonly ICadInteractiveCommandAdapterRegistry? _interactiveAdapterRegistry;
    private readonly CadCollaborationWorkspaceService? _collaborationWorkspace;

    public CadBlockEditorViewModelFactory(
        ICadRenderSceneBuilder sceneBuilder,
        CadRenderSceneSettings baseSettings,
        CadSelectionService selectionService,
        CadSelectionFocusService focusService,
        IRenderStatsExportService statsExportService,
        CadEditorSessionHostService? sessionHost = null,
        CadEditorControllerHostService? controllerHost = null,
        ICadInteractiveCommandAdapterRegistry? interactiveAdapterRegistry = null,
        CadCollaborationWorkspaceService? collaborationWorkspace = null)
    {
        _sceneBuilder = sceneBuilder;
        _baseSettings = baseSettings;
        _selectionService = selectionService;
        _focusService = focusService;
        _sessionHost = sessionHost;
        _controllerHost = controllerHost;
        _interactiveAdapterRegistry = interactiveAdapterRegistry;
        _collaborationWorkspace = collaborationWorkspace;
        _statsExportService = statsExportService;
    }

    public CadBlockEditorViewModel Create(CadDocumentViewModel documentViewModel, BlockRecord block)
    {
        if (documentViewModel is null)
        {
            throw new ArgumentNullException(nameof(documentViewModel));
        }

        if (block is null)
        {
            throw new ArgumentNullException(nameof(block));
        }

        var selection = CadRenderSettingsBuilder.ResolveDefaultLayout(documentViewModel.Document);
        var settings = CadRenderSettingsBuilder.Build(
            documentViewModel.Document,
            documentViewModel.Path,
            _baseSettings,
            selection);

        var overrideProvider = new BlockEditorDynamicOverrideProvider();
        var scene = _sceneBuilder.BuildBlock(documentViewModel.Document, block, settings);
        var controller = _controllerHost?.GetOrCreate(documentViewModel.Document);
        var statsFileName = $"block-{block.Name}-stats.json";
        var render = new CadRenderViewModel(
            documentViewModel.Document,
            scene,
            _sceneBuilder,
            _baseSettings,
            selection,
            documentViewModel.Path,
            overrideProvider,
            dynamicBlockOverrideChanges: null,
            _selectionService,
            _focusService,
            _sessionHost,
            controller?.CommandRuntime,
            interactiveAdapterRegistry: _interactiveAdapterRegistry,
            collaborationWorkspace: _collaborationWorkspace,
            statsExportService: _statsExportService,
            statsFileName: statsFileName,
            allowLayoutUpdates: false);
        render.ShowLayoutTabs = false;
        render.FitOnLoad = true;
        render.FitRequest++;

        var viewModel = new CadBlockEditorViewModel(
            documentViewModel.Document,
            block,
            render,
            _sceneBuilder,
            _baseSettings,
            selection,
            documentViewModel.Path,
            overrideProvider);
        viewModel.ShowAttributes = _baseSettings.RenderAttributes;
        viewModel.ShowAttributeDefinitions = _baseSettings.RenderAttributeDefinitions;
        return viewModel;
    }
}
