using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ACadInspector.Core;
using ACadInspector.Diagnostics;
using ACadInspector.Services;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadDxfSemanticsViewModel : CadToolViewModelBase, IFastPathDiagnosticsSource
{
    private readonly ObservableCollection<CadDxfPropertyRowViewModel> _propertyRows = new();
    private readonly CadSelectionService _selectionService;

    public DataGridCollectionView PropertyRowsView { get; }
    public DataGridColumnDefinitionList PropertyColumnDefinitions { get; }
    public FastPathDiagnosticsService FastPathDiagnostics { get; }

    public SortingModel SortingModel { get; } = new();
    public FilteringModel FilteringModel { get; } = new();
    public SearchModel SearchModel { get; } = new();

    [Reactive]
    public partial string SearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string FilterText { get; set; } = string.Empty;

    [Reactive]
    public partial string SelectedTitle { get; set; } = "No selection";

    [Reactive]
    public partial bool IsActive { get; set; }

    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearFilterCommand { get; }

    public CadDxfSemanticsViewModel(
        CadSelectionService selectionService,
        FastPathDiagnosticsService fastPathDiagnostics)
    {
        _selectionService = selectionService;
        FastPathDiagnostics = fastPathDiagnostics;
        PropertyRowsView = new DataGridCollectionView(_propertyRows);
        PropertyColumnDefinitions = CadDxfPropertyColumnDefinitions.Create();

        SearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        SearchModel.HighlightCurrent = true;
        SearchModel.WrapNavigation = true;

        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => ApplySearch());

        this.WhenAnyValue(x => x.FilterText)
            .Subscribe(_ => ApplyFilter());

        _selectionService.WhenAnyValue(x => x.SelectedObject)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(UpdateRows);

        ClearSearchCommand = ReactiveCommand.Create(() => { SearchText = string.Empty; });
        ClearFilterCommand = ReactiveCommand.Create(() => { FilterText = string.Empty; });
    }

    private void UpdateRows(object? selected)
    {
        _propertyRows.Clear();

        if (selected is null)
        {
            SelectedTitle = "No selection";
            PropertyRowsView.Refresh();
            return;
        }

        SelectedTitle = CadSelectionTitleFormatter.BuildTitle(selected);

        var descriptor = FindDescriptor(selected.GetType());
        if (descriptor is not null)
        {
            foreach (var property in descriptor.Properties)
            {
                if (property.DxfCodes.Length == 0)
                {
                    continue;
                }

                var valueText = FormatValue(selected, property);
                var codes = string.Join(", ", property.DxfCodes);
                var referenceType = property.DxfReferenceType ?? string.Empty;
                _propertyRows.Add(new CadDxfPropertyRowViewModel(property.Name, codes, referenceType, valueText));
            }
        }

        PropertyRowsView.Refresh();
    }

    private void ApplySearch()
    {
        DataGridFilterHelper.ApplySearch(SearchModel, SearchText);
    }

    private void ApplyFilter()
    {
        DataGridFilterHelper.ApplyFilter(FilteringModel, PropertyColumnDefinitions, FilterText);
    }

    private static string FormatValue(object target, CadPropertyDescriptor descriptor)
    {
        try
        {
            var value = descriptor.Getter(target);
            return CadValueConverter.FormatValue(value, descriptor.PropertyType);
        }
        catch (Exception)
        {
            return string.Empty;
        }
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

}
