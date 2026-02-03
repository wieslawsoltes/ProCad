using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using ACadSharp;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadRenderLayerListViewModel : ViewModelBase
{
    private readonly ObservableCollection<CadRenderLayerRowViewModel> _layerRows = new();

    [Reactive]
    public partial string LayerSearchText { get; set; } = string.Empty;

    [Reactive]
    public partial IReadOnlyDictionary<string, bool>? LayerVisibilityOverrides { get; set; }

    public DataGridCollectionView LayerRowsView { get; }
    public DataGridColumnDefinitionList LayerColumnDefinitions { get; }
    public SortingModel LayerSortingModel { get; } = new();
    public FilteringModel LayerFilteringModel { get; } = new();
    public SearchModel LayerSearchModel { get; } = new();

    public ReactiveCommand<Unit, Unit> ClearLayerSearchCommand { get; }

    public CadRenderLayerListViewModel(CadDocument? document)
    {
        LayerRowsView = new DataGridCollectionView(_layerRows);
        LayerColumnDefinitions = CadRenderLayerColumnDefinitions.Create();

        this.WhenAnyValue(x => x.LayerSearchText)
            .Subscribe(_ => ApplyLayerSearch());
        ClearLayerSearchCommand = ReactiveCommand.Create(() => { LayerSearchText = string.Empty; });

        LayerSearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        LayerSearchModel.HighlightCurrent = true;
        LayerSearchModel.WrapNavigation = true;

        if (document is not null)
        {
            LoadLayers(document);
        }
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
}
