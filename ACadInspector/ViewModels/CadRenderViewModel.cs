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

    [Reactive]
    public partial CadRenderLayoutViewModel? SelectedLayout { get; set; }

    public ReactiveCommand<Unit, Unit> FitCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportStatsCommand { get; }

    private readonly IRenderStatsExportService _statsExportService;
    private readonly string _statsFileName;

    public CadRenderViewModel(
        CadDocument document,
        RenderScene? scene,
        ICadRenderSceneBuilder sceneBuilder,
        CadRenderSceneSettings baseSettings,
        CadRenderLayoutSelection selection,
        string? documentPath,
        IRenderStatsExportService statsExportService,
        string? statsFileName)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _documentPath = documentPath;
        _sceneBuilder = sceneBuilder ?? throw new ArgumentNullException(nameof(sceneBuilder));
        _baseSettings = baseSettings ?? throw new ArgumentNullException(nameof(baseSettings));
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

        _suppressLayoutUpdates = true;
        SelectedLayout = ResolveSelectedLayout(selection);
        _suppressLayoutUpdates = false;

        this.WhenAnyValue(x => x.SelectedLayout)
            .Where(layout => layout is not null)
            .Subscribe(layout => OnLayoutChanged(layout!));

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
