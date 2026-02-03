using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Core;
using ACadInspector.Rendering;
using ACadInspector.Services;
using ACadSharp;
using ACadSharp.Objects;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadRenderViewModel : ViewModelBase
{
    private readonly CadDocument _document;
    private readonly string? _documentPath;
    private readonly ICadRenderSceneBuilder _sceneBuilder;
    private readonly CadRenderSceneSettings _baseSettings;
    private readonly CadSelectionService _selectionService;
    private readonly CadSelectionAnnotationService _annotationService;
    private readonly CadToolManager _toolManager;
    private readonly List<RenderBounds> _bvhBounds = new();
    private RenderSpatialIndex? _hitTestIndex;
    private CancellationTokenSource? _layoutRebuildCts;
    private bool _suppressLayoutUpdates;

    [Reactive]
    public partial RenderScene? Scene { get; set; }

    [Reactive]
    public partial bool ShowGrid { get; set; } = true;

    [Reactive]
    public partial bool ShowAxes { get; set; } = true;

    [Reactive]
    public partial bool FitOnLoad { get; set; } = true;

    [Reactive]
    public partial int FitRequest { get; set; }

    [Reactive]
    public partial int ResetRequest { get; set; }

    public CadRenderLayerListViewModel LayerList { get; }
    public IReadOnlyList<CadRenderLayoutViewModel> Layouts { get; }
    public CadToolManager ToolManager => _toolManager;

    [Reactive]
    public partial CadRenderLayoutViewModel? SelectedLayout { get; set; }

    [Reactive]
    public partial object? HoveredObject { get; set; }

    [Reactive]
    public partial object? SelectedObject { get; set; }

    [Reactive]
    public partial bool ShowDebugOverlay { get; set; }

    [Reactive]
    public partial int DebugBvhDepth { get; set; } = 1;

    [Reactive]
    public partial RenderBounds? HoverBounds { get; set; }

    [Reactive]
    public partial RenderBounds? SelectionBounds { get; set; }

    [Reactive]
    public partial RenderAnnotation? HoverAnnotation { get; set; }

    [Reactive]
    public partial RenderAnnotation? SelectionAnnotation { get; set; }

    [Reactive]
    public partial IReadOnlyList<RenderBounds>? DebugBvhBounds { get; set; }

    public ReactiveCommand<Unit, Unit> FitCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportStatsCommand { get; }
    public ReactiveCommand<CadRenderHitTestRequest, Unit> HoverHitTestCommand { get; }
    public ReactiveCommand<CadRenderHitTestRequest, Unit> SelectHitTestCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearHoverCommand { get; }

    private readonly IRenderStatsExportService _statsExportService;
    private readonly string _statsFileName;

    public CadRenderViewModel(
        CadDocument document,
        RenderScene? scene,
        ICadRenderSceneBuilder sceneBuilder,
        CadRenderSceneSettings baseSettings,
        CadRenderLayoutSelection selection,
        string? documentPath,
        CadSelectionService selectionService,
        IRenderStatsExportService statsExportService,
        string? statsFileName)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _documentPath = documentPath;
        _sceneBuilder = sceneBuilder ?? throw new ArgumentNullException(nameof(sceneBuilder));
        _baseSettings = baseSettings ?? throw new ArgumentNullException(nameof(baseSettings));
        _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
        _annotationService = new CadSelectionAnnotationService();
        _toolManager = new CadToolManager();
        Scene = scene;
        LayerList = new CadRenderLayerListViewModel(document);
        Layouts = BuildLayouts(document);
        _statsExportService = statsExportService;
        _statsFileName = EnsureStatsFileName(statsFileName);

        var canExport = this.WhenAnyValue(x => x.Scene)
            .Select(scene => scene is not null);
        FitCommand = ReactiveCommand.Create(RequestFit);
        ResetCommand = ReactiveCommand.Create(ResetView);
        ExportStatsCommand = ReactiveCommand.CreateFromTask(ExportStatsAsync, canExport);
        HoverHitTestCommand = ReactiveCommand.Create<CadRenderHitTestRequest>(request =>
            HandleToolInput(CadToolInput.Hover(request)));
        SelectHitTestCommand = ReactiveCommand.Create<CadRenderHitTestRequest>(request =>
            HandleToolInput(CadToolInput.Select(request)));
        ClearHoverCommand = ReactiveCommand.Create(() =>
            HandleToolInput(CadToolInput.ClearHover()));

        _suppressLayoutUpdates = true;
        SelectedLayout = ResolveSelectedLayout(selection);
        _suppressLayoutUpdates = false;

        this.WhenAnyValue(x => x.SelectedLayout)
            .Where(layout => layout is not null)
            .Subscribe(layout => OnLayoutChanged(layout!));

        this.WhenAnyValue(x => x.Scene)
            .Subscribe(_ => OnSceneChanged());

        this.WhenAnyValue(x => x.LayerList.LayerVisibilityOverrides)
            .Subscribe(_ => UpdateHitTestIndex());

        this.WhenAnyValue(x => x.ShowDebugOverlay, x => x.DebugBvhDepth, x => x.Scene)
            .Subscribe(_ => UpdateDebugOverlay());

        _selectionService.WhenAnyValue(x => x.SelectedObject)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(UpdateSelectionFromService);

        _annotationService.WhenAnyValue(x => x.HoverAnnotation)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(annotation =>
            {
                HoverAnnotation = annotation;
                HoverBounds = annotation?.Bounds;
                UpdateDebugOverlay();
            });

        _annotationService.WhenAnyValue(x => x.SelectionAnnotation)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(annotation =>
            {
                SelectionAnnotation = annotation;
                SelectionBounds = annotation?.Bounds;
                UpdateDebugOverlay();
            });

        _annotationService.WhenAnyValue(x => x.HoveredObject)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(obj => HoveredObject = obj);

        RegisterDefaultTools();

        if (Scene is null && SelectedLayout is not null)
        {
            _ = RebuildSceneAsync(SelectedLayout);
        }
    }

    private void RequestFit()
    {
        FitRequest++;
    }

    private void ResetView()
    {
        ResetRequest++;
    }

    private void OnSceneChanged()
    {
        _annotationService.UpdateScene(Scene);
        _annotationService.ClearHover();
        UpdateHitTestIndex();
        UpdateDebugOverlay();
        UpdateSelectionFromService(_selectionService.SelectedObject);
    }

    private IReadOnlyList<CadRenderLayoutViewModel> BuildLayouts(CadDocument document)
    {
        var layouts = new List<CadRenderLayoutViewModel>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var modelName = Layout.ModelLayoutName;
        layouts.Add(new CadRenderLayoutViewModel(modelName, isPaperSpace: false, displayName: "Model"));
        names.Add(modelName);

        if (document.Layouts is not null)
        {
            var ordered = new List<Layout>();
            foreach (var layout in document.Layouts)
            {
                if (layout.IsPaperSpace)
                {
                    ordered.Add(layout);
                }
            }

            ordered.Sort(static (left, right) => left.TabOrder.CompareTo(right.TabOrder));
            foreach (var layout in ordered)
            {
                if (names.Add(layout.Name))
                {
                    layouts.Add(new CadRenderLayoutViewModel(layout.Name, isPaperSpace: true));
                }
            }
        }

        return layouts;
    }

    private CadRenderLayoutViewModel? ResolveSelectedLayout(CadRenderLayoutSelection selection)
    {
        if (!selection.IsPaperSpace)
        {
            return Layouts.Count > 0 ? Layouts[0] : null;
        }

        if (!string.IsNullOrWhiteSpace(selection.LayoutName))
        {
            foreach (var layout in Layouts)
            {
                if (layout.IsPaperSpace && string.Equals(layout.Name, selection.LayoutName, StringComparison.OrdinalIgnoreCase))
                {
                    return layout;
                }
            }
        }

        foreach (var layout in Layouts)
        {
            if (layout.IsPaperSpace)
            {
                return layout;
            }
        }

        return Layouts.Count > 0 ? Layouts[0] : null;
    }

    private void OnLayoutChanged(CadRenderLayoutViewModel layout)
    {
        if (_suppressLayoutUpdates)
        {
            return;
        }

        _ = RebuildSceneAsync(layout);
    }

    private async Task RebuildSceneAsync(CadRenderLayoutViewModel layout)
    {
        _layoutRebuildCts?.Cancel();
        var cts = new CancellationTokenSource();
        _layoutRebuildCts = cts;

        var selection = layout.IsPaperSpace
            ? new CadRenderLayoutSelection(true, layout.Name)
            : CadRenderLayoutSelection.ModelSpace;
        var settings = CadRenderSettingsBuilder.Build(_document, _documentPath, _baseSettings, selection);

        RenderScene? scene = null;
        try
        {
            scene = await Task.Run(() => _sceneBuilder.Build(_document, settings), cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested)
        {
            return;
        }

        RxApp.MainThreadScheduler.Schedule(() =>
        {
            Scene = scene;
            RequestFit();
        });
    }

    private void HandleToolInput(CadToolInput input)
    {
        _toolManager.HandleInput(input, BuildToolContext());
    }

    private CadToolContext BuildToolContext()
    {
        return new CadToolContext(Scene, _hitTestIndex, _selectionService, _annotationService);
    }

    private void RegisterDefaultTools()
    {
        var context = BuildToolContext();
        _toolManager.RegisterTool(new CadSelectionTool(), context, activate: true);
    }

    private void UpdateSelectionFromService(object? selected)
    {
        SelectedObject = selected;
        _annotationService.UpdateSelection(selected, null);
    }

    private void UpdateHitTestIndex()
    {
        var scene = Scene;
        if (scene is null)
        {
            _hitTestIndex = null;
            return;
        }

        var overrides = LayerList.LayerVisibilityOverrides;
        if (overrides is null)
        {
            _hitTestIndex = scene.SpatialIndex;
            return;
        }

        var visibleLayers = new List<RenderLayer>(scene.Layers.Count);
        foreach (var layer in scene.Layers)
        {
            var visible = layer.IsVisible;
            if (overrides.TryGetValue(layer.Name, out var overrideVisible))
            {
                visible = overrideVisible;
            }

            if (visible)
            {
                visibleLayers.Add(layer);
            }
        }

        _hitTestIndex = RenderSpatialIndex.Build(visibleLayers);
    }

    private void UpdateDebugOverlay()
    {
        if (!ShowDebugOverlay)
        {
            DebugBvhBounds = null;
            return;
        }

        var scene = Scene;
        if (scene is null)
        {
            DebugBvhBounds = null;
            return;
        }

        var index = _hitTestIndex ?? scene.SpatialIndex;
        if (index is null)
        {
            DebugBvhBounds = null;
            return;
        }

        _bvhBounds.Clear();
        index.CollectNodeBounds(DebugBvhDepth, _bvhBounds);
        DebugBvhBounds = _bvhBounds.Count == 0 ? null : _bvhBounds.ToArray();
    }

    private async Task ExportStatsAsync(CancellationToken cancellationToken)
    {
        var scene = Scene;
        if (scene is null)
        {
            return;
        }

        var result = await _statsExportService
            .SaveStatsAsync(_statsFileName, cancellationToken)
            .ConfigureAwait(true);
        if (result is null)
        {
            return;
        }

        var json = RenderStatsExporter.ToJson(scene.Stats, indented: true);
        await using var stream = await result.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static string EnsureStatsFileName(string? statsFileName)
    {
        if (string.IsNullOrWhiteSpace(statsFileName))
        {
            return "render-stats.json";
        }

        var extension = Path.GetExtension(statsFileName);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return statsFileName;
        }

        return $"{Path.GetFileNameWithoutExtension(statsFileName)}.json";
    }
}
