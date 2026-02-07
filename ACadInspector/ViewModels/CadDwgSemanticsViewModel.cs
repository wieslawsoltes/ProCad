using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ACadInspector.Core;
using ACadInspector.Diagnostics;
using ACadInspector.Services;
using ACadSharp;
using ACadSharp.Classes;
using ACadSharp.Header;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadDwgSemanticsViewModel : CadToolViewModelBase, IFastPathDiagnosticsSource
{
    private readonly CadSelectionService _selectionService;
    private readonly CadDocumentContextService _documentContext;
    private readonly ObservableCollection<CadDwgHeaderRowViewModel> _headerRows = new();
    private readonly ObservableCollection<CadDwgClassRowViewModel> _classRows = new();
    private readonly ObservableCollection<CadDwgSummaryRowViewModel> _summaryRows = new();

    public DataGridCollectionView HeaderRowsView { get; }
    public DataGridColumnDefinitionList HeaderColumnDefinitions { get; }
    public DataGridCollectionView ClassRowsView { get; }
    public DataGridColumnDefinitionList ClassColumnDefinitions { get; }
    public DataGridCollectionView SummaryRowsView { get; }
    public DataGridColumnDefinitionList SummaryColumnDefinitions { get; }
    public FastPathDiagnosticsService FastPathDiagnostics { get; }

    public SortingModel HeaderSortingModel { get; } = new();
    public FilteringModel HeaderFilteringModel { get; } = new();
    public SearchModel HeaderSearchModel { get; } = new();

    public SortingModel ClassSortingModel { get; } = new();
    public FilteringModel ClassFilteringModel { get; } = new();
    public SearchModel ClassSearchModel { get; } = new();

    public SortingModel SummarySortingModel { get; } = new();
    public FilteringModel SummaryFilteringModel { get; } = new();
    public SearchModel SummarySearchModel { get; } = new();

    [Reactive]
    public partial string HeaderSearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string HeaderFilterText { get; set; } = string.Empty;

    [Reactive]
    public partial string ClassSearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string ClassFilterText { get; set; } = string.Empty;

    [Reactive]
    public partial string SummarySearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string SummaryFilterText { get; set; } = string.Empty;

    [Reactive]
    public partial string DwgDocumentTitle { get; set; } = "No document";

    [Reactive]
    public partial bool IsActive { get; set; }

    public ReactiveCommand<Unit, Unit> ClearHeaderSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearHeaderFilterCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearClassSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearClassFilterCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearSummarySearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearSummaryFilterCommand { get; }

    public CadDwgSemanticsViewModel(
        CadSelectionService selectionService,
        CadDocumentContextService documentContext,
        FastPathDiagnosticsService fastPathDiagnostics)
    {
        _selectionService = selectionService;
        _documentContext = documentContext;
        FastPathDiagnostics = fastPathDiagnostics;
        HeaderRowsView = new DataGridCollectionView(_headerRows);
        HeaderColumnDefinitions = CadDwgHeaderColumnDefinitions.Create();
        ClassRowsView = new DataGridCollectionView(_classRows);
        ClassColumnDefinitions = CadDwgClassColumnDefinitions.Create();
        SummaryRowsView = new DataGridCollectionView(_summaryRows);
        SummaryColumnDefinitions = CadDwgSummaryColumnDefinitions.Create();

        HeaderSearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        HeaderSearchModel.HighlightCurrent = true;
        HeaderSearchModel.WrapNavigation = true;

        ClassSearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        ClassSearchModel.HighlightCurrent = true;
        ClassSearchModel.WrapNavigation = true;

        SummarySearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        SummarySearchModel.HighlightCurrent = true;
        SummarySearchModel.WrapNavigation = true;

        this.WhenAnyValue(x => x.HeaderSearchText)
            .Subscribe(_ => ApplyHeaderSearch());
        this.WhenAnyValue(x => x.HeaderFilterText)
            .Subscribe(_ => ApplyHeaderFilter());

        this.WhenAnyValue(x => x.ClassSearchText)
            .Subscribe(_ => ApplyClassSearch());
        this.WhenAnyValue(x => x.ClassFilterText)
            .Subscribe(_ => ApplyClassFilter());

        this.WhenAnyValue(x => x.SummarySearchText)
            .Subscribe(_ => ApplySummarySearch());
        this.WhenAnyValue(x => x.SummaryFilterText)
            .Subscribe(_ => ApplySummaryFilter());

        _selectionService.WhenAnyValue(x => x.SelectedObject)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(UpdateDwgSemantics);

        this.WhenAnyValue(x => x.IsActive)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnIsActiveChanged);

        ClearHeaderSearchCommand = ReactiveCommand.Create(() => { HeaderSearchText = string.Empty; });
        ClearHeaderFilterCommand = ReactiveCommand.Create(() => { HeaderFilterText = string.Empty; });
        ClearClassSearchCommand = ReactiveCommand.Create(() => { ClassSearchText = string.Empty; });
        ClearClassFilterCommand = ReactiveCommand.Create(() => { ClassFilterText = string.Empty; });
        ClearSummarySearchCommand = ReactiveCommand.Create(() => { SummarySearchText = string.Empty; });
        ClearSummaryFilterCommand = ReactiveCommand.Create(() => { SummaryFilterText = string.Empty; });
    }

    private void UpdateDwgSemantics(object? selected)
    {
        if (!IsActive)
        {
            return;
        }

        _headerRows.Clear();
        _classRows.Clear();
        _summaryRows.Clear();

        var document = _documentContext.ResolveDocument(selected);
        var viewModel = _documentContext.ResolveViewModel(selected);
        if (document is null)
        {
            DwgDocumentTitle = "No document";
            HeaderRowsView.Refresh();
            ClassRowsView.Refresh();
            SummaryRowsView.Refresh();
            return;
        }

        _documentContext.TrySetActiveFromSelection(selected);
        DwgDocumentTitle = viewModel?.Title ?? "Document";

        if (document.Header is not null)
        {
            foreach (var variable in CadHeaderMetadataRegistry.Variables)
            {
                var valueText = FormatHeaderValue(document.Header, variable);
                var codes = variable.DxfCodes.Length == 0 ? string.Empty : string.Join(", ", variable.DxfCodes);
                var referenceType = variable.ReferenceType ?? string.Empty;
                var typeName = GetTypeDisplayName(variable.PropertyType);
                _headerRows.Add(new CadDwgHeaderRowViewModel(variable.VariableName, variable.PropertyName, codes, referenceType, typeName, valueText));
            }
        }

        if (document.Classes is not null)
        {
            foreach (var entry in document.Classes)
            {
                _classRows.Add(new CadDwgClassRowViewModel(entry));
            }
        }

        BuildSummaryRows(document.SummaryInfo);

        HeaderRowsView.Refresh();
        ClassRowsView.Refresh();
        SummaryRowsView.Refresh();
    }

    private void OnIsActiveChanged(bool isActive)
    {
        if (!isActive)
        {
            _headerRows.Clear();
            _classRows.Clear();
            _summaryRows.Clear();
            DwgDocumentTitle = "No document";
            HeaderRowsView.Refresh();
            ClassRowsView.Refresh();
            SummaryRowsView.Refresh();
            return;
        }

        UpdateDwgSemantics(_selectionService.SelectedObject);
    }

    private void ApplyHeaderSearch()
    {
        DataGridFilterHelper.ApplySearch(HeaderSearchModel, HeaderSearchText);
    }

    private void ApplyHeaderFilter()
    {
        DataGridFilterHelper.ApplyFilter(HeaderFilteringModel, HeaderColumnDefinitions, HeaderFilterText);
    }

    private void ApplyClassSearch()
    {
        DataGridFilterHelper.ApplySearch(ClassSearchModel, ClassSearchText);
    }

    private void ApplyClassFilter()
    {
        DataGridFilterHelper.ApplyFilter(ClassFilteringModel, ClassColumnDefinitions, ClassFilterText);
    }

    private void ApplySummarySearch()
    {
        DataGridFilterHelper.ApplySearch(SummarySearchModel, SummarySearchText);
    }

    private void ApplySummaryFilter()
    {
        DataGridFilterHelper.ApplyFilter(SummaryFilteringModel, SummaryColumnDefinitions, SummaryFilterText);
    }

    private static string FormatHeaderValue(CadHeader header, CadHeaderVariableDescriptor descriptor)
    {
        try
        {
            var value = descriptor.Getter(header);
            return CadValueConverter.FormatValue(value, descriptor.PropertyType);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private void BuildSummaryRows(CadSummaryInfo? summary)
    {
        if (summary is null)
        {
            return;
        }

        AddSummaryRow("Summary", "Title", summary.Title, typeof(string));
        AddSummaryRow("Summary", "Subject", summary.Subject, typeof(string));
        AddSummaryRow("Summary", "Author", summary.Author, typeof(string));
        AddSummaryRow("Summary", "Keywords", summary.Keywords, typeof(string));
        AddSummaryRow("Summary", "Comments", summary.Comments, typeof(string));
        AddSummaryRow("Summary", "LastSavedBy", summary.LastSavedBy, typeof(string));
        AddSummaryRow("Summary", "RevisionNumber", summary.RevisionNumber, typeof(string));
        AddSummaryRow("Summary", "HyperlinkBase", summary.HyperlinkBase, typeof(string));
        AddSummaryRow("Summary", "CreatedDate", summary.CreatedDate, typeof(DateTime));
        AddSummaryRow("Summary", "ModifiedDate", summary.ModifiedDate, typeof(DateTime));

        if (summary.Properties is not null)
        {
            foreach (var entry in summary.Properties)
            {
                AddSummaryRow("Custom", entry.Key, entry.Value, typeof(string));
            }
        }
    }

    private void AddSummaryRow(string category, string name, object? value, Type type)
    {
        var valueText = CadValueConverter.FormatValue(value, type);
        _summaryRows.Add(new CadDwgSummaryRowViewModel(category, name, valueText));
    }

    private static string GetTypeDisplayName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        return underlying is null ? type.Name : $"{underlying.Name}?";
    }
}
