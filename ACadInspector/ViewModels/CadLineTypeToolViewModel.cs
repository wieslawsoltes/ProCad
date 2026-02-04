using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Services;
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

public sealed partial class CadLineTypeToolViewModel : CadToolViewModelBase
{
    private readonly ObservableCollection<CadLineTypeRowViewModel> _rows = new();
    private readonly Dictionary<LineType, CadLineTypeRowViewModel> _rowMap = new(ReferenceEqualityComparer.Instance);
    private readonly CadSelectionService _selectionService;
    private readonly CadDocumentContextService _documentContext;
    private readonly CadStylePreviewService _previewService;
    private CancellationTokenSource? _previewCts;
    private const int PreviewSize = 48;
    private bool _suppressSelection;

    [Reactive]
    public partial string SearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string FilterText { get; set; } = string.Empty;

    [Reactive]
    public partial CadLineTypeRowViewModel? SelectedLineType { get; set; }

    public DataGridCollectionView LineTypesView { get; }
    public DataGridColumnDefinitionList ColumnDefinitions { get; }
    public SortingModel SortingModel { get; } = new();
    public FilteringModel FilteringModel { get; } = new();
    public SearchModel SearchModel { get; } = new();

    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearFilterCommand { get; }

    public CadLineTypeToolViewModel(
        CadSelectionService selectionService,
        CadDocumentContextService documentContext,
        CadStylePreviewService previewService)
    {
        _selectionService = selectionService;
        _documentContext = documentContext;
        _previewService = previewService;

        LineTypesView = new DataGridCollectionView(_rows);
        ColumnDefinitions = CadLineTypeColumnDefinitions.Create();

        SearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        SearchModel.HighlightCurrent = true;
        SearchModel.WrapNavigation = true;

        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => ApplySearch());

        this.WhenAnyValue(x => x.FilterText)
            .Subscribe(_ => ApplyFilter());

        this.WhenAnyValue(x => x.SelectedLineType)
            .Subscribe(OnSelectedLineTypeChanged);

        _selectionService.WhenAnyValue(x => x.SelectedObject)
            .Subscribe(UpdateSelectionFromService);

        _documentContext.WhenAnyValue(x => x.ActiveDocument)
            .Subscribe(LoadLineTypes);

        ClearSearchCommand = ReactiveCommand.Create(() => { SearchText = string.Empty; });
        ClearFilterCommand = ReactiveCommand.Create(() => { FilterText = string.Empty; });
    }

    private void LoadLineTypes(CadDocumentViewModel? documentViewModel)
    {
        _rows.Clear();
        _rowMap.Clear();
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();

        var lineTypes = documentViewModel?.Document?.LineTypes;
        if (lineTypes is null)
        {
            LineTypesView.Refresh();
            return;
        }

        foreach (var lineType in lineTypes)
        {
            if (lineType is null)
            {
                continue;
            }

            var row = new CadLineTypeRowViewModel(lineType);
            _rows.Add(row);
            _rowMap[lineType] = row;
            QueuePreview(row);
        }

        LineTypesView.Refresh();
        ApplySearch();
        UpdateSelectionFromService(_selectionService.SelectedObject);
    }

    private void QueuePreview(CadLineTypeRowViewModel row)
    {
        if (_previewCts is null)
        {
            return;
        }

        var token = _previewCts.Token;
        _ = Task.Run(async () =>
        {
            var preview = await _previewService.GetLineTypePreviewAsync(row.LineType, PreviewSize, token)
                .ConfigureAwait(false);

            if (preview is null || token.IsCancellationRequested)
            {
                return;
            }

            RxApp.MainThreadScheduler.Schedule(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    row.Preview = preview;
                }
            });
        }, CancellationToken.None);
    }

    private void ApplySearch()
    {
        DataGridFilterHelper.ApplySearch(SearchModel, SearchText);
    }

    private void ApplyFilter()
    {
        DataGridFilterHelper.ApplyFilter(FilteringModel, ColumnDefinitions, FilterText);
    }

    private void OnSelectedLineTypeChanged(CadLineTypeRowViewModel? row)
    {
        if (_suppressSelection)
        {
            return;
        }

        _selectionService.SelectedObject = row?.LineType;
    }

    private void UpdateSelectionFromService(object? selected)
    {
        if (_suppressSelection)
        {
            return;
        }

        var lineType = ResolveLineType(selected);
        _suppressSelection = true;
        SelectedLineType = lineType is not null && _rowMap.TryGetValue(lineType, out var row) ? row : null;
        _suppressSelection = false;
    }

    private static LineType? ResolveLineType(object? selected)
    {
        switch (selected)
        {
            case CadDocumentTreeNode node:
                return ResolveLineType(node.Source);
            case LineType lineType:
                return lineType;
            case Entity entity:
                return entity.GetActiveLineType();
        }

        return null;
    }
}
