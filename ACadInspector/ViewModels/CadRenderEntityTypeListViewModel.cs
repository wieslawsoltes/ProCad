using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadRenderEntityTypeListViewModel : ViewModelBase
{
    private readonly ObservableCollection<CadRenderEntityTypeRowViewModel> _rows = new();
    private bool _suppressVisibilityUpdates;

    [Reactive]
    public partial string SearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string FilterText { get; set; } = string.Empty;

    [Reactive]
    public partial IReadOnlyDictionary<string, bool>? EntityTypeVisibilityOverrides { get; set; }

    public DataGridCollectionView RowsView { get; }
    public DataGridColumnDefinitionList ColumnDefinitions { get; }
    public SortingModel SortingModel { get; } = new();
    public FilteringModel FilteringModel { get; } = new();
    public SearchModel SearchModel { get; } = new();

    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearFilterCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectNoneCommand { get; }

    public CadRenderEntityTypeListViewModel(CadDocument? document)
    {
        RowsView = new DataGridCollectionView(_rows);
        ColumnDefinitions = CadRenderEntityTypeColumnDefinitions.Create();

        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => ApplySearch());
        this.WhenAnyValue(x => x.FilterText)
            .Subscribe(_ => ApplyFilter());
        ClearSearchCommand = ReactiveCommand.Create(() => { SearchText = string.Empty; });
        ClearFilterCommand = ReactiveCommand.Create(() => { FilterText = string.Empty; });
        SelectAllCommand = ReactiveCommand.Create(() => SetAllVisibility(isVisible: true));
        SelectNoneCommand = ReactiveCommand.Create(() => SetAllVisibility(isVisible: false));

        SearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        SearchModel.HighlightCurrent = true;
        SearchModel.WrapNavigation = true;

        if (document is not null)
        {
            LoadEntityTypes(document);
        }
    }

    private void LoadEntityTypes(CadDocument document)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        AccumulateEntities(document.Entities, counts);

        if (document.BlockRecords is not null)
        {
            foreach (var block in document.BlockRecords)
            {
                if (block is null || block.Layout is not null)
                {
                    continue;
                }

                AccumulateEntities(block.Entities, counts);
            }
        }

        var names = new List<string>(counts.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var row = new CadRenderEntityTypeRowViewModel(name, counts[name], isVisible: true);
            row.VisibilityChanged += OnVisibilityChanged;
            _rows.Add(row);
        }

        RowsView.Refresh();
        UpdateVisibilityOverrides();
    }

    private void AccumulateEntities(IEnumerable<Entity>? entities, Dictionary<string, int> counts)
    {
        if (entities is null)
        {
            return;
        }

        foreach (var entity in entities)
        {
            if (entity is null)
            {
                continue;
            }

            var name = entity.GetType().Name;
            counts.TryGetValue(name, out var count);
            counts[name] = count + 1;
        }
    }

    private void OnVisibilityChanged(object? sender, EventArgs e)
    {
        if (_suppressVisibilityUpdates)
        {
            return;
        }

        UpdateVisibilityOverrides();
    }

    private void UpdateVisibilityOverrides()
    {
        if (_rows.Count == 0)
        {
            EntityTypeVisibilityOverrides = null;
            return;
        }

        Dictionary<string, bool>? map = null;
        foreach (var row in _rows)
        {
            if (!row.IsVisible)
            {
                map ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                map[row.EntityType] = false;
            }
        }

        EntityTypeVisibilityOverrides = map;
    }

    private void SetAllVisibility(bool isVisible)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        _suppressVisibilityUpdates = true;
        foreach (var row in _rows)
        {
            row.IsVisible = isVisible;
        }
        _suppressVisibilityUpdates = false;
        UpdateVisibilityOverrides();
    }

    private void ApplySearch()
    {
        DataGridFilterHelper.ApplySearch(SearchModel, SearchText);
    }

    private void ApplyFilter()
    {
        DataGridFilterHelper.ApplyFilter(FilteringModel, ColumnDefinitions, FilterText);
    }
}
