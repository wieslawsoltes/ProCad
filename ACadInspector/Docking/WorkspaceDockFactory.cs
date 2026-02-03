using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.ReactiveUI;
using Dock.Model.ReactiveUI.Controls;
using ACadInspector.ViewModels;

namespace ACadInspector.Docking;

public sealed class WorkspaceDockFactory : Factory
{
    private readonly PropertyGridViewModel _propertyGrid;
    private readonly CadIoOptionsViewModel _ioOptions;
    private readonly CadDocumentTreeViewModel _documentTree;
    private readonly CadLayerToolViewModel _layerTool;
    private readonly CadDxfSemanticsViewModel _dxfSemantics;
    private readonly CadDxfRawViewModel _dxfRaw;
    private readonly CadDwgSemanticsViewModel _dwgSemantics;
    private readonly CadPreviewViewModel _preview;
    private readonly CadBatchViewModel _batch;
    private readonly CadScriptingViewModel _scripting;

    public WorkspaceDockFactory(
        PropertyGridViewModel propertyGrid,
        CadIoOptionsViewModel ioOptions,
        CadDocumentTreeViewModel documentTree,
        CadLayerToolViewModel layerTool,
        CadDxfSemanticsViewModel dxfSemantics,
        CadDxfRawViewModel dxfRaw,
        CadDwgSemanticsViewModel dwgSemantics,
        CadPreviewViewModel preview,
        CadBatchViewModel batch,
        CadScriptingViewModel scripting)
    {
        _propertyGrid = propertyGrid;
        _ioOptions = ioOptions;
        _documentTree = documentTree;
        _layerTool = layerTool;
        _dxfSemantics = dxfSemantics;
        _dxfRaw = dxfRaw;
        _dwgSemantics = dwgSemantics;
        _preview = preview;
        _batch = batch;
        _scripting = scripting;
    }

    public override IRootDock CreateLayout()
    {
        var documents = CreateDocumentDock();
        documents.Id = "Documents";
        documents.Title = "Documents";
        documents.VisibleDockables = CreateList<IDockable>();
        documents.ActiveDockable = null;
        documents.DefaultDockable = null;

        _documentTree.Id = "DocumentTree";
        _documentTree.Title = "Document Tree";

        _layerTool.Id = "Layers";
        _layerTool.Title = "Layers";

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

        var leftTools = CreateToolDock();
        leftTools.Id = "LeftTools";
        leftTools.Title = "Document Tree";
        leftTools.VisibleDockables = CreateList<IDockable>(_documentTree);
        leftTools.ActiveDockable = _documentTree;
        leftTools.DefaultDockable = _documentTree;

        var rightTopTools = CreateToolDock();
        rightTopTools.Id = "InspectorTools";
        rightTopTools.Title = "Inspector";
        rightTopTools.VisibleDockables = CreateList<IDockable>(_propertyGrid, _layerTool, _preview);
        rightTopTools.ActiveDockable = _propertyGrid;
        rightTopTools.DefaultDockable = _propertyGrid;

        var rightBottomTools = CreateToolDock();
        rightBottomTools.Id = "SemanticsTools";
        rightBottomTools.Title = "Semantics";
        rightBottomTools.VisibleDockables = CreateList<IDockable>(_dxfSemantics, _dxfRaw, _dwgSemantics, _batch, _scripting, _ioOptions);
        rightBottomTools.ActiveDockable = _dxfSemantics;
        rightBottomTools.DefaultDockable = _dxfSemantics;

        documents.Proportion = 0.65;
        leftTools.Proportion = 0.35;
        rightTopTools.Proportion = 0.55;
        rightBottomTools.Proportion = 0.45;

        var left = CreateProportionalDock();
        left.Id = "Left";
        left.Orientation = Orientation.Vertical;
        left.VisibleDockables = CreateList<IDockable>(documents, new ProportionalDockSplitter(), leftTools);
        left.ActiveDockable = documents;
        left.DefaultDockable = documents;
        left.Proportion = 0.6;

        var right = CreateProportionalDock();
        right.Id = "Right";
        right.Orientation = Orientation.Vertical;
        right.VisibleDockables = CreateList<IDockable>(rightTopTools, new ProportionalDockSplitter(), rightBottomTools);
        right.ActiveDockable = rightTopTools;
        right.DefaultDockable = rightTopTools;
        right.Proportion = 0.4;

        var main = CreateProportionalDock();
        main.Id = "Main";
        main.Orientation = Orientation.Horizontal;
        main.VisibleDockables = CreateList<IDockable>(left, new ProportionalDockSplitter(), right);
        main.ActiveDockable = left;
        main.DefaultDockable = left;

        var root = CreateRootDock();
        root.Id = "Root";
        root.VisibleDockables = CreateList<IDockable>(main);
        root.ActiveDockable = main;
        root.DefaultDockable = main;
        root.LeftPinnedDockables = CreateList<IDockable>();
        root.RightPinnedDockables = CreateList<IDockable>();
        root.TopPinnedDockables = CreateList<IDockable>();
        root.BottomPinnedDockables = CreateList<IDockable>();

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

        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        base.InitLayout(layout);
        ResetActiveFlags();
        SyncActiveDockables(layout);
    }

    public override void OnDockableActivated(IDockable? dockable)
    {
        base.OnDockableActivated(dockable);
        if (dockable is not null)
        {
            SetDockableActive(dockable, true);
        }
    }

    public override void OnDockableDeactivated(IDockable? dockable)
    {
        base.OnDockableDeactivated(dockable);
        if (dockable is not null)
        {
            SetDockableActive(dockable, false);
        }
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
}
