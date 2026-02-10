using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Diagnostics;
using ACadInspector.Core;
using ACadInspector.Editing.Controllers;
using ACadInspector.Editing.Interaction;
using ACadInspector.Editing.Prompt;
using ACadInspector.Services;
using ACadInspector.Rendering;
using ACadSharp;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.ReactiveUI.Controls;
using ReactiveUI;

namespace ACadInspector.ViewModels;

public sealed class WorkspaceViewModel : ViewModelBase, IRoutableViewModel
{
    public string? UrlPathSegment => "workspace";
    public IScreen HostScreen { get; }

    public IFactory Factory { get; }

    public IRootDock Layout { get; }

    public PropertyGridViewModel PropertyGrid { get; }
    public FastPathDiagnosticsService FastPathDiagnostics { get; }

    public ReactiveCommand<Unit, Unit> NewCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveAsCommand { get; }
    public ReactiveCommand<Unit, Unit> CompareCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearDiagnosticsCommand { get; }

    private readonly ICadDocumentService _documentService;
    private readonly ICadFileDialogService _fileDialogService;
    private readonly CadIoOptionsViewModel _ioOptions;
    private readonly CadDocumentTreeViewModel _documentTree;
    private readonly CadSelectionService _selectionService;
    private readonly CadSelectionFocusService _focusService;
    private readonly CadDocumentContextService _documentContext;
    private readonly CadDocumentDockService _documentDockService;
    private readonly CadDynamicBlockOverrideService _dynamicBlockOverrides;
    private readonly Docking.WorkspaceDockFactory _dockFactory;
    private readonly CadCompareViewModelFactory _compareFactory;
    private readonly ICadRenderSceneBuilder _renderSceneBuilder;
    private readonly CadRenderSceneSettings _renderSceneSettings;
    private readonly CadEditorSessionHostService _sessionHost;
    private readonly CadEditorControllerHostService _controllerHost;
    private readonly ICadInteractiveCommandAdapterRegistry _interactiveAdapterRegistry;
    private readonly CadCollaborationWorkspaceService _collaborationWorkspace;
    private readonly IRenderStatsExportService _statsExportService;
    private readonly IAppNotificationService _notificationService;

    public WorkspaceViewModel(
        IScreen hostScreen,
        PropertyGridViewModel propertyGrid,
        CadIoOptionsViewModel ioOptions,
        CadDocumentTreeViewModel documentTree,
        CadSelectionService selectionService,
        CadSelectionFocusService focusService,
        CadDocumentContextService documentContext,
        CadDocumentDockService documentDockService,
        CadDynamicBlockOverrideService dynamicBlockOverrides,
        ICadDocumentService documentService,
        ICadFileDialogService fileDialogService,
        Docking.WorkspaceDockFactory dockFactory,
        CadCompareViewModelFactory compareFactory,
        FastPathDiagnosticsService fastPathDiagnostics,
        ICadRenderSceneBuilder renderSceneBuilder,
        CadRenderSceneSettings renderSceneSettings,
        CadEditorSessionHostService sessionHost,
        CadEditorControllerHostService controllerHost,
        ICadInteractiveCommandAdapterRegistry interactiveAdapterRegistry,
        CadCollaborationWorkspaceService collaborationWorkspace,
        IRenderStatsExportService statsExportService,
        IAppNotificationService notificationService)
    {
        AppLog.Write("WorkspaceViewModel ctor start.");
        HostScreen = hostScreen;
        PropertyGrid = propertyGrid;
        FastPathDiagnostics = fastPathDiagnostics;
        _ioOptions = ioOptions;
        _documentTree = documentTree;
        _selectionService = selectionService;
        _focusService = focusService;
        _documentContext = documentContext;
        _documentDockService = documentDockService;
        _dynamicBlockOverrides = dynamicBlockOverrides;
        _documentService = documentService;
        _fileDialogService = fileDialogService;
        _dockFactory = dockFactory;
        _compareFactory = compareFactory;
        _renderSceneBuilder = renderSceneBuilder;
        _renderSceneSettings = renderSceneSettings;
        _sessionHost = sessionHost;
        _controllerHost = controllerHost;
        _interactiveAdapterRegistry = interactiveAdapterRegistry;
        _collaborationWorkspace = collaborationWorkspace;
        _statsExportService = statsExportService;
        _notificationService = notificationService;
        Factory = dockFactory;

        AppLog.Write("WorkspaceViewModel creating layout.");
        Layout = dockFactory.CreateLayout();
        AppLog.Write("WorkspaceViewModel layout created.");
        dockFactory.InitLayout(Layout);
        _documentDockService.RegisterLayout(Layout);
        AppLog.Write("WorkspaceViewModel layout initialized.");

        NewCommand = ReactiveCommand.CreateFromTask(NewAsync);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        SaveAsCommand = ReactiveCommand.CreateFromTask(SaveAsAsync);
        CompareCommand = ReactiveCommand.Create(OpenCompare);
        ClearDiagnosticsCommand = ReactiveCommand.Create(FastPathDiagnostics.Clear);
        AppLog.Write("WorkspaceViewModel ctor done.");
    }

