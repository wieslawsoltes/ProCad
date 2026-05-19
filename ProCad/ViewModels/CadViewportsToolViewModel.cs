using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ProCad.Services;
using ACadSharp.Entities;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class CadViewportsToolViewModel : CadToolViewModelBase
{
    private readonly ObservableCollection<CadViewportRowViewModel> _viewportRows = new();
    private readonly Dictionary<Viewport, CadViewportRowViewModel> _rowMap = new(ReferenceEqualityComparer.Instance);
    private readonly CadDocumentContextService _documentContext;
    private readonly CadSelectionService _selectionService;
    private bool _suppressSelection;

    [Reactive]
    public partial string SearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string FilterText { get; set; } = string.Empty;

    [Reactive]
    public partial CadViewportRowViewModel? SelectedViewport { get; set; }

    public DataGridCollectionView ViewportsView { get; }
    public DataGridColumnDefinitionList ColumnDefinitions { get; }
    public SortingModel SortingModel { get; } = new();
    public FilteringModel FilteringModel { get; } = new();
    public SearchModel SearchModel { get; } = new();

    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearFilterCommand { get; }

    public CadViewportsToolViewModel(
        CadDocumentContextService documentContext,
        CadSelectionService selectionService)
    {
        _documentContext = documentContext;
        _selectionService = selectionService;

        ViewportsView = new DataGridCollectionView(_viewportRows);
        ColumnDefinitions = CadViewportColumnDefinitions.Create();

        SearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        SearchModel.HighlightCurrent = true;
        SearchModel.WrapNavigation = true;

        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => ApplySearch());

        this.WhenAnyValue(x => x.FilterText)
            .Subscribe(_ => ApplyFilter());

        ClearSearchCommand = ReactiveCommand.Create(() => { SearchText = string.Empty; });
        ClearFilterCommand = ReactiveCommand.Create(() => { FilterText = string.Empty; });

        this.WhenAnyValue(x => x.SelectedViewport)
            .Subscribe(OnSelectedViewportChanged);

        _selectionService.WhenAnyValue(x => x.SelectedObject)
            .Subscribe(UpdateSelectionFromService);

        _documentContext.WhenAnyValue(x => x.ActiveDocument)
            .Subscribe(LoadViewports);
    }

    private void LoadViewports(CadDocumentViewModel? documentViewModel)
    {
        _viewportRows.Clear();
        _rowMap.Clear();

        var document = documentViewModel?.Document;
        if (document?.Layouts is null)
        {
            ViewportsView.Refresh();
            return;
        }

        foreach (var layout in document.Layouts)
        {
            if (layout is null || !layout.IsPaperSpace)
            {
                continue;
            }

            var viewports = layout.Viewports;
            if (viewports is null)
            {
                continue;
            }

            foreach (var viewport in viewports)
            {
                if (viewport is null)
                {
                    continue;
                }

                var row = new CadViewportRowViewModel(layout, viewport);
                _viewportRows.Add(row);
                _rowMap[viewport] = row;
            }
        }

        ViewportsView.Refresh();
        ApplySearch();
        UpdateSelectionFromService(_selectionService.SelectedObject);
    }

    private void ApplySearch()
    {
        DataGridFilterHelper.ApplySearch(SearchModel, SearchText);
    }

    private void ApplyFilter()
    {
        DataGridFilterHelper.ApplyFilter(FilteringModel, ColumnDefinitions, FilterText);
    }

    private void OnSelectedViewportChanged(CadViewportRowViewModel? row)
    {
        if (_suppressSelection)
        {
            return;
        }

        _selectionService.SelectedObject = row?.Viewport;
    }

    private void UpdateSelectionFromService(object? selected)
    {
        if (_suppressSelection)
        {
            return;
        }

        var viewport = ResolveViewport(selected);
        _suppressSelection = true;
        SelectedViewport = viewport is not null && _rowMap.TryGetValue(viewport, out var row) ? row : null;
        _suppressSelection = false;
    }

    private static Viewport? ResolveViewport(object? selected)
    {
        switch (selected)
        {
            case CadDocumentTreeNode node:
                return ResolveViewport(node.Source);
            case Viewport viewport:
                return viewport;
        }

        return null;
    }
}
