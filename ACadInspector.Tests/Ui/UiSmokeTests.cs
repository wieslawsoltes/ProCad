using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector;
using ACadInspector.Collaboration.Presence;
using ACadInspector.Collaboration.Services;
using ACadInspector.Collaboration.Snapshots;
using ACadInspector.Collaboration.Transports;
using ACadInspector.Collaboration.UI;
using ACadInspector.Commands;
using ACadInspector.Core;
using ACadInspector.Diagnostics;
using ACadInspector.Docking;
using ACadInspector.Editing.Clipboard;
using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Controllers;
using ACadInspector.Editing.Interaction;
using ACadInspector.Editing.Prompt;
using ACadInspector.Editing.Sessions;
using ACadInspector.IO;
using ACadInspector.Rendering;
using ACadInspector.Scripting;
using ACadInspector.Services;
using ACadInspector.ViewModels;
using ACadInspector.Views;
using ACadSharp;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Dock.Model.Controls;
using Dock.Model.Core;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using ReactiveUI.Avalonia;
using Splat;
using Xunit;

namespace ACadInspector.Tests.Ui;

public sealed class UiSmokeTests
{
    [AvaloniaFact]
    public void WorkspaceView_RendersAndRoutes()
    {
        using var provider = BuildServiceProvider();
        Locator.CurrentMutable.RegisterConstant<IActivationForViewFetcher>(new AvaloniaActivationForViewFetcher());
        Locator.CurrentMutable.RegisterConstant<IViewLocator>(new AppViewLocator());
        EnsureStyles();

        var shell = provider.GetRequiredService<ShellViewModel>();
        var workspace = shell.Router.NavigationStack.LastOrDefault() as WorkspaceViewModel;
        Assert.NotNull(workspace);

        var view = new WorkspaceView { DataContext = workspace };

        var window = new Window
        {
            Width = 1200,
            Height = 800,
            Content = view
        };

        window.Show();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);

        var workspaceView = window.GetVisualDescendants().OfType<WorkspaceView>().FirstOrDefault();
        Assert.NotNull(workspaceView);

