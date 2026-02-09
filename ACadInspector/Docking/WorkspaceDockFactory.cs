using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.ReactiveUI;
using Dock.Model.ReactiveUI.Controls;
using ACadInspector.Services;
using ACadInspector.ViewModels;

namespace ACadInspector.Docking;

public sealed class WorkspaceDockFactory : Factory
{
    private IRootDock? _rootLayout;
    private readonly PropertyGridViewModel _propertyGrid;
    private readonly CadIoOptionsViewModel _ioOptions;
    private readonly CadDocumentTreeViewModel _documentTree;
    private readonly CadLayerToolViewModel _layerTool;
    private readonly CadRenderOptionsToolViewModel _renderOptionsTool;
    private readonly CadEntityTypeToolViewModel _entityTypeTool;
    private readonly CadBlocksToolViewModel _blocksTool;
    private readonly CadViewportsToolViewModel _viewportsTool;
    private readonly CadTextStyleToolViewModel _textStyleTool;
    private readonly CadTextStyleEditorToolViewModel _textStyleEditorTool;
    private readonly CadLineTypeToolViewModel _lineTypeTool;
    private readonly CadLineTypeEditorToolViewModel _lineTypeEditorTool;
    private readonly CadDimensionStyleToolViewModel _dimensionStyleTool;
    private readonly CadDxfSemanticsViewModel _dxfSemantics;
    private readonly CadDxfRawViewModel _dxfRaw;
    private readonly CadDwgSemanticsViewModel _dwgSemantics;
    private readonly CadPreviewViewModel _preview;
    private readonly CadBatchViewModel _batch;
    private readonly CadScriptingViewModel _scripting;
    private readonly CadCommandLineViewModel _commandLine;
    private readonly CadEditorToolPanelViewModel _editorToolPanel;
    private readonly CadCollaborationToolViewModel _collaborationTool;
    private readonly CadSelectionService _selectionService;
    private readonly CadDocumentContextService _documentContext;
    private readonly CadEditorSessionHostService _sessionHost;
    private readonly CadEditorControllerHostService _controllerHost;
    private readonly CadCollaborationWorkspaceService _collaborationWorkspace;

    public WorkspaceDockFactory(
        PropertyGridViewModel propertyGrid,
        CadIoOptionsViewModel ioOptions,
        CadDocumentTreeViewModel documentTree,
        CadLayerToolViewModel layerTool,
        CadRenderOptionsToolViewModel renderOptionsTool,
        CadEntityTypeToolViewModel entityTypeTool,
        CadBlocksToolViewModel blocksTool,
        CadViewportsToolViewModel viewportsTool,
        CadTextStyleToolViewModel textStyleTool,
        CadTextStyleEditorToolViewModel textStyleEditorTool,
        CadLineTypeToolViewModel lineTypeTool,
        CadLineTypeEditorToolViewModel lineTypeEditorTool,
        CadDimensionStyleToolViewModel dimensionStyleTool,
        CadDxfSemanticsViewModel dxfSemantics,
        CadDxfRawViewModel dxfRaw,
        CadDwgSemanticsViewModel dwgSemantics,
        CadPreviewViewModel preview,
        CadBatchViewModel batch,
        CadScriptingViewModel scripting,
        CadCommandLineViewModel commandLine,
        CadEditorToolPanelViewModel editorToolPanel,
        CadCollaborationToolViewModel collaborationTool,
        CadSelectionService selectionService,
        CadDocumentContextService documentContext,
        CadEditorSessionHostService sessionHost,
        CadEditorControllerHostService controllerHost,
        CadCollaborationWorkspaceService collaborationWorkspace)
    {
        _propertyGrid = propertyGrid;
        _ioOptions = ioOptions;
        _documentTree = documentTree;
        _layerTool = layerTool;
        _renderOptionsTool = renderOptionsTool;
        _entityTypeTool = entityTypeTool;
        _blocksTool = blocksTool;
        _viewportsTool = viewportsTool;
        _textStyleTool = textStyleTool;
        _textStyleEditorTool = textStyleEditorTool;
        _lineTypeTool = lineTypeTool;
        _lineTypeEditorTool = lineTypeEditorTool;
        _dimensionStyleTool = dimensionStyleTool;
        _dxfSemantics = dxfSemantics;
        _dxfRaw = dxfRaw;
        _dwgSemantics = dwgSemantics;
        _preview = preview;
        _batch = batch;
        _scripting = scripting;
        _commandLine = commandLine;
        _editorToolPanel = editorToolPanel;
        _collaborationTool = collaborationTool;
        _selectionService = selectionService;
        _documentContext = documentContext;
        _sessionHost = sessionHost;
        _controllerHost = controllerHost;
        _collaborationWorkspace = collaborationWorkspace;
    }