    private async Task NewAsync(CancellationToken cancellationToken)
    {
        try
        {
            var document = new CadDocument();
            var title = BuildNewDocumentTitle();
            var viewModel = await CreateDocumentViewModelAsync(
                    document,
                    CadFileFormat.Dxf,
                    path: null,
                    displayName: title,
                    cancellationToken)
                .ConfigureAwait(true);

            _documentContext.Register(viewModel);
            AddDocument(viewModel);
            _documentTree.LoadDocument(viewModel);
            _selectionService.SelectedObject = viewModel.Document;
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation from background scene creation.
        }
        catch (Exception ex)
        {
            AppLog.Error("Create new document failed.", exception: ex);
            _notificationService.ShowError("New Drawing Failed", $"Could not create a new drawing: {ex.Message}");
        }
    }

    private async Task OpenAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CadOpenFileResult> results;
        try
        {
            results = await _fileDialogService.OpenCadFilesAsync(null, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            AppLog.Error("Open file picker failed.", exception: ex);
            _notificationService.ShowError("Open Failed", $"Could not open file picker: {ex.Message}");
            return;
        }

        if (results.Count == 0)
        {
            return;
        }

        CadDocumentViewModel? lastOpened = null;
        var openedCount = 0;
        var failedCount = 0;
        string? firstFailureMessage = null;
        foreach (var result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var options = _ioOptions.BuildReadOptions(result.Format);
                var document = await LoadDocumentAsync(result, options, cancellationToken).ConfigureAwait(true);
                if (document is null)
                {
                    continue;
                }

                var viewModel = await CreateDocumentViewModelAsync(
                        document,
                        result.Format,
                        result.Path,
                        result.FileName,
                        cancellationToken)
                    .ConfigureAwait(true);
                _documentContext.Register(viewModel);
                AddDocument(viewModel);
                lastOpened = viewModel;
                openedCount++;
            }
            catch (Exception ex)
            {
                failedCount++;
                firstFailureMessage ??= $"{result.FileName}: {ex.Message}";
                AppLog.Error($"Open failed for '{result.FileName}'.", exception: ex);
            }
        }

        if (lastOpened is not null)
        {
            _documentTree.LoadDocument(lastOpened);
            _selectionService.SelectedObject = lastOpened.Document;
        }

        if (failedCount <= 0)
        {
            return;
        }

        if (openedCount == 0)
        {
            _notificationService.ShowError(
                "Open Failed",
                $"Failed to open {failedCount} file(s). {firstFailureMessage}",
                TimeSpan.FromSeconds(10));
            return;
        }

        _notificationService.ShowWarning(
            "Open Partially Completed",
            $"Opened {openedCount} file(s), failed {failedCount}. {firstFailureMessage}",
            TimeSpan.FromSeconds(10));
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var viewModel = GetActiveDocument();
        if (viewModel is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(viewModel.Path))
        {
            await SaveAsAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        var options = _ioOptions.BuildWriteOptions(viewModel.Format);
        _documentService.Save(viewModel.Path, viewModel.Document, options);
    }

    private async Task SaveAsAsync(CancellationToken cancellationToken)
    {
        var viewModel = GetActiveDocument();
        if (viewModel is null)
        {
            return;
        }

        var suggestedFileName = EnsureExtension(viewModel.Title, viewModel.Format);
        var result = await _fileDialogService.SaveCadFileAsync(viewModel.Format, suggestedFileName, cancellationToken)
            .ConfigureAwait(true);
        if (result is null)
        {
            return;
        }

        var options = _ioOptions.BuildWriteOptions(result.Format);
        await SaveDocumentAsync(result, viewModel, options, cancellationToken).ConfigureAwait(true);

        viewModel.UpdateLocation(result.Format, result.Path, result.FileName);
    }

    private async Task<CadDocument?> LoadDocumentAsync(
        CadOpenFileResult result,
        CadReadOptions options,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(result.Path))
        {
            return await Task.Run(() => _documentService.Load(result.Path, options), cancellationToken)
                .ConfigureAwait(true);
        }

