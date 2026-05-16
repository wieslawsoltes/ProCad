using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Markup.Xaml;
using ACadInspector.Core;
using ACadInspector.Collaboration.Presence;
using ACadInspector.Collaboration.Services;
using ACadInspector.Collaboration.Snapshots;
using ACadInspector.Collaboration.Transports;
using ACadInspector.Collaboration.UI;
using ACadInspector.Commands;
using ACadInspector.Diagnostics;
using ACadInspector.Docking;
using ACadInspector.Editing.Clipboard;
using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Controllers;
using ACadInspector.Editing.Interaction;
using ACadInspector.Editing.Prompt;
using ACadInspector.Editing.Sessions;
using ACadInspector.IO;
using ACadInspector.Services;
using ACadInspector.Scripting;
using ACadInspector.Rendering;
using ACadInspector.Rendering.Backends;
using ACadInspector.ViewModels;
using ACadInspector.Views;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using Splat;

namespace ACadInspector;

public partial class App : Application
{
    private IServiceProvider? _services;

    public override void Initialize()
    {
        AppLog.Write("App.Initialize");
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppLog.Write("OnFrameworkInitializationCompleted start.");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Console.Error.WriteLine($"Unhandled exception: {args.ExceptionObject}");
            AppLog.Error("Unhandled exception.", exception: args.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Console.Error.WriteLine($"Unobserved task exception: {args.Exception}");
            AppLog.Error("Unobserved task exception.", exception: args.Exception);
        };

        try
        {
            AppLog.Write("ConfigureServices start.");
            _services = ConfigureServices();
            AppLog.Configure(_services.GetRequiredService<IAppLogService>());
            AppLog.Write("ConfigureServices done.");
            AppLog.Write("ConfigureReactiveUi start.");
            ConfigureReactiveUi();
            AppLog.Write("ConfigureReactiveUi done.");
            RenderBackendRegistry.Factory = _services.GetRequiredService<IRenderBackendFactory>();
            _ = _services.GetRequiredService<CadCommandScriptRecordingTracker>();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Startup failure: {ex}");
            AppLog.Critical("Startup failure.", exception: ex);
            throw;
        }

        AppLog.Write("Resolving ShellViewModel.");
        var shell = _services.GetRequiredService<ShellViewModel>();
        AppLog.Write("ShellViewModel resolved.");
        var storageAccessor = _services.GetRequiredService<IStorageProviderAccessor>();
        var clipboardAccessor = _services.GetRequiredService<IClipboardAccessor>();
        var notificationService = _services.GetRequiredService<IAppNotificationService>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppLog.Write("Creating MainWindow.");
            var mainWindow = new MainWindow
            {
                DataContext = shell
            };
            notificationService.SetManager(new WindowNotificationManager(mainWindow));
            desktop.MainWindow = mainWindow;
            desktop.MainWindow.Opened += (_, _) => AppLog.Write("MainWindow opened.");
            desktop.MainWindow.Activated += (_, _) => AppLog.Write("MainWindow activated.");
            desktop.MainWindow.Closed += (_, _) =>
            {
                AppLog.Write("MainWindow closed.");
                DisposeWorkspaceScopedServices();
            };

            storageAccessor.SetProvider(() => desktop.MainWindow?.StorageProvider);
            clipboardAccessor.SetProvider(() => desktop.MainWindow?.Clipboard);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            AppLog.Write("Creating MainView.");
            singleViewPlatform.MainView = new MainView
            {
                DataContext = shell
            };

            storageAccessor.SetProvider(() =>
                TopLevel.GetTopLevel(singleViewPlatform.MainView)?.StorageProvider);
            clipboardAccessor.SetProvider(() =>
                TopLevel.GetTopLevel(singleViewPlatform.MainView)?.Clipboard);
        }
        else
        {
            AppLog.Write($"Unknown ApplicationLifetime: {ApplicationLifetime?.GetType().FullName ?? "null"}");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisposeWorkspaceScopedServices()
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            _services.GetService<CadEditorControllerHostService>()?.Clear();
            _services.GetService<CadEditorSessionHostService>()?.Clear();
            _services.GetService<CadCollaborationWorkspaceService>()?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error("DisposeWorkspaceScopedServices failed.", exception: ex);
        }