    public override IRootDock CreateLayout()
    {
        var documents = CreateDocumentDock();
        documents.Id = "Documents";
        documents.Title = "Documents";
        documents.VisibleDockables = CreateList<IDockable>();
        documents.ActiveDockable = null;
        documents.DefaultDockable = null;
        documents.IsCollapsable = false;
        documents.CanClose = false;

        _documentTree.Id = "DocumentTree";
        _documentTree.Title = "Document Tree";
        _documentTree.Dock = DockMode.Left;

        _layerTool.Id = "Layers";
        _layerTool.Title = "Layers";

        _renderOptionsTool.Id = "RenderOptions";
        _renderOptionsTool.Title = "Render Options";

        _entityTypeTool.Id = "EntityTypes";
        _entityTypeTool.Title = "Entity Types";

        _blocksTool.Id = "Blocks";
        _blocksTool.Title = "Blocks";

        _viewportsTool.Id = "Viewports";
        _viewportsTool.Title = "Viewports";

        _textStyleTool.Id = "TextStyles";
        _textStyleTool.Title = "Text Styles";

        _textStyleEditorTool.Id = "TextStyleEditor";
        _textStyleEditorTool.Title = "Text Style Editor";

        _lineTypeTool.Id = "LineTypes";
        _lineTypeTool.Title = "Line Types";

        _lineTypeEditorTool.Id = "LineTypeEditor";
        _lineTypeEditorTool.Title = "Line Type Editor";

        _dimensionStyleTool.Id = "DimensionStyles";
        _dimensionStyleTool.Title = "Dimension Styles";

        _dxfSemantics.Id = "DxfSemantics";
        _dxfSemantics.Title = "DXF";

        _dxfRaw.Id = "DxfRaw";
        _dxfRaw.Title = "DXF Raw";

        _dwgSemantics.Id = "DwgSemantics";
        _dwgSemantics.Title = "DWG";

        _preview.Id = "Preview";
        _preview.Title = "Preview";

        _propertyGrid.Id = "Properties";
        _propertyGrid.Title = "Properties";

        _ioOptions.Id = "IoOptions";
        _ioOptions.Title = "IO Options";

        _batch.Id = "Batch";
        _batch.Title = "Batch";

        _scripting.Id = "Scripting";
        _scripting.Title = "Scripting";

        _commandLine.Id = "CommandLine";
        _commandLine.Title = "Command Line";

        _editorToolPanel.Id = "EditorTools";
        _editorToolPanel.Title = "Editor Tools";

        _collaborationTool.Id = "Collaboration";
        _collaborationTool.Title = "Collaboration";

        var leftTools = CreateToolDock();
        leftTools.Id = "LeftTools";
        leftTools.Title = "Document Tree";
        leftTools.Dock = DockMode.Left;
        leftTools.VisibleDockables = CreateList<IDockable>(_documentTree);
        leftTools.ActiveDockable = _documentTree;
        leftTools.DefaultDockable = _documentTree;

        var rightTopTools = CreateToolDock();
        rightTopTools.Id = "InspectorTools";
        rightTopTools.Title = "Inspector";
        rightTopTools.VisibleDockables = CreateList<IDockable>(
            _editorToolPanel,
            _propertyGrid,
            _renderOptionsTool,
            _layerTool,
            _entityTypeTool,
            _blocksTool,
            _viewportsTool,
            _textStyleTool,
            _textStyleEditorTool,
            _lineTypeTool,
            _lineTypeEditorTool,
            _dimensionStyleTool,
            _preview);
        rightTopTools.ActiveDockable = _editorToolPanel;
        rightTopTools.DefaultDockable = _editorToolPanel;

        var rightBottomTools = CreateToolDock();
        rightBottomTools.Id = "SemanticsTools";
        rightBottomTools.Title = "Semantics";
        rightBottomTools.VisibleDockables = CreateList<IDockable>(
            _commandLine,
            _collaborationTool,
            _dxfSemantics,
            _dxfRaw,
            _dwgSemantics,
            _batch,
            _scripting,
            _ioOptions);
        rightBottomTools.ActiveDockable = _commandLine;
        rightBottomTools.DefaultDockable = _commandLine;

        documents.Proportion = 0.6;
        leftTools.Proportion = 0.2;
        rightTopTools.Proportion = 0.55;
        rightBottomTools.Proportion = 0.45;

        var right = CreateProportionalDock();
        right.Id = "Right";
        right.Orientation = Orientation.Vertical;
        right.VisibleDockables = CreateList<IDockable>(rightTopTools, new ProportionalDockSplitter(), rightBottomTools);
        right.ActiveDockable = rightTopTools;
        right.DefaultDockable = rightTopTools;
        right.Proportion = 0.2;

        var main = CreateProportionalDock();
        main.Id = "Main";
        main.Orientation = Orientation.Horizontal;
        main.VisibleDockables = CreateList<IDockable>(leftTools, new ProportionalDockSplitter(), documents, new ProportionalDockSplitter(), right);
        main.ActiveDockable = documents;
        main.DefaultDockable = documents;

        var root = CreateRootDock();
        root.Id = "Root";
        root.VisibleDockables = CreateList<IDockable>(main);
        root.ActiveDockable = main;
        root.DefaultDockable = main;
        root.HiddenDockables = CreateList<IDockable>();
        root.LeftPinnedDockables = CreateList<IDockable>();
        root.RightPinnedDockables = CreateList<IDockable>();
        root.TopPinnedDockables = CreateList<IDockable>();
        root.BottomPinnedDockables = CreateList<IDockable>();
        root.Windows = CreateList<IDockWindow>();

        var pinned = CreateToolDock();
        pinned.Id = "Pinned";
        pinned.Title = "Pinned";
        pinned.VisibleDockables = CreateList<IDockable>();
        pinned.ActiveDockable = null;
        pinned.DefaultDockable = null;
        pinned.IsEmpty = true;
        pinned.IsCollapsable = true;
        pinned.Proportion = 0.0;
        root.PinnedDock = pinned;
        _rootLayout = root;

        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        base.InitLayout(layout);
        if (layout is IRootDock rootDock)
        {
            _rootLayout = rootDock;
        }

        RefreshActiveFlags();
    }

