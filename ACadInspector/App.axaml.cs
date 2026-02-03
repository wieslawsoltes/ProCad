using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ACadInspector.Core;
using ACadInspector.Diagnostics;
using ACadInspector.Docking;
using ACadInspector.IO;
using ACadInspector.Services;
using ACadInspector.Scripting;
using ACadInspector.Rendering;
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
            AppLog.Write($"Unhandled exception: {args.ExceptionObject}");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Console.Error.WriteLine($"Unobserved task exception: {args.Exception}");
            AppLog.Write($"Unobserved task exception: {args.Exception}");
        };

        try
        {
            AppLog.Write("ConfigureServices start.");
            _services = ConfigureServices();
            AppLog.Write("ConfigureServices done.");
            AppLog.Write("ConfigureReactiveUi start.");
            ConfigureReactiveUi();
            AppLog.Write("ConfigureReactiveUi done.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Startup failure: {ex}");
            AppLog.Write($"Startup failure: {ex}");
            throw;
        }

        AppLog.Write("Resolving ShellViewModel.");
        var shell = _services.GetRequiredService<ShellViewModel>();
        AppLog.Write("ShellViewModel resolved.");
        var storageAccessor = _services.GetRequiredService<IStorageProviderAccessor>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppLog.Write("Creating MainWindow.");
            desktop.MainWindow = new MainWindow
            {
                DataContext = shell
            };
            desktop.MainWindow.Opened += (_, _) => AppLog.Write("MainWindow opened.");
            desktop.MainWindow.Activated += (_, _) => AppLog.Write("MainWindow activated.");
            desktop.MainWindow.Closed += (_, _) => AppLog.Write("MainWindow closed.");

            storageAccessor.SetProvider(() => desktop.MainWindow?.StorageProvider);
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
        }
        else
        {
            AppLog.Write($"Unknown ApplicationLifetime: {ApplicationLifetime?.GetType().FullName ?? "null"}");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddCadInspectorIO();

        services.AddSingleton<FastPathDiagnosticsService>();
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
        services.AddSingleton<ICadFileDialogService, AvaloniaCadFileDialogService>();
        services.AddSingleton<ICadBatchFileDialogService, AvaloniaCadBatchFileDialogService>();
        services.AddSingleton<ICadBatchExportService, AvaloniaCadBatchExportService>();
        services.AddSingleton<IRenderStatsExportService, AvaloniaRenderStatsExportService>();
        services.AddSingleton<CadSelectionService>();
        services.AddSingleton<CadDocumentContextService>();
        services.AddSingleton<CadScriptWorkspaceService>();

        services.AddSingleton<PropertyGridViewModel>();
        services.AddSingleton<CadDocumentTreeViewModel>();
        services.AddSingleton<CadLayerToolViewModel>();
        services.AddSingleton<CadPreviewViewModel>();
        services.AddSingleton<CadDxfSemanticsViewModel>();
        services.AddSingleton<CadDxfRawViewModel>();
        services.AddSingleton<CadDwgSemanticsViewModel>();
        services.AddSingleton<CadIoOptionsViewModel>();
        services.AddSingleton<CadBatchViewModel>();
        services.AddSingleton<CadScriptingViewModel>();
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