        if (_services is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private IServiceProvider ConfigureServices()
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
        services.AddSingleton<IRenderBackendFactory, SkiaRenderBackendFactory>();
        services.AddSingleton<CadRenderSceneSettings>();
        services.AddSingleton<IRenderCache, RenderCache>();
        services.AddSingleton<IRenderCacheStampProvider, RenderCacheStampProvider>();
        services.AddSingleton<IRenderStyleResolver, DefaultRenderStyleResolver>();
        services.AddSingleton<IRenderLinePatternResolver, DefaultRenderLinePatternResolver>();
        services.AddSingleton<IRenderShapeResolver, DefaultRenderShapeResolver>();
        services.AddSingleton<IRenderEntityOrderResolver, DefaultRenderEntityOrderResolver>();
        services.AddSingleton<IRenderXRefResolver, DefaultRenderXRefResolver>();
        services.AddSingleton<IShxFontResolver, DefaultShxFontResolver>();
        services.AddSingleton<IUnicodeTextService, UnicodeTextService>();
        services.AddSingleton<SkiaRenderTextShaper>();
        services.AddSingleton<HarfBuzzRenderTextShaper>();
        services.AddSingleton<IRenderTextShaper>(provider =>
            new CachedRenderTextShaper(
                new ShxRenderTextShaper(
                    provider.GetRequiredService<IShxFontResolver>(),
                    provider.GetRequiredService<HarfBuzzRenderTextShaper>()),
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
        services.AddSingleton<IRenderEntityHandler, MLineRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, LineRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, RayRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, XLineRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, PointRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, ArcRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, CircleRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, EllipseRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, SplineRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, PolylineRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, PolygonMeshRenderHandler>();
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
        services.AddSingleton<IRenderEntityHandler, ShapeRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, TextEntityRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, MTextRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, Ole2FrameRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, ToleranceRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, ProxyEntityRenderHandler>();
        services.AddSingleton<IRenderEntityHandler, FallbackRenderHandler>();
        services.AddSingleton<ICadRenderSceneBuilder, CadRenderSceneBuilder>();

        services.AddSingleton<IStorageProviderAccessor, StorageProviderAccessor>();
        services.AddSingleton<IClipboardAccessor, ClipboardAccessor>();
        services.AddSingleton<IAppNotificationService, AvaloniaAppNotificationService>();
        services.AddSingleton<ICadFileDialogService, AvaloniaCadFileDialogService>();
        services.AddSingleton<ICadBatchFileDialogService, AvaloniaCadBatchFileDialogService>();
        services.AddSingleton<ICadBatchExportService, AvaloniaCadBatchExportService>();
        services.AddSingleton<IRenderStatsExportService, AvaloniaRenderStatsExportService>();
        services.AddSingleton<ICadSystemClipboardBridge, AvaloniaCadSystemClipboardBridge>();
        services.AddSingleton<ICadClipboardPlatformFacade, AvaloniaClipboardPlatformFacade>();
        services.AddSingleton<ICadClipboardService, SystemClipboardCadClipboardService>();
        services.AddSingleton<ICadEditorSessionFactory, CadEditorSessionFactory>();
        services.AddSingleton<CadEditorSessionHostService>();
        services.AddSingleton<ICadCommandScriptRecordingService, CadCommandScriptRecordingService>();
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
        services.AddSingleton<ICadCommandHandler, ScriptRecordCadCommand>();
        services.AddSingleton<ICadCommandHandler, ScriptRecordSaveCadCommand>();
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
        services.AddSingleton<CadCommandScriptRecordingTracker>();
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
        services.AddSingleton<ICadScriptCommandHost>(provider =>
            new CadScriptCommandHost(() => provider.GetRequiredService<ICadCommandRegistry>()));
        services.AddSingleton<ICadRealtimeTransportFactory, ProEditCadRealtimeTransportFactory>();
        services.AddSingleton<ICadCollabService, CadCollabService>();
        services.AddSingleton<ICadCollabUiService, CadCollabUiService>();
        services.AddSingleton<ICadCollabConnectionOptionsProvider, CadCollabConnectionOptionsProvider>();
        services.AddSingleton<CadCollaborationWorkspaceService>();
        services.AddSingleton<ICadCollabControlService>(provider =>
            provider.GetRequiredService<CadCollaborationWorkspaceService>());
        services.AddSingleton<CadCollabPresenceRegistry>();
        services.AddSingleton<ICadCollabSnapshotStoreFactory>(_ =>
        {
            if (OperatingSystem.IsBrowser())
            {
                return new BrowserCadCollabSnapshotStoreFactory();
            }

            var basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ACadInspector",
                "Collaboration");
            return new FileCadCollabSnapshotStoreFactory(basePath);
        });
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

    private static void ConfigureReactiveUi()
    {
        Locator.CurrentMutable.RegisterConstant<IViewLocator>(new AppViewLocator());
    }
}
