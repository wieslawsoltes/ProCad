using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Core;
using ACadInspector.Diagnostics;
using ACadInspector.Services;
using ACadSharp;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadCompareViewModel : CadDocumentViewModelBase, IFastPathDiagnosticsSource
{
    private readonly ObservableCollection<CadDiffSummaryRowViewModel> _summaryRows = new();
    private readonly ObservableCollection<CadObjectDiffRowViewModel> _objectDiffRows = new();
    private readonly ObservableCollection<CadPropertyDiffRowViewModel> _propertyDiffRows = new();
    private readonly ObservableCollection<DxfTextDiffRowViewModel> _dxfTextRows = new();

    private readonly ICadFileDialogService _fileDialogService;
    private readonly ICadDocumentService _documentService;
    private readonly CadIoOptionsViewModel _ioOptions;
    private readonly CadDocumentContextService _documentContext;
    private readonly CadDocumentDiffEngine _diffEngine;
    private readonly DxfTextDiffEngine _dxfTextDiffEngine;
    private readonly IDxfTextService _dxfTextService;
    private CancellationTokenSource? _dxfTextCancellation;

    public CadCompareSideViewModel Left { get; }

    public CadCompareSideViewModel Right { get; }

    public DataGridCollectionView SummaryView { get; }

    public DataGridCollectionView ObjectDiffsView { get; }

    public DataGridCollectionView PropertyDiffsView { get; }

    public DataGridCollectionView DxfTextDiffsView { get; }

    public DataGridColumnDefinitionList SummaryColumnDefinitions { get; }

    public DataGridColumnDefinitionList ObjectDiffColumnDefinitions { get; }

    public DataGridColumnDefinitionList PropertyDiffColumnDefinitions { get; }

    public DataGridColumnDefinitionList DxfTextColumnDefinitions { get; }
    public FastPathDiagnosticsService FastPathDiagnostics { get; }

    public SortingModel SummarySortingModel { get; } = new();

    public FilteringModel SummaryFilteringModel { get; } = new();

    public SearchModel SummarySearchModel { get; } = new();

    public SortingModel ObjectSortingModel { get; } = new();

    public FilteringModel ObjectFilteringModel { get; } = new();

    public SearchModel ObjectSearchModel { get; } = new();

    public SortingModel PropertySortingModel { get; } = new();

    public FilteringModel PropertyFilteringModel { get; } = new();

    public SearchModel PropertySearchModel { get; } = new();

    public SortingModel DxfTextSortingModel { get; } = new();

    public FilteringModel DxfTextFilteringModel { get; } = new();

    public SearchModel DxfTextSearchModel { get; } = new();

    public ReactiveCommand<Unit, Unit> LoadLeftCommand { get; }

    public ReactiveCommand<Unit, Unit> LoadRightCommand { get; }

    public ReactiveCommand<Unit, Unit> UseActiveAsLeftCommand { get; }

    public ReactiveCommand<Unit, Unit> UseActiveAsRightCommand { get; }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    [Reactive]
    public partial CadObjectDiffRowViewModel? SelectedObjectDiff { get; set; }

    [Reactive]
    public partial string StatusMessage { get; set; } = "Select two documents to compare.";

    [Reactive]
    public partial string SelectedDiffTitle { get; set; } = "Property Changes";

    [Reactive]
    public partial string DxfTextStatusMessage { get; set; } = "DXF text diff is available for ASCII DXF files.";

    public CadCompareViewModel(
        ICadFileDialogService fileDialogService,
        ICadDocumentService documentService,
        CadIoOptionsViewModel ioOptions,
        CadDocumentContextService documentContext,
        CadDocumentDiffEngine diffEngine,
        DxfTextDiffEngine dxfTextDiffEngine,
        IDxfTextService dxfTextService,
        FastPathDiagnosticsService fastPathDiagnostics)
    {
        _fileDialogService = fileDialogService;
        _documentService = documentService;
        _ioOptions = ioOptions;
        _documentContext = documentContext;
        _diffEngine = diffEngine;
        _dxfTextDiffEngine = dxfTextDiffEngine;
        _dxfTextService = dxfTextService;
        FastPathDiagnostics = fastPathDiagnostics;

        Title = "Compare";

        Left = new CadCompareSideViewModel("Left");
        Right = new CadCompareSideViewModel("Right");

        SummaryView = new DataGridCollectionView(_summaryRows);
        ObjectDiffsView = new DataGridCollectionView(_objectDiffRows);
        PropertyDiffsView = new DataGridCollectionView(_propertyDiffRows);
        DxfTextDiffsView = new DataGridCollectionView(_dxfTextRows);

        SummaryColumnDefinitions = CadDiffSummaryColumnDefinitions.Create();
        ObjectDiffColumnDefinitions = CadObjectDiffColumnDefinitions.Create();
        PropertyDiffColumnDefinitions = CadPropertyDiffColumnDefinitions.Create();
        DxfTextColumnDefinitions = global::ACadInspector.ViewModels.DxfTextDiffColumnDefinitions.Create();

        LoadLeftCommand = ReactiveCommand.CreateFromTask(LoadLeftAsync);
        LoadRightCommand = ReactiveCommand.CreateFromTask(LoadRightAsync);
        UseActiveAsLeftCommand = ReactiveCommand.Create(UseActiveAsLeft);
        UseActiveAsRightCommand = ReactiveCommand.Create(UseActiveAsRight);

        var canRefresh = this.WhenAnyValue(
            x => x.Left.IsLoaded,
            x => x.Right.IsLoaded,
            static (left, right) => left && right);
        RefreshCommand = ReactiveCommand.Create(RefreshDiff, canRefresh);

        this.WhenAnyValue(x => x.SelectedObjectDiff)
            .Subscribe(UpdatePropertyDiffs);

        Left.WhenAnyValue(x => x.Document)
            .Merge(Right.WhenAnyValue(x => x.Document))
            .Subscribe(_ => RefreshDiff());
    }

    private async Task LoadLeftAsync(CancellationToken cancellationToken)
    {
        await LoadSideAsync(Left, cancellationToken).ConfigureAwait(true);
    }

    private async Task LoadRightAsync(CancellationToken cancellationToken)
    {
        await LoadSideAsync(Right, cancellationToken).ConfigureAwait(true);
    }

    private async Task LoadSideAsync(CadCompareSideViewModel side, CancellationToken cancellationToken)
    {
        var result = await _fileDialogService.OpenCadFileAsync(null, cancellationToken).ConfigureAwait(true);
        if (result is null)
        {
            return;
        }

        try
        {
            var options = _ioOptions.BuildReadOptions(result.Format);
            var document = await LoadDocumentAsync(result, options, cancellationToken).ConfigureAwait(true);
            side.UpdateFrom(document, result.Format, result.Path, result.FileName, result.OpenReadAsync);
            StatusMessage = $"Loaded {side.Label} document: {side.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load {side.Label} document: {ex.Message}";
        }
    }

    private void UseActiveAsLeft()
    {
        UseActiveDocument(Left, "Left");
    }

    private void UseActiveAsRight()
    {
        UseActiveDocument(Right, "Right");
    }

    private void UseActiveDocument(CadCompareSideViewModel side, string label)
    {
        var active = _documentContext.ActiveDocument;
        if (active is null)
        {
            StatusMessage = "No active document available.";
            return;
        }

        side.UpdateFrom(active.Document, active.Format, active.Path, active.Title);
        StatusMessage = $"Using active document for {label}: {active.Title}";
    }

    private async Task<CadDocument> LoadDocumentAsync(
        CadOpenFileResult result,
        CadReadOptions options,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(result.Path))
        {
            return _documentService.Load(result.Path, options);
        }

        await using var stream = await result.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        return _documentService.Load(stream, options);
    }

    private void RefreshDiff()
    {
        _summaryRows.Clear();
        _objectDiffRows.Clear();
        _propertyDiffRows.Clear();
        _dxfTextRows.Clear();
        SelectedObjectDiff = null;
        SelectedDiffTitle = "Property Changes";

        if (!Left.IsLoaded || !Right.IsLoaded || Left.Document is null || Right.Document is null)
        {
            StatusMessage = "Select two documents to compare.";
            SummaryView.Refresh();
            ObjectDiffsView.Refresh();
            PropertyDiffsView.Refresh();
            DxfTextDiffsView.Refresh();
            DxfTextStatusMessage = "DXF text diff is available for ASCII DXF files.";
            return;
        }

        var diff = _diffEngine.Compare(Left.Document, Right.Document);
        AddSummary("Added", diff.Added.Count);
        AddSummary("Removed", diff.Removed.Count);
        AddSummary("Modified", diff.Modified.Count);
        AddSummary("Unchanged", diff.Unchanged.Count);

        AddObjectDiffs(diff.Added);
        AddObjectDiffs(diff.Removed);
        AddObjectDiffs(diff.Modified);
        AddObjectDiffs(diff.Unchanged);

        SummaryView.Refresh();
        ObjectDiffsView.Refresh();
        PropertyDiffsView.Refresh();
        DxfTextDiffsView.Refresh();

        StatusMessage = $"Comparison complete. Added: {diff.Added.Count}, Removed: {diff.Removed.Count}, Modified: {diff.Modified.Count}, Unchanged: {diff.Unchanged.Count}.";
        _ = RefreshDxfTextDiffAsync();
    }

    private void AddSummary(string label, int count)
    {
        _summaryRows.Add(new CadDiffSummaryRowViewModel(label, count));
    }

    private void AddObjectDiffs(IReadOnlyList<CadObjectDiff> diffs)
    {
        foreach (var diff in diffs)
        {
            _objectDiffRows.Add(new CadObjectDiffRowViewModel(diff));
        }
    }

    private void UpdatePropertyDiffs(CadObjectDiffRowViewModel? selected)
    {
        _propertyDiffRows.Clear();
        if (selected?.Diff is null)
        {
            SelectedDiffTitle = "Property Changes";
            PropertyDiffsView.Refresh();
            return;
        }

        SelectedDiffTitle = selected.Path;
        foreach (var property in selected.Diff.PropertyDiffs)
        {
            _propertyDiffRows.Add(new CadPropertyDiffRowViewModel(property));
        }

        PropertyDiffsView.Refresh();
    }

    private async Task RefreshDxfTextDiffAsync()
    {
        _dxfTextCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _dxfTextCancellation = cancellation;
        var token = cancellation.Token;

        _dxfTextRows.Clear();
        DxfTextDiffsView.Refresh();

        if (!Left.IsLoaded || !Right.IsLoaded)
        {
            DxfTextStatusMessage = "Load two DXF documents to enable text diff.";
            return;
        }

        if (Left.Format != CadFileFormat.Dxf || Right.Format != CadFileFormat.Dxf)
        {
            DxfTextStatusMessage = "DXF text diff is available only for DXF vs DXF comparisons.";
            return;
        }

        DxfTextStatusMessage = "Loading DXF text...";

        var leftText = await _dxfTextService
            .TryLoadAsciiDxfAsync(Left.Format, Left.Path, Left.OpenRead, token)
            .ConfigureAwait(true);
        if (token.IsCancellationRequested)
        {
            return;
        }

        var rightText = await _dxfTextService
            .TryLoadAsciiDxfAsync(Right.Format, Right.Path, Right.OpenRead, token)
            .ConfigureAwait(true);
        if (token.IsCancellationRequested)
        {
            return;
        }

        if (!leftText.HasText || !rightText.HasText)
        {
            DxfTextStatusMessage = BuildTextStatus(leftText, rightText);
            return;
        }

        var diff = await Task
            .Run(() => _dxfTextDiffEngine.Compare(leftText.Text, rightText.Text), token)
            .ConfigureAwait(true);
        foreach (var line in diff.Lines)
        {
            _dxfTextRows.Add(new DxfTextDiffRowViewModel(line));
        }

        DxfTextDiffsView.Refresh();
        DxfTextStatusMessage = $"Text diff complete. Added: {diff.AddedCount}, Removed: {diff.RemovedCount}, Modified: {diff.ModifiedCount}, Unchanged: {diff.UnchangedCount}.";
        if (diff.IsApproximate && !string.IsNullOrWhiteSpace(diff.Warning))
        {
            DxfTextStatusMessage = $"{DxfTextStatusMessage} {diff.Warning}";
        }
    }

    private static string BuildTextStatus(DxfTextLoadResult leftText, DxfTextLoadResult rightText)
    {
        if (leftText.IsBinary || rightText.IsBinary)
        {
            return "Binary DXF detected. Text diff is available only for ASCII DXF files.";
        }

        if (!leftText.HasText && !string.IsNullOrWhiteSpace(leftText.Error) &&
            !rightText.HasText && !string.IsNullOrWhiteSpace(rightText.Error))
        {
            return $"DXF text diff unavailable: {leftText.Error}; {rightText.Error}";
        }

        if (!leftText.HasText && !string.IsNullOrWhiteSpace(leftText.Error))
        {
            return $"Left DXF text unavailable: {leftText.Error}";
        }

        if (!rightText.HasText && !string.IsNullOrWhiteSpace(rightText.Error))
        {
            return $"Right DXF text unavailable: {rightText.Error}";
        }

        return "DXF text diff unavailable.";
    }
}
