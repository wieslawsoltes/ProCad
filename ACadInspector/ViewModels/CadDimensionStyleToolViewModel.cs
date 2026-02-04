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

public sealed partial class CadDimensionStyleToolViewModel : CadToolViewModelBase
{
    private readonly ObservableCollection<CadDimensionStyleRowViewModel> _rows = new();
    private readonly Dictionary<DimensionStyle, CadDimensionStyleRowViewModel> _rowMap = new(ReferenceEqualityComparer.Instance);
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
    public partial CadDimensionStyleRowViewModel? SelectedStyle { get; set; }

    public DataGridCollectionView StylesView { get; }
    public DataGridColumnDefinitionList ColumnDefinitions { get; }
    public SortingModel SortingModel { get; } = new();
    public FilteringModel FilteringModel { get; } = new();
    public SearchModel SearchModel { get; } = new();

    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearFilterCommand { get; }

    public CadDimensionStyleToolViewModel(
        CadSelectionService selectionService,
        CadDocumentContextService documentContext,
        CadStylePreviewService previewService)
    {
        _selectionService = selectionService;
        _documentContext = documentContext;
        _previewService = previewService;

        StylesView = new DataGridCollectionView(_rows);
        ColumnDefinitions = CadDimensionStyleColumnDefinitions.Create();

        SearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        SearchModel.HighlightCurrent = true;
        SearchModel.WrapNavigation = true;

        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => ApplySearch());

        this.WhenAnyValue(x => x.FilterText)
            .Subscribe(_ => ApplyFilter());

        this.WhenAnyValue(x => x.SelectedStyle)
            .Subscribe(OnSelectedStyleChanged);

        _selectionService.WhenAnyValue(x => x.SelectedObject)
            .Subscribe(UpdateSelectionFromService);

        _documentContext.WhenAnyValue(x => x.ActiveDocument)
            .Subscribe(LoadStyles);

        ClearSearchCommand = ReactiveCommand.Create(() => { SearchText = string.Empty; });
        ClearFilterCommand = ReactiveCommand.Create(() => { FilterText = string.Empty; });
    }

    private void LoadStyles(CadDocumentViewModel? documentViewModel)
    {
        _rows.Clear();
        _rowMap.Clear();
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();

        var styles = documentViewModel?.Document?.DimensionStyles;
        if (styles is null)
        {
            StylesView.Refresh();
            return;
        }

        foreach (var style in styles)
        {
            if (style is null)
            {
                continue;
            }

            var row = new CadDimensionStyleRowViewModel(style);
            _rows.Add(row);
            _rowMap[style] = row;
            QueuePreview(row);
        }

        StylesView.Refresh();
        ApplySearch();
        UpdateSelectionFromService(_selectionService.SelectedObject);
    }

    private void QueuePreview(CadDimensionStyleRowViewModel row)
    {
        if (_previewCts is null)
        {
            return;
        }

        var token = _previewCts.Token;
        _ = Task.Run(async () =>
        {
            var preview = await _previewService.GetDimensionStylePreviewAsync(row.Style, PreviewSize, token)
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

    private void OnSelectedStyleChanged(CadDimensionStyleRowViewModel? row)
    {
        if (_suppressSelection)
        {
            return;
        }

        _selectionService.SelectedObject = row?.Style;
    }

    private void UpdateSelectionFromService(object? selected)
    {
        if (_suppressSelection)
        {
            return;
        }

        var style = ResolveDimensionStyle(selected);
        _suppressSelection = true;
        SelectedStyle = style is not null && _rowMap.TryGetValue(style, out var row) ? row : null;
        _suppressSelection = false;
    }

    private static DimensionStyle? ResolveDimensionStyle(object? selected)
    {
        switch (selected)
        {
            case CadDocumentTreeNode node:
                return ResolveDimensionStyle(node.Source);
            case DimensionStyle style:
                return style;
            case Dimension dimension:
                return dimension.Style;
        }

        return null;
    }
}