    public override void OnDockableActivated(IDockable? dockable)
    {
        base.OnDockableActivated(dockable);
        RefreshActiveFlags();

        if (dockable is CadDocumentViewModel document)
        {
            document.Render.ClearTransientInteractionVisuals();
            _selectionService.SelectedObject = document.Document;
            _documentTree.LoadDocument(document);
            _documentContext.ActiveDocument = document;
        }

        if (dockable is CadBlockEditorViewModel blockEditor)
        {
            blockEditor.Render.ClearTransientInteractionVisuals();
            if (_documentContext.TryGetViewModel(blockEditor.Document, out var documentViewModel))
            {
                _documentContext.ActiveDocument = documentViewModel;
                _documentTree.LoadDocument(documentViewModel);
            }

            _selectionService.SelectedObject = blockEditor.Block;
        }
    }

    public override void OnDockableDeactivated(IDockable? dockable)
    {
        base.OnDockableDeactivated(dockable);
        RefreshActiveFlags();
    }

    public override void OnDockableRemoved(IDockable? dockable)
    {
        base.OnDockableRemoved(dockable);
        CleanupClosedDockable(dockable);
        RefreshActiveFlags();
    }

    public override void OnDockableClosed(IDockable? dockable)
    {
        base.OnDockableClosed(dockable);
        CleanupClosedDockable(dockable);
        RefreshActiveFlags();
    }

    private void RefreshActiveFlags()
    {
        var rootLayout = _rootLayout;
        if (rootLayout is null)
        {
            return;
        }

        ResetActiveFlags();
        SyncActiveDockables(rootLayout);
    }

    private void ResetActiveFlags()
    {
        _propertyGrid.IsActive = false;
        _dxfSemantics.IsActive = false;
        _dxfRaw.IsActive = false;
        _dwgSemantics.IsActive = false;
    }

    private void SyncActiveDockables(IDockable dockable)
    {
        if (dockable is not IDock dock)
        {
            return;
        }

        if (dock.ActiveDockable is not null)
        {
            SetDockableActive(dock.ActiveDockable, true);
            SyncActiveDockables(dock.ActiveDockable);
        }

        if (dock.VisibleDockables is null)
        {
            return;
        }

        foreach (var child in dock.VisibleDockables)
        {
            SyncActiveDockables(child);
        }
    }

    private void SetDockableActive(IDockable dockable, bool isActive)
    {
        switch (dockable)
        {
            case CadDxfSemanticsViewModel viewModel:
                viewModel.IsActive = isActive;
                break;
            case CadDxfRawViewModel viewModel:
                viewModel.IsActive = isActive;
                break;
            case CadDwgSemanticsViewModel viewModel:
                viewModel.IsActive = isActive;
                break;
            case PropertyGridViewModel viewModel:
                viewModel.IsActive = isActive;
                break;
        }
    }

    private void CleanupClosedDockable(IDockable? dockable)
    {
        if (dockable is CadDocumentViewModel documentViewModel)
        {
            documentViewModel.Render.ClearTransientInteractionVisuals(preserveRemoteHints: false);

            if (_sessionHost.TryGet(documentViewModel.Document, out var session))
            {
                _ = _collaborationWorkspace.CloseSessionAsync(session);
            }

            _sessionHost.Remove(documentViewModel.Document);
            _controllerHost.Remove(documentViewModel.Document);
            _documentContext.Unregister(documentViewModel.Document);

            var selectedDocument = _documentContext.ResolveDocument(_selectionService.SelectedObject);
            if (ReferenceEquals(selectedDocument, documentViewModel.Document))
            {
                _selectionService.ClearSelection();
            }

            _documentTree.LoadDocument(_documentContext.ActiveDocument);
            documentViewModel.Dispose();
            return;
        }

        if (dockable is CadBlockEditorViewModel blockEditorViewModel)
        {
            blockEditorViewModel.Render.ClearTransientInteractionVisuals(preserveRemoteHints: false);
            blockEditorViewModel.Dispose();
        }
    }
}