        window.Close();
    }

    [AvaloniaFact]
    public void WorkspaceView_SwitchesInspectorToolsWithoutCrashing()
    {
        using var provider = BuildServiceProvider();
        Locator.CurrentMutable.RegisterConstant<IActivationForViewFetcher>(new AvaloniaActivationForViewFetcher());
        Locator.CurrentMutable.RegisterConstant<IViewLocator>(new AppViewLocator());
        EnsureStyles();

        var shell = provider.GetRequiredService<ShellViewModel>();
        var workspace = shell.Router.NavigationStack.LastOrDefault() as WorkspaceViewModel;
        Assert.NotNull(workspace);

        var view = new WorkspaceView { DataContext = workspace };
        var window = new Window
        {
            Width = 1200,
            Height = 800,
            Content = view
        };

        window.Show();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);

        var inspectorDock = FindDockById(workspace!.Layout, "InspectorTools");
        Assert.NotNull(inspectorDock);
        Assert.NotNull(inspectorDock!.VisibleDockables);

        foreach (var dockable in inspectorDock.VisibleDockables!)
        {
            inspectorDock.ActiveDockable = dockable;
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        }

        window.Close();
    }

    [AvaloniaFact]
    public void WorkspaceView_SwitchesAllToolPanelsRepeatedlyWithDocumentWithoutCrashing()
    {
        using var provider = BuildServiceProvider();
        Locator.CurrentMutable.RegisterConstant<IActivationForViewFetcher>(new AvaloniaActivationForViewFetcher());
        Locator.CurrentMutable.RegisterConstant<IViewLocator>(new AppViewLocator());
        EnsureStyles();

        var shell = provider.GetRequiredService<ShellViewModel>();
        var workspace = shell.Router.NavigationStack.LastOrDefault() as WorkspaceViewModel;
        Assert.NotNull(workspace);

        var view = new WorkspaceView { DataContext = workspace };
        var window = new Window
        {
            Width = 1400,
            Height = 900,
            Content = view
        };

        window.Show();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);

        workspace = AddSeedDocument(provider, workspace!);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);

        var dockIds = new[] { "LeftTools", "InspectorTools", "SemanticsTools" };
        for (var pass = 0; pass < 24; pass++)
        {
            foreach (var dockId in dockIds)
            {
                var dock = FindDockById(workspace.Layout, dockId);
                Assert.NotNull(dock);
                Assert.NotNull(dock!.VisibleDockables);

                foreach (var dockable in dock.VisibleDockables!)
                {
                    dock.ActiveDockable = dockable;
                    window.Measure(new Size(window.Width, window.Height));
                    window.Arrange(new Rect(0, 0, window.Width, window.Height));
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                }
            }
        }

        window.Close();
    }

    private static IDock? FindDockById(IDockable root, string id)
    {
        if (root is IDock dock && string.Equals(dock.Id, id, StringComparison.Ordinal))
        {
            return dock;
        }

        if (root is not IDock parent || parent.VisibleDockables is null)
        {
            return null;
        }

        foreach (var child in parent.VisibleDockables)
        {
            var found = FindDockById(child, id);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static WorkspaceViewModel AddSeedDocument(ServiceProvider provider, WorkspaceViewModel workspace)
    {
        var document = new CadDocument();
        var renderSceneBuilder = provider.GetRequiredService<ICadRenderSceneBuilder>();
        var baseRenderSettings = provider.GetRequiredService<CadRenderSceneSettings>();
        var selection = CadRenderSettingsBuilder.ResolveDefaultLayout(document);
        var renderSettings = CadRenderSettingsBuilder.Build(
            document,
            documentPath: null,
            baseRenderSettings,
            selection);
        var scene = renderSceneBuilder.Build(document, renderSettings);
        var selectionService = provider.GetRequiredService<CadSelectionService>();
        var focusService = provider.GetRequiredService<CadSelectionFocusService>();
        var sessionHost = provider.GetRequiredService<CadEditorSessionHostService>();
        var controllerHost = provider.GetRequiredService<CadEditorControllerHostService>();
        var adapterRegistry = provider.GetRequiredService<ICadInteractiveCommandAdapterRegistry>();
        var collaborationWorkspace = provider.GetRequiredService<CadCollaborationWorkspaceService>();
        var statsExport = provider.GetRequiredService<IRenderStatsExportService>();
        var controller = controllerHost.GetOrCreate(document);

        var renderViewModel = new CadRenderViewModel(
            document,
            scene,
            renderSceneBuilder,
            baseRenderSettings,
            selection,
            documentPath: null,
            dynamicBlockOverrides: null,
            dynamicBlockOverrideChanges: null,
            selectionService,
            focusService,
            sessionHost,
            controller.CommandRuntime,
            interactiveAdapterRegistry: adapterRegistry,
            collaborationWorkspace: collaborationWorkspace,
            statsExportService: statsExport,
            statsFileName: "ui-smoke-seed.render-stats.json");

        var documentViewModel = new CadDocumentViewModel(
            document,
            CadFileFormat.Dxf,
            path: null,
            displayName: "UiSmokeSeed.dxf",
            renderViewModel);
        var documentContext = provider.GetRequiredService<CadDocumentContextService>();
        documentContext.Register(documentViewModel);
        documentContext.ActiveDocument = documentViewModel;

        if (FindDockById(workspace.Layout, "Documents") is not IDocumentDock documentDock)
        {
            throw new InvalidOperationException("Could not locate Documents dock.");
        }

        documentDock.AddDocument(documentViewModel);
        documentDock.ActiveDockable = documentViewModel;
        documentDock.DefaultDockable ??= documentViewModel;
        provider.GetRequiredService<CadDocumentTreeViewModel>().LoadDocument(documentViewModel);
        selectionService.SelectedObject = document;
        return workspace;
    }

    private static void EnsureStyles()
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        if (app.Styles.OfType<FluentTheme>().Any())
        {
            return;
        }

        app.Styles.Add(new FluentTheme());
        app.Styles.Add(new StyleInclude(new Uri("avares://Avalonia.Controls.DataGrid/"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.v2.xaml")
        });
        app.Styles.Add(new StyleInclude(new Uri("avares://Dock.Avalonia.Themes.Fluent/"))
        {
            Source = new Uri("avares://Dock.Avalonia.Themes.Fluent/DockFluentTheme.axaml")
        });
        app.Styles.Add(new StyleInclude(new Uri("avares://AvaloniaEdit/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml")
        });
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddCadInspectorIO();

        services.AddSingleton<FastPathDiagnosticsService>();
        services.AddSingleton<IAppLogService, AppLogService>();
        services.AddSingleton<ICadPropertyValidator, CadDefaultPropertyValidator>();
        services.AddSingleton<ICadPropertyValidator, CadFiniteNumberValidator>();
        services.AddSingleton<ICadPropertyEditPipeline, CadPropertyEditPipeline>();
        services.AddSingleton<CadDocumentDiffEngine>();
        services.AddSingleton<DxfTextDiffEngine>();
        services.AddSingleton<CadBatchQueryEngine>();
        services.AddSingleton<ICadScriptHost, CadScriptHost>();
        services.AddSingleton<CadRenderSceneSettings>();
        services.AddSingleton<IRenderCache, RenderCache>();
        services.AddSingleton<IRenderCacheStampProvider, RenderCacheStampProvider>();
        services.AddSingleton<IRenderStyleResolver, DefaultRenderStyleResolver>();
        services.AddSingleton<IRenderLinePatternResolver, DefaultRenderLinePatternResolver>();
        services.AddSingleton<IRenderShapeResolver, DefaultRenderShapeResolver>();
        services.AddSingleton<IRenderEntityOrderResolver, DefaultRenderEntityOrderResolver>();
        services.AddSingleton<IRenderXRefResolver, DefaultRenderXRefResolver>();
        services.AddSingleton<IShxFontResolver, DefaultShxFontResolver>();
        services.AddSingleton<SkiaRenderTextShaper>();
        services.AddSingleton<IRenderTextShaper>(provider =>
            new CachedRenderTextShaper(
                new ShxRenderTextShaper(
                    provider.GetRequiredService<IShxFontResolver>(),
                    provider.GetRequiredService<SkiaRenderTextShaper>()),
                provider.GetRequiredService<IRenderCache>()));
        services.AddSingleton<IRenderEntityVisibilityResolver, DefaultRenderEntityVisibilityResolver>();
        services.AddSingleton<DefaultRenderGeometrySampler>();
        services.AddSingleton<IRenderGeometrySampler>(provider =>
            new CachedRenderGeometrySampler(
                provider.GetRequiredService<DefaultRenderGeometrySampler>(),
                provider.GetRequiredService<IRenderCache>(),
                provider.GetRequiredService<IRenderCacheStampProvider>()));
        services.AddSingleton<IRenderEntityDispatcher, RenderEntityDispatcher>();
        services.AddSingleton<IRenderEntityHandler, TableRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, InsertRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, DimensionRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, LeaderRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, MultiLeaderRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, LineRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, PointRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, ArcRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, CircleRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, EllipseRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, SplineRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, PolylineRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, ModelerGeometryRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, Face3DRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, MeshRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, PolyfaceMeshRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, SolidRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, HatchRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, WipeoutRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, RasterImageRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, PdfUnderlayRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, ViewportRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, TextEntityRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, MTextRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, ProxyEntityRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, FallbackRenderHandler>();
        services.AddSingleton<ICadRenderSceneBuilder, CadRenderSceneBuilder>();

        services.AddSingleton<IStorageProviderAccessor, StorageProviderAccessor>();
        services.AddSingleton<IAppNotificationService, AvaloniaAppNotificationService>();
        services.AddSingleton<ICadFileDialogService, AvaloniaCadFileDialogService>();
        services.AddSingleton<ICadBatchFileDialogService, AvaloniaCadBatchFileDialogService>();
        services.AddSingleton<ICadBatchExportService, AvaloniaCadBatchExportService>();
        services.AddSingleton<IRenderStatsExportService, NullRenderStatsExportService>();
        services.AddSingleton<ICadClipboardService, InMemoryCadClipboardService>();
        services.AddSingleton<ICadEditorSessionFactory, CadEditorSessionFactory>();
        services.AddSingleton<CadEditorSessionHostService>();
        services.AddSingleton<ICadCommandHandler, LineCadCommand>();
        services.AddSingleton<ICadCommandHandler, XLineCadCommand>();
        services.AddSingleton<ICadCommandHandler, RayCadCommand>();
        services.AddSingleton<ICadCommandHandler, CircleCadCommand>();
        services.AddSingleton<ICadCommandHandler, ArcCadCommand>();
        services.AddSingleton<ICadCommandHandler, EllipseCadCommand>();
        services.AddSingleton<ICadCommandHandler, SplineCadCommand>();
        services.AddSingleton<ICadCommandHandler, TextCadCommand>();
        services.AddSingleton<ICadCommandHandler, MTextCadCommand>();
        services.AddSingleton<ICadCommandHandler, DimLinearCadCommand>();
        services.AddSingleton<ICadCommandHandler, DimAlignedCadCommand>();
        services.AddSingleton<ICadCommandHandler, DimRadiusCadCommand>();
        services.AddSingleton<ICadCommandHandler, DimDiameterCadCommand>();
        services.AddSingleton<ICadCommandHandler, DimAngularCadCommand>();
        services.AddSingleton<ICadCommandHandler, LeaderCadCommand>();
        services.AddSingleton<ICadCommandHandler, MLeaderCadCommand>();
        services.AddSingleton<ICadCommandHandler, HatchCadCommand>();
        services.AddSingleton<ICadCommandHandler, BoundaryCadCommand>();
        services.AddSingleton<ICadCommandHandler, PlineCadCommand>();
        services.AddSingleton<ICadCommandHandler, PointCadCommand>();
        services.AddSingleton<ICadCommandHandler, InsertCadCommand>();
        services.AddSingleton<ICadCommandHandler, XRefReloadCadCommand>();
        services.AddSingleton<ICadCommandHandler, XRefBindCadCommand>();
        services.AddSingleton<ICadCommandHandler, XRefDetachCadCommand>();
        services.AddSingleton<ICadCommandHandler, RectangCadCommand>();
        services.AddSingleton<ICadCommandHandler, PolygonCadCommand>();
        services.AddSingleton<ICadCommandHandler, MoveCadCommand>();
        services.AddSingleton<ICadCommandHandler, StretchCadCommand>();
        services.AddSingleton<ICadCommandHandler, RotateCadCommand>();
        services.AddSingleton<ICadCommandHandler, ScaleCadCommand>();
        services.AddSingleton<ICadCommandHandler, MirrorCadCommand>();
        services.AddSingleton<ICadCommandHandler, OffsetCadCommand>();
        services.AddSingleton<ICadCommandHandler, TrimCadCommand>();
        services.AddSingleton<ICadCommandHandler, ExtendCadCommand>();
        services.AddSingleton<ICadCommandHandler, BreakCadCommand>();
        services.AddSingleton<ICadCommandHandler, JoinCadCommand>();
        services.AddSingleton<ICadCommandHandler, FilletCadCommand>();
        services.AddSingleton<ICadCommandHandler, ChamferCadCommand>();
        services.AddSingleton<ICadCommandHandler, ArrayCadCommand>();
        services.AddSingleton<ICadCommandHandler, ExplodeCadCommand>();
        services.AddSingleton<ICadCommandHandler, AlignCadCommand>();
        services.AddSingleton<ICadCommandHandler, MatchPropCadCommand>();
        services.AddSingleton<ICadCommandHandler, CopyClipCadCommand>();
        services.AddSingleton<ICadCommandHandler, CutCadCommand>();
        services.AddSingleton<ICadCommandHandler, PasteClipCadCommand>();
        services.AddSingleton<ICadCommandHandler, CopyCadCommand>();
        services.AddSingleton<ICadCommandHandler, EraseCadCommand>();
        services.AddSingleton<ICadCommandHandler, UndoCadCommand>();
        services.AddSingleton<ICadCommandHandler, RedoCadCommand>();
        services.AddSingleton<ICadCommandHandler, ClearSelectionCadCommand>();
        services.AddSingleton<ICadCommandHandler, ScriptFileCadCommand>();
        services.AddSingleton<HelpCadCommand>(provider =>
            new HelpCadCommand(
                () => provider.GetRequiredService<ICadCommandRegistry>().GetRegisteredCommands()));
        services.AddSingleton<ICadCommandHandler>(provider => provider.GetRequiredService<HelpCadCommand>());
        services.AddSingleton<ICadCommandRegistry>(provider =>
        {
            var registry = new CadCommandRegistry();
            foreach (var handler in provider.GetServices<ICadCommandHandler>())
            {
                registry.Register(handler);
            }

            return registry;
        });
        services.AddSingleton<ICadCommandIntellisenseService, CadCommandIntellisenseService>();
        services.AddSingleton<ICadEditorControllerFactory, CadEditorControllerFactory>();
        services.AddSingleton<CadEditorControllerHostService>();
        services.AddSingleton<ICadInteractiveCommandAdapter, LineInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, PlineInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, XLineInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, RayInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, CircleInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, ArcInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, EllipseInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, PolygonInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, SplineInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, RectangInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, PointInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, InsertInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, TextInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, MTextInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, MoveInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, CopyInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, RotateInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, ScaleInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, MirrorInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, EraseInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, OffsetInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, StretchInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, BreakInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, TrimInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, ExtendInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, PasteClipInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, BoundaryInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, HatchInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, CopyClipInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, CutInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, ExplodeInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, JoinInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, FilletInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, ChamferInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, ArrayInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, AlignInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, MatchPropInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, DimLinearInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, DimAlignedInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, DimRadiusInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, DimDiameterInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, DimAngularInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, LeaderInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapter, MLeaderInteractiveCommandAdapter>();
        services.AddSingleton<ICadInteractiveCommandAdapterRegistry, CadInteractiveCommandAdapterRegistry>();
        services.AddSingleton<ICadSnapService, CadSnapService>();
        services.AddSingleton<ICadTrackingService, CadTrackingService>();
        services.AddSingleton<ICadGripService, CadGripService>();
        services.AddSingleton<ICadRealtimeTransportFactory, VibeCadRealtimeTransportFactory>();
        services.AddSingleton<ICadCollabSnapshotStoreFactory, InMemoryCadCollabSnapshotStoreFactory>();
        services.AddSingleton<ICadCollabService, CadCollabService>();
        services.AddSingleton<ICadCollabUiService, CadCollabUiService>();
        services.AddSingleton<ICadCommandScriptRecordingService, CadCommandScriptRecordingService>();
        services.AddSingleton<CadCommandScriptRecordingTracker>();
        services.AddSingleton<CadCollabPresenceRegistry>();
        services.AddSingleton<ICadCollabConnectionOptionsProvider, CadCollabConnectionOptionsProvider>();
        services.AddSingleton<CadCollaborationWorkspaceService>();
        services.AddSingleton<ICadCollabControlService>(provider =>
            provider.GetRequiredService<CadCollaborationWorkspaceService>());
        services.AddSingleton<ICadScriptCommandHost>(provider =>
            new CadScriptCommandHost(() => provider.GetRequiredService<ICadCommandRegistry>()));
        services.AddSingleton<CadSelectionService>();
        services.AddSingleton<CadSelectionFocusService>();
        services.AddSingleton<CadDynamicBlockOverrideService>();
        services.AddSingleton<CadDocumentContextService>();
        services.AddSingleton<CadDocumentDockService>();
        services.AddSingleton<CadScriptWorkspaceService>();
        services.AddSingleton<CadBlockEditorViewModelFactory>();
        services.AddSingleton<CadBlockEditorService>();
        services.AddSingleton<IBlockPreviewRenderer, SkiaBlockPreviewRenderer>();
        services.AddSingleton<CadBlockPreviewService>();
        services.AddSingleton<IStylePreviewRenderer, SkiaStylePreviewRenderer>();
        services.AddSingleton<CadStylePreviewService>();

        services.AddSingleton<PropertyGridViewModel>();
        services.AddSingleton<CadDocumentTreeViewModel>();
        services.AddSingleton<CadLayerToolViewModel>();
        services.AddSingleton<CadRenderOptionsToolViewModel>();
        services.AddSingleton<CadEntityTypeToolViewModel>();
        services.AddSingleton<CadBlocksToolViewModel>();
        services.AddSingleton<CadViewportsToolViewModel>();
        services.AddSingleton<CadTextStyleToolViewModel>();
        services.AddSingleton<CadTextStyleEditorToolViewModel>();
        services.AddSingleton<CadLineTypeToolViewModel>();
        services.AddSingleton<CadLineTypeEditorToolViewModel>();
        services.AddSingleton<CadDimensionStyleToolViewModel>();
        services.AddSingleton<CadPreviewViewModel>();
        services.AddSingleton<CadDxfSemanticsViewModel>();
        services.AddSingleton<CadDxfRawViewModel>();
        services.AddSingleton<CadDwgSemanticsViewModel>();
        services.AddSingleton<CadIoOptionsViewModel>();
        services.AddSingleton<CadBatchViewModel>();
        services.AddSingleton<CadScriptingViewModel>();
        services.AddSingleton<CadCommandLineViewModel>();
        services.AddSingleton<CadEditorToolPanelViewModel>();
        services.AddSingleton<CadCollaborationToolViewModel>();
        services.AddSingleton<CadLogOutputToolViewModel>();
        services.AddSingleton<WorkspaceDockFactory>();
        services.AddSingleton<WorkspaceViewModelFactory>();
        services.AddSingleton<CadCompareViewModelFactory>();
        services.AddSingleton<ShellViewModel>();

        return services.BuildServiceProvider();
    }

    private sealed class NullRenderStatsExportService : IRenderStatsExportService
    {
        public Task<RenderStatsExportResult?> SaveStatsAsync(string? suggestedFileName, CancellationToken cancellationToken)
        {
            return Task.FromResult<RenderStatsExportResult?>(null);
        }
    }
}
