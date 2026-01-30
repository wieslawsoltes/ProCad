using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Core;
using ACadInspector.Rendering;
using ACadSharp;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadRenderViewModel : ViewModelBase
{
    private readonly ObservableCollection<CadRenderLayerRowViewModel> _layerRows = new();

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

    [Reactive]
    public partial string LayerSearchText { get; set; } = string.Empty;

    [Reactive]
    public partial IReadOnlyDictionary<string, bool>? LayerVisibilityOverrides { get; set; }

    public DataGridCollectionView LayerRowsView { get; }
    public DataGridColumnDefinitionList LayerColumnDefinitions { get; }
    public SortingModel LayerSortingModel { get; } = new();
    public FilteringModel LayerFilteringModel { get; } = new();
    public SearchModel LayerSearchModel { get; } = new();

    public ReactiveCommand<Unit, Unit> FitCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportStatsCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearLayerSearchCommand { get; }

    private readonly IRenderStatsExportService _statsExportService;
    private readonly string _statsFileName;

    public CadRenderViewModel(
        CadDocument document,
        RenderScene? scene,
        IRenderStatsExportService statsExportService,
        string? statsFileName)
    {
        Scene = scene;
        _statsExportService = statsExportService;
        _statsFileName = EnsureStatsFileName(statsFileName);

        LayerRowsView = new DataGridCollectionView(_layerRows);
        LayerColumnDefinitions = CadRenderLayerColumnDefinitions.Create();

        var canExport = this.WhenAnyValue(x => x.Scene)
            .Select(scene => scene is not null);
        FitCommand = ReactiveCommand.Create(RequestFit);
        ResetCommand = ReactiveCommand.Create(ResetView);
        ExportStatsCommand = ReactiveCommand.CreateFromTask(ExportStatsAsync, canExport);

        this.WhenAnyValue(x => x.LayerSearchText)
            .Subscribe(_ => ApplyLayerSearch());
        ClearLayerSearchCommand = ReactiveCommand.Create(() => { LayerSearchText = string.Empty; });

        LayerSearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        LayerSearchModel.HighlightCurrent = true;
        LayerSearchModel.WrapNavigation = true;

        LoadLayers(document);
    }

    private void RequestFit()
    {
        FitRequest++;
    }

    private void ResetView()
    {
        ResetRequest++;
    }

    private void LoadLayers(CadDocument document)
    {
        if (document.Layers is null)
        {
            return;
        }

        foreach (var layer in document.Layers)
        {
            var row = new CadRenderLayerRowViewModel(layer);
            row.VisibilityChanged += OnLayerVisibilityChanged;
            _layerRows.Add(row);
        }

        LayerRowsView.Refresh();
        UpdateLayerVisibilityOverrides();
    }

    private void OnLayerVisibilityChanged(object? sender, EventArgs e)
    {
        UpdateLayerVisibilityOverrides();
    }

    private void UpdateLayerVisibilityOverrides()
    {
        if (_layerRows.Count == 0)
        {
            LayerVisibilityOverrides = null;
            return;
        }

        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _layerRows)
        {
            map[row.Name] = row.IsVisible;
        }

        LayerVisibilityOverrides = map;
    }

    private void ApplyLayerSearch()
    {
        if (string.IsNullOrWhiteSpace(LayerSearchText))
        {
            LayerSearchModel.Clear();
            return;
        }

        var descriptor = new SearchDescriptor(
            LayerSearchText.Trim(),
            matchMode: SearchMatchMode.Contains,
            termMode: SearchTermCombineMode.Any,
            scope: SearchScope.VisibleColumns,
            comparison: StringComparison.OrdinalIgnoreCase);

        LayerSearchModel.SetOrUpdate(descriptor);
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