        await using var stream = await result.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => _documentService.Load(stream, options), cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task<CadDocumentViewModel> CreateDocumentViewModelAsync(
        CadDocument document,
        CadFileFormat format,
        string? path,
        string displayName,
        CancellationToken cancellationToken)
    {
        var selection = CadRenderSettingsBuilder.ResolveDefaultLayout(document);
        var settings = CadRenderSettingsBuilder.Build(
            document,
            path,
            _renderSceneSettings.WithDynamicBlockOverrides(_dynamicBlockOverrides),
            selection);
        var scene = await BuildSceneAsync(document, settings, cancellationToken).ConfigureAwait(true);
        var controller = _controllerHost.GetOrCreate(document);
        var statsFileName = BuildStatsFileName(displayName);
        var renderViewModel = new CadRenderViewModel(
            document,
            scene,
            _renderSceneBuilder,
            _renderSceneSettings,
            selection,
            path,
            _dynamicBlockOverrides,
            _dynamicBlockOverrides.WhenAnyValue(x => x.ChangeStamp),
            _selectionService,
            _focusService,
            _sessionHost,
            controller.CommandRuntime,
            interactiveAdapterRegistry: _interactiveAdapterRegistry,
            collaborationWorkspace: _collaborationWorkspace,
            statsExportService: _statsExportService,
            statsFileName: statsFileName);

        return new CadDocumentViewModel(document, format, path, displayName, renderViewModel);
    }

    private Task<RenderScene> BuildSceneAsync(
        CadDocument document,
        CadRenderSceneSettings settings,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => _renderSceneBuilder.Build(document, settings), cancellationToken);
    }

    private async Task SaveDocumentAsync(
        CadSaveFileResult result,
        CadDocumentViewModel viewModel,
        CadWriteOptions options,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(result.Path))
        {
            _documentService.Save(result.Path, viewModel.Document, options);
            return;
        }

        await using var stream = await result.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        _documentService.Save(stream, viewModel.Document, options);
    }

    private void AddDocument(CadDocumentViewModel viewModel)
    {
        var documentDock = FindDocumentDock(Layout);
        if (documentDock is null)
        {
            return;
        }
        viewModel.Id = Guid.NewGuid().ToString();
        documentDock.AddDocument(viewModel);
        documentDock.ActiveDockable = viewModel;
        documentDock.DefaultDockable ??= viewModel;
    }

    private void OpenCompare()
    {
        var documentDock = FindDocumentDock(Layout);
        if (documentDock is null)
        {
            return;
        }

        var compareViewModel = _compareFactory.Create();
        compareViewModel.Id = Guid.NewGuid().ToString();
        documentDock.AddDocument(compareViewModel);
        documentDock.ActiveDockable = compareViewModel;
        documentDock.DefaultDockable ??= compareViewModel;
    }

    private CadDocumentViewModel? GetActiveDocument()
    {
        var documentDock = FindDocumentDock(Layout);
        if (documentDock?.ActiveDockable is not CadDocumentViewModel viewModel)
        {
            return null;
        }
        return viewModel;
    }

    private static IDocumentDock? FindDocumentDock(IDockable dockable)
    {
        if (dockable is IDocumentDock documentDock)
        {
            return documentDock;
        }

        if (dockable is IDock dock && dock.VisibleDockables is not null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                var result = FindDocumentDock(child);
                if (result is not null)
                {
                    return result;
                }
            }
        }

        return null;
    }

    private string BuildNewDocumentTitle()
    {
        var documentDock = FindDocumentDock(Layout);
        var index = 1;
        while (true)
        {
            var candidate = $"Drawing{index}.dxf";
            if (!ContainsDocumentTitle(documentDock, candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    private static bool ContainsDocumentTitle(IDocumentDock? documentDock, string title)
    {
        if (documentDock?.VisibleDockables is null)
        {
            return false;
        }

        foreach (var dockable in documentDock.VisibleDockables)
        {
            if (dockable is CadDocumentViewModel document &&
                string.Equals(document.Title, title, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string EnsureExtension(string displayName, CadFileFormat format)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return format == CadFileFormat.Dxf ? "document.dxf" : "document.dwg";
        }

        var extension = Path.GetExtension(displayName);
        if (string.Equals(extension, ".dxf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".dwg", StringComparison.OrdinalIgnoreCase))
        {
            return displayName;
        }

        var suffix = format == CadFileFormat.Dxf ? ".dxf" : ".dwg";
        return $"{displayName}{suffix}";
    }

    private static string BuildStatsFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "render-stats.json";
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return "render-stats.json";
        }

        return $"{baseName}.render-stats.json";
    }
}
