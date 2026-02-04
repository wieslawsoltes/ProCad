using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector;
using ACadInspector.Core;
using ACadInspector.Diagnostics;
using ACadInspector.Docking;
using ACadInspector.IO;
using ACadInspector.Rendering;
using ACadInspector.Scripting;
using ACadInspector.Services;
using ACadInspector.ViewModels;
using ACadInspector.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
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
        services.AddSingleton<IRenderStatsExportService, NullRenderStatsExportService>();
        services.AddSingleton<CadSelectionService>();
        services.AddSingleton<CadSelectionFocusService>();
        services.AddSingleton<CadDynamicBlockOverrideService>();
        services.AddSingleton<CadDocumentContextService>();
        services.AddSingleton<CadDocumentDockService>();
        services.AddSingleton<CadScriptWorkspaceService>();
        services.AddSingleton<CadBlockEditorViewModelFactory>();
        services.AddSingleton<CadBlockEditorService>();
        services.AddSingleton<CadBlockPreviewService>();
        services.AddSingleton<CadStylePreviewService>();

        services.AddSingleton<PropertyGridViewModel>();
        services.AddSingleton<CadDocumentTreeViewModel>();
        services.AddSingleton<CadLayerToolViewModel>();
        services.AddSingleton<CadBlocksToolViewModel>();
        services.AddSingleton<CadTextStyleToolViewModel>();
        services.AddSingleton<CadLineTypeToolViewModel>();
        services.AddSingleton<CadDimensionStyleToolViewModel>();
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

    private sealed class NullRenderStatsExportService : IRenderStatsExportService
    {
        public Task<RenderStatsExportResult?> SaveStatsAsync(string? suggestedFileName, CancellationToken cancellationToken)
        {
            return Task.FromResult<RenderStatsExportResult?>(null);
        }
    }
}
