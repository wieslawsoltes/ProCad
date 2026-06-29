using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ProCad.Core;
using ProCad.Diagnostics;
using ProCad.Rendering;
using ProCad.Services;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class PropertyGridViewModel : CadToolViewModelBase, IFastPathDiagnosticsSource
{
    private const string FilterColumnId = "Name";
    private readonly ObservableCollection<PropertyGridRowViewModel> _rows = new();
    private readonly ReadOnlyObservableCollection<PropertyGridRowViewModel> _rowsReadOnly;

    public DataGridCollectionView RowsView { get; }
    public ReadOnlyObservableCollection<PropertyGridRowViewModel> Rows => _rowsReadOnly;
    public DataGridColumnDefinitionList ColumnDefinitions { get; }
    public FastPathDiagnosticsService FastPathDiagnostics { get; }

    public SortingModel SortingModel { get; } = new();
    public FilteringModel FilteringModel { get; } = new();
    public SearchModel SearchModel { get; } = new();

    [Reactive]
    public partial object? SelectedObject { get; set; }

    [Reactive]
    public partial bool IsActive { get; set; }

    [Reactive]
    public partial string SearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string FilterText { get; set; } = string.Empty;

    [Reactive]
    public partial string SearchSummary { get; set; } = "No results";

    [Reactive]
    public partial bool CanEditBlock { get; set; }

    public ReactiveCommand<Unit, Unit> NextSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearFilterCommand { get; }
    public ReactiveCommand<Unit, Unit> EditBlockCommand { get; }

    private readonly ICadPropertyEditPipeline _pipeline;
    private readonly IRenderCacheStampProvider _stampProvider;
    private readonly CadSelectionService _selectionService;
    private readonly CadBlockEditorService _blockEditorService;
    private readonly CadDynamicBlockOverrideService _dynamicBlockOverrides;
    private bool _suppressSelection;
    private CadDynamicBlockInspectorViewModel? _dynamicInspector;

    [Reactive]
    public partial CadDynamicBlockInspectorViewModel? DynamicBlockInspector { get; set; }

    [Reactive]
    public partial bool HasDynamicBlockInspector { get; set; }
    public PropertyGridViewModel(
        ICadPropertyEditPipeline pipeline,
        IRenderCacheStampProvider stampProvider,
        CadSelectionService selectionService,
        CadBlockEditorService blockEditorService,
        CadDynamicBlockOverrideService dynamicBlockOverrides,
        FastPathDiagnosticsService fastPathDiagnostics)
    {
        _pipeline = pipeline;
        _stampProvider = stampProvider;
        _selectionService = selectionService;
        _blockEditorService = blockEditorService;
        _dynamicBlockOverrides = dynamicBlockOverrides;
        FastPathDiagnostics = fastPathDiagnostics;
        _rowsReadOnly = new ReadOnlyObservableCollection<PropertyGridRowViewModel>(_rows);
        RowsView = new DataGridCollectionView(_rows);
        ColumnDefinitions = PropertyGridColumnDefinitions.Create();

        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => ApplySearch());

        this.WhenAnyValue(x => x.FilterText)
            .Subscribe(_ => ApplyFilter());

        this.WhenAnyValue(x => x.SelectedObject)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(selected =>
            {
                if (IsActive)
                {
                    UpdateRows(selected);
                }
            });

        _selectionService.WhenAnyValue(x => x.SelectedObject)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(selected =>
            {
                if (IsActive)
                {
                    UpdateSelectionFromService(selected);
                }
            });

        this.WhenAnyValue(x => x.SelectedObject)
            .Subscribe(PublishSelection);

        this.WhenAnyValue(x => x.SelectedObject)
            .Subscribe(UpdateDynamicInspector);

        this.WhenAnyValue(x => x.DynamicBlockInspector)
            .Select(inspector => inspector is not null)
            .Subscribe(hasInspector => HasDynamicBlockInspector = hasInspector);

        this.WhenAnyValue(x => x.SelectedObject)
            .Select(selected => _blockEditorService.CanOpen(selected))
            .Subscribe(canEdit => CanEditBlock = canEdit);

        this.WhenAnyValue(x => x.IsActive)
            .Subscribe(active =>
            {
                if (active)
                {
                    UpdateSelectionFromService(_selectionService.SelectedObject);
                }
            });

        SearchModel.ResultsChanged += (_, _) => UpdateSearchSummary();
        SearchModel.CurrentChanged += (_, _) => UpdateSearchSummary();
        SearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        SearchModel.HighlightCurrent = true;
        SearchModel.WrapNavigation = true;

        var canNavigate = Observable
            .FromEventPattern<SearchResultsChangedEventArgs>(
                handler => SearchModel.ResultsChanged += handler,
                handler => SearchModel.ResultsChanged -= handler)
            .Select(_ => SearchModel.Results.Count > 0)
            .StartWith(SearchModel.Results.Count > 0)
            .DistinctUntilChanged();
        NextSearchCommand = ReactiveCommand.Create(() => { SearchModel.MoveNext(); }, canNavigate);
        PreviousSearchCommand = ReactiveCommand.Create(() => { SearchModel.MovePrevious(); }, canNavigate);
        ClearSearchCommand = ReactiveCommand.Create(() => { SearchText = string.Empty; });
        ClearFilterCommand = ReactiveCommand.Create(() => { FilterText = string.Empty; });
        EditBlockCommand = ReactiveCommand.Create(
            () => { _blockEditorService.TryOpenBlockEditor(SelectedObject); },
            this.WhenAnyValue(x => x.CanEditBlock));
    }

    private void UpdateRows(object? target)
    {
        _rows.Clear();

        if (target is null)
        {
            return;
        }

        var descriptor = FindDescriptor(target.GetType());
        if (descriptor is null)
        {
            return;
        }

        foreach (var property in descriptor.Properties)
        {
            _rows.Add(new PropertyGridRowViewModel(target, property, _pipeline, _stampProvider, RefreshRows));
        }

        RowsView.Refresh();
        ApplyFilter();
        ApplySearch();
    }

    private void UpdateDynamicInspector(object? target)
    {
        _dynamicInspector?.Dispose();
        _dynamicInspector = null;
        DynamicBlockInspector = null;

        if (target is not ACadSharp.Entities.Insert insert || insert.Block is null)
        {
            return;
        }

        if (insert.Block.EvaluationGraph is null)
        {
            return;
        }

        var inspector = new CadDynamicBlockInspectorViewModel(insert, insert.Block, _dynamicBlockOverrides);
        _dynamicInspector = inspector;
        DynamicBlockInspector = inspector;
    }

    private static CadTypeDescriptor? FindDescriptor(Type type)
    {
        if (CadMetadataRegistry.Types.TryGetValue(type, out var descriptor))
        {
            return descriptor;
        }

        var current = type.BaseType;
        while (current is not null)
        {
            if (CadMetadataRegistry.Types.TryGetValue(current, out descriptor))
            {
                return descriptor;
            }

            current = current.BaseType;
        }

        return null;
    }

    private void RefreshRows()
    {
        foreach (var row in _rows)
        {
            row.RefreshValue();
        }

        RowsView.Refresh();
    }

    private void ApplySearch()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchModel.Clear();
            UpdateSearchSummary();
            return;
        }

        var descriptor = new SearchDescriptor(
            SearchText.Trim(),
            matchMode: SearchMatchMode.Contains,
            termMode: SearchTermCombineMode.Any,
            scope: SearchScope.VisibleColumns,
            comparison: StringComparison.OrdinalIgnoreCase);

        SearchModel.SetOrUpdate(descriptor);
        UpdateSearchSummary();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(FilterText))
        {
            FilteringModel.Remove(FilterColumnId);
            return;
        }

        var text = FilterText.Trim();
        FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: FilterColumnId,
            @operator: FilteringOperator.Custom,
            propertyPath: FilterColumnId,
            predicate: item => MatchesFilter(item, text)));
    }

    private void UpdateSearchSummary()
    {
        var count = SearchModel.Results.Count;
        var current = SearchModel.CurrentIndex >= 0 ? SearchModel.CurrentIndex + 1 : 0;

        if (count == 0)
        {
            SearchSummary = "No results";
        }
        else if (current == 0)
        {
            SearchSummary = $"{count:n0} results";
        }
        else
        {
            SearchSummary = $"{current:n0} of {count:n0}";
        }
    }

    private static bool MatchesFilter(object item, string text)
    {
        if (item is not PropertyGridRowViewModel row)
        {
            return false;
        }

        return row.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               row.TypeName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               row.DxfCodes.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               row.DxfReferenceTypeText.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               row.ValueText.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               row.ValidationMessageText.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSelectionFromService(object? selected)
    {
        if (_suppressSelection)
        {
            return;
        }

        _suppressSelection = true;
        SelectedObject = selected;
        _suppressSelection = false;
    }

    private void PublishSelection(object? selected)
    {
        if (_suppressSelection)
        {
            return;
        }

        _selectionService.SelectedObject = selected;
    }
}
