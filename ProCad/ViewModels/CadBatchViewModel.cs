using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ProCad.Core;
using ProCad.Diagnostics;
using ProCad.Services;
using ProCad.Scripting;
using ProCad.Serialization;
using ACadSharp;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class CadBatchViewModel : CadToolViewModelBase, IFastPathDiagnosticsSource
{
    private readonly ObservableCollection<CadBatchItemViewModel> _items = new();
    private readonly ObservableCollection<CadBatchResultRowViewModel> _results = new();
    private readonly ObservableCollection<CadBatchScriptResultRowViewModel> _scriptResults = new();
    private readonly List<CadBatchSearchResult> _resultModels = new();
    private readonly ICadBatchFileDialogService _batchDialogService;
    private readonly ICadBatchExportService _exportService;
    private readonly ICadDocumentService _documentService;
    private readonly CadIoOptionsViewModel _ioOptions;
    private readonly CadBatchQueryEngine _queryEngine;
    private readonly ICadScriptHost _scriptHost;
    private readonly CadScriptWorkspaceService _scriptWorkspace;
    private CancellationTokenSource? _processing;

    public DataGridCollectionView ItemsView { get; }

    public DataGridCollectionView ResultsView { get; }

    public DataGridCollectionView ScriptResultsView { get; }

    public DataGridColumnDefinitionList ColumnDefinitions { get; }

    public DataGridColumnDefinitionList ResultColumnDefinitions { get; }

    public DataGridColumnDefinitionList ScriptResultColumnDefinitions { get; }
    public FastPathDiagnosticsService FastPathDiagnostics { get; }

    public SortingModel SortingModel { get; } = new();

    public FilteringModel FilteringModel { get; } = new();

    public SearchModel SearchModel { get; } = new();

    public SortingModel ResultSortingModel { get; } = new();

    public FilteringModel ResultFilteringModel { get; } = new();

    public SearchModel ResultSearchModel { get; } = new();

    public SortingModel ScriptResultSortingModel { get; } = new();

    public FilteringModel ScriptResultFilteringModel { get; } = new();

    public SearchModel ScriptResultSearchModel { get; } = new();

    [Reactive]
    public partial string ItemsSearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string ItemsFilterText { get; set; } = string.Empty;

    [Reactive]
    public partial string ResultsSearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string ResultsFilterText { get; set; } = string.Empty;

    [Reactive]
    public partial string ScriptResultsSearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string ScriptResultsFilterText { get; set; } = string.Empty;

    [Reactive]
    public partial CadBatchItemViewModel? SelectedItem { get; set; }

    [Reactive]
    public partial string StatusMessage { get; set; } = "Add files or a folder to begin batch processing.";

    [Reactive]
    public partial string ResultsStatusMessage { get; set; } = "No results yet.";

    [Reactive]
    public partial string ScriptResultsStatusMessage { get; set; } = "No script results yet.";

    [Reactive]
    public partial string ProgressText { get; set; } = "0 / 0";

    [Reactive]
    public partial double ProgressValue { get; set; }

    [Reactive]
    public partial bool IsRunning { get; set; }

    [Reactive]
    public partial int ItemsCount { get; set; }

    [Reactive]
    public partial int ResultsCount { get; set; }

    [Reactive]
    public partial int ScriptResultsCount { get; set; }

    [Reactive]
    public partial string QueryText { get; set; } = string.Empty;

    public ReactiveCommand<Unit, Unit> AddFilesCommand { get; }

    public ReactiveCommand<Unit, Unit> AddFolderCommand { get; }

    public ReactiveCommand<Unit, Unit> StartCommand { get; }

    public ReactiveCommand<Unit, Unit> RunSearchCommand { get; }

    public ReactiveCommand<Unit, Unit> RunScriptCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearCommand { get; }

    public ReactiveCommand<Unit, Unit> RemoveSelectedCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearResultsCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearScriptResultsCommand { get; }

    public ReactiveCommand<Unit, Unit> ExportCsvCommand { get; }

    public ReactiveCommand<Unit, Unit> ExportJsonCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearItemsSearchCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearItemsFilterCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearResultsSearchCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearResultsFilterCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearScriptResultsSearchCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearScriptResultsFilterCommand { get; }

    public CadBatchViewModel(
        ICadBatchFileDialogService batchDialogService,
        ICadBatchExportService exportService,
        ICadDocumentService documentService,
        CadIoOptionsViewModel ioOptions,
        CadBatchQueryEngine queryEngine,
        ICadScriptHost scriptHost,
        CadScriptWorkspaceService scriptWorkspace,
        FastPathDiagnosticsService fastPathDiagnostics)
    {
        _batchDialogService = batchDialogService;
        _exportService = exportService;
        _documentService = documentService;
        _ioOptions = ioOptions;
        _queryEngine = queryEngine;
        _scriptHost = scriptHost;
        _scriptWorkspace = scriptWorkspace;
        FastPathDiagnostics = fastPathDiagnostics;

        ItemsView = new DataGridCollectionView(_items);
        ColumnDefinitions = CadBatchColumnDefinitions.Create();
        ResultsView = new DataGridCollectionView(_results);
        ResultColumnDefinitions = CadBatchResultColumnDefinitions.Create();
        ScriptResultsView = new DataGridCollectionView(_scriptResults);
        ScriptResultColumnDefinitions = CadBatchScriptResultColumnDefinitions.Create();

        SearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        SearchModel.HighlightCurrent = true;
        SearchModel.WrapNavigation = true;

        ResultSearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        ResultSearchModel.HighlightCurrent = true;
        ResultSearchModel.WrapNavigation = true;

        ScriptResultSearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        ScriptResultSearchModel.HighlightCurrent = true;
        ScriptResultSearchModel.WrapNavigation = true;

        this.WhenAnyValue(x => x.ItemsSearchText)
            .Subscribe(_ => ApplyItemsSearch());
        this.WhenAnyValue(x => x.ItemsFilterText)
            .Subscribe(_ => ApplyItemsFilter());
        this.WhenAnyValue(x => x.ResultsSearchText)
            .Subscribe(_ => ApplyResultsSearch());
        this.WhenAnyValue(x => x.ResultsFilterText)
            .Subscribe(_ => ApplyResultsFilter());
        this.WhenAnyValue(x => x.ScriptResultsSearchText)
            .Subscribe(_ => ApplyScriptResultsSearch());
        this.WhenAnyValue(x => x.ScriptResultsFilterText)
            .Subscribe(_ => ApplyScriptResultsFilter());

        _items.CollectionChanged += (_, _) => UpdateCounts();
        _scriptResults.CollectionChanged += (_, _) => UpdateScriptResultsCount();
        UpdateCounts();
        UpdateScriptResultsCount();

        var canEditQueue = this.WhenAnyValue(x => x.IsRunning, running => !running);
        var canStart = this.WhenAnyValue(
            x => x.IsRunning,
            x => x.ItemsCount,
            (running, count) => !running && count > 0);
        var canCancel = this.WhenAnyValue(x => x.IsRunning);
        var canSearch = this.WhenAnyValue(x => x.IsRunning, running => !running);
        var canRunScript = this.WhenAnyValue(
            x => x.IsRunning,
            x => x.ItemsCount,
            (running, count) => !running && count > 0);
        var canExport = this.WhenAnyValue(
            x => x.IsRunning,
            x => x.ResultsCount,
            (running, count) => !running && count > 0);

        AddFilesCommand = ReactiveCommand.CreateFromTask(AddFilesAsync, canEditQueue);
        AddFolderCommand = ReactiveCommand.CreateFromTask(AddFolderAsync, canEditQueue);
        StartCommand = ReactiveCommand.CreateFromTask(StartAsync, canStart);
        RunSearchCommand = ReactiveCommand.CreateFromTask(RunSearchAsync, canSearch);
        RunScriptCommand = ReactiveCommand.CreateFromTask(RunScriptAsync, canRunScript);
        CancelCommand = ReactiveCommand.Create(CancelProcessing, canCancel);
        ClearCommand = ReactiveCommand.Create(ClearQueue, canEditQueue);
        RemoveSelectedCommand = ReactiveCommand.Create(RemoveSelected, canEditQueue);
        ClearResultsCommand = ReactiveCommand.Create(ClearResults, canSearch);
        ClearScriptResultsCommand = ReactiveCommand.Create(ClearScriptResults, canSearch);
        ExportCsvCommand = ReactiveCommand.CreateFromTask(ExportCsvAsync, canExport);
        ExportJsonCommand = ReactiveCommand.CreateFromTask(ExportJsonAsync, canExport);

        ClearItemsSearchCommand = ReactiveCommand.Create(() => { ItemsSearchText = string.Empty; });
        ClearItemsFilterCommand = ReactiveCommand.Create(() => { ItemsFilterText = string.Empty; });
        ClearResultsSearchCommand = ReactiveCommand.Create(() => { ResultsSearchText = string.Empty; });
        ClearResultsFilterCommand = ReactiveCommand.Create(() => { ResultsFilterText = string.Empty; });
        ClearScriptResultsSearchCommand = ReactiveCommand.Create(() => { ScriptResultsSearchText = string.Empty; });
        ClearScriptResultsFilterCommand = ReactiveCommand.Create(() => { ScriptResultsFilterText = string.Empty; });
    }

    private async Task AddFilesAsync(CancellationToken cancellationToken)
    {
        var results = await _batchDialogService.OpenCadFilesAsync(cancellationToken).ConfigureAwait(true);
        AddResults(results, "files");
    }

    private async Task AddFolderAsync(CancellationToken cancellationToken)
    {
        var results = await _batchDialogService.OpenCadFolderAsync(cancellationToken).ConfigureAwait(true);
        AddResults(results, "folder items");
    }

    private void AddResults(IReadOnlyList<CadOpenFileResult> results, string sourceLabel)
    {
        if (results.Count == 0)
        {
            StatusMessage = $"No {sourceLabel} added.";
            return;
        }

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _items)
        {
            var key = BuildKey(item.Path, item.FileName);
            existing.Add(key);
        }

        var added = 0;
        foreach (var result in results)
        {
            var key = BuildKey(result.Path, result.FileName);
            if (existing.Contains(key))
            {
                continue;
            }

            _items.Add(new CadBatchItemViewModel(result));
            existing.Add(key);
            added++;
        }

        ItemsView.Refresh();
        StatusMessage = added > 0
            ? $"Added {added} {sourceLabel} to the batch queue."
            : "No new items were added (duplicates filtered).";
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        StatusMessage = "Batch processing started.";
        var pending = GetPendingItems();
        UpdateProgress(0, pending.Count);

        _processing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _processing.Token;

        var processed = 0;
        try
        {
            foreach (var item in pending)
            {
                if (token.IsCancellationRequested)
                {
                    item.MarkCancelled();
                    continue;
                }

                item.MarkLoading();
                ItemsView.Refresh();

                try
                {
                    token.ThrowIfCancellationRequested();
                    var options = _ioOptions.BuildReadOptions(item.Format);
                    var document = await LoadDocumentAsync(item, options, token).ConfigureAwait(true);
                    item.MarkLoaded(document);
                }
                catch (OperationCanceledException)
                {
                    item.MarkCancelled();
                }
                catch (Exception ex)
                {
                    item.MarkFailed(ex.Message);
                }

                processed++;
                UpdateProgress(processed, pending.Count);
            }
        }
        finally
        {
            IsRunning = false;
            _processing?.Dispose();
            _processing = null;
            StatusMessage = token.IsCancellationRequested
                ? "Batch processing cancelled."
                : "Batch processing complete.";
        }
    }

    private async Task RunSearchAsync(CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return;
        }

        var query = CadBatchQueryParser.Parse(QueryText);
        if (query.Terms.Count == 0)
        {
            ResultsStatusMessage = "Enter a query to search across batch items.";
            return;
        }

        IsRunning = true;
        ResultsStatusMessage = "Running batch search...";
        ClearResultsInternal();

        var items = GetPendingItems(includeLoaded: true);
        UpdateProgress(0, items.Count);

        _processing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _processing.Token;

        var processed = 0;
        var matchedDocuments = 0;
        try
        {
            foreach (var item in items)
            {
                if (token.IsCancellationRequested)
                {
                    item.MarkCancelled();
                    continue;
                }

                if (item.Document is null)
                {
                    item.MarkLoading();
                    ItemsView.Refresh();
                    try
                    {
                        var options = _ioOptions.BuildReadOptions(item.Format);
                        var document = await LoadDocumentAsync(item, options, token).ConfigureAwait(true);
                        item.MarkLoaded(document);
                    }
                    catch (OperationCanceledException)
                    {
                        item.MarkCancelled();
                    }
                    catch (Exception ex)
                    {
                        item.MarkFailed(ex.Message);
                    }
                }

                if (item.Document is not null)
                {
                    var results = _queryEngine.Search(query, item.Document, item.FileName, item.Format, item.Path);
                    if (results.Count > 0)
                    {
                        matchedDocuments++;
                        AddSearchResults(results);
                    }
                }

                processed++;
                UpdateProgress(processed, items.Count);
            }
        }
        finally
        {
            IsRunning = false;
            _processing?.Dispose();
            _processing = null;
            ResultsStatusMessage = token.IsCancellationRequested
                ? "Batch search cancelled."
                : $"Search complete. {ResultsCount} matches across {matchedDocuments} documents.";
        }
    }

    private void CancelProcessing()
    {
        _processing?.Cancel();
        StatusMessage = "Cancelling batch processing...";
    }

    private void ClearQueue()
    {
        _items.Clear();
        ItemsView.Refresh();
        UpdateProgress(0, 0);
        StatusMessage = "Batch queue cleared.";
    }

    private void ClearResults()
    {
        ClearResultsInternal();
        ResultsStatusMessage = "Results cleared.";
    }

    private void ClearScriptResults()
    {
        ClearScriptResultsInternal();
        ScriptResultsStatusMessage = "Script results cleared.";
    }

    private void RemoveSelected()
    {
        if (SelectedItem is null)
        {
            return;
        }

        _items.Remove(SelectedItem);
        ItemsView.Refresh();
        UpdateCounts();
    }

    private List<CadBatchItemViewModel> GetPendingItems(bool includeLoaded = false)
    {
        var pending = new List<CadBatchItemViewModel>();
        foreach (var item in _items)
        {
            if (item.Status is CadBatchItemStatus.Pending or CadBatchItemStatus.Failed or CadBatchItemStatus.Skipped ||
                (includeLoaded && item.Status == CadBatchItemStatus.Loaded))
            {
                pending.Add(item);
            }
        }

        return pending;
    }

    private async Task<CadDocument> LoadDocumentAsync(
        CadBatchItemViewModel item,
        CadReadOptions options,
        CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path))
        {
            return await Task.Run(() => _documentService.Load(item.Path, options), token)
                .ConfigureAwait(true);
        }

        if (item.OpenRead is null)
        {
            throw new InvalidOperationException("No readable stream available for this item.");
        }

        await using var stream = await item.OpenRead(token).ConfigureAwait(false);
        return await Task.Run(() => _documentService.Load(stream, options), token)
            .ConfigureAwait(true);
    }

    private void UpdateCounts()
    {
        ItemsCount = _items.Count;
        UpdateProgress(0, _items.Count);
    }

    private void ApplyItemsSearch()
    {
        DataGridFilterHelper.ApplySearch(SearchModel, ItemsSearchText);
    }

    private void ApplyItemsFilter()
    {
        DataGridFilterHelper.ApplyFilter(FilteringModel, ColumnDefinitions, ItemsFilterText);
    }

    private void ApplyResultsSearch()
    {
        DataGridFilterHelper.ApplySearch(ResultSearchModel, ResultsSearchText);
    }

    private void ApplyResultsFilter()
    {
        DataGridFilterHelper.ApplyFilter(ResultFilteringModel, ResultColumnDefinitions, ResultsFilterText);
    }

    private void ApplyScriptResultsSearch()
    {
        DataGridFilterHelper.ApplySearch(ScriptResultSearchModel, ScriptResultsSearchText);
    }

    private void ApplyScriptResultsFilter()
    {
        DataGridFilterHelper.ApplyFilter(ScriptResultFilteringModel, ScriptResultColumnDefinitions, ScriptResultsFilterText);
    }

    private void UpdateScriptResultsCount()
    {
        ScriptResultsCount = _scriptResults.Count;
    }

    private void UpdateProgress(int processed, int total)
    {
        ProgressText = $"{processed} / {total}";
        ProgressValue = total == 0 ? 0 : processed / (double)total;
    }

    private static string BuildKey(string? path, string fileName)
    {
        return string.IsNullOrWhiteSpace(path) ? fileName : path;
    }

    private void AddSearchResults(IReadOnlyList<CadBatchSearchResult> results)
    {
        foreach (var result in results)
        {
            _resultModels.Add(result);
            _results.Add(new CadBatchResultRowViewModel(result));
        }

        ResultsView.Refresh();
        ResultsCount = _resultModels.Count;
    }

    private void ClearResultsInternal()
    {
        _resultModels.Clear();
        _results.Clear();
        ResultsView.Refresh();
        ResultsCount = 0;
    }

    private void AddScriptResult(CadBatchItemViewModel item, CadScriptExecutionResult result)
    {
        _scriptResults.Add(new CadBatchScriptResultRowViewModel(
            item.FileName,
            item.Path,
            item.Format,
            result));
        ScriptResultsView.Refresh();
        UpdateScriptResultsCount();
    }

    private void ClearScriptResultsInternal()
    {
        _scriptResults.Clear();
        ScriptResultsView.Refresh();
        ScriptResultsCount = 0;
    }

    private async Task RunScriptAsync(CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return;
        }

        var scriptText = _scriptWorkspace.ScriptText;
        if (string.IsNullOrWhiteSpace(scriptText))
        {
            ScriptResultsStatusMessage = "No script available. Use the scripting tool to author a script.";
            return;
        }

        IsRunning = true;
        ScriptResultsStatusMessage = "Loading documents for batch script run...";
        ClearScriptResultsInternal();

        var items = GetPendingItems(includeLoaded: true);
        UpdateProgress(0, items.Count);

        _processing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _processing.Token;

        var processed = 0;
        var loadedDocuments = new List<CadDocument>();

        try
        {
            foreach (var item in items)
            {
                if (token.IsCancellationRequested)
                {
                    item.MarkCancelled();
                    continue;
                }

                if (item.Document is null)
                {
                    item.MarkLoading();
                    ItemsView.Refresh();

                    try
                    {
                        var options = _ioOptions.BuildReadOptions(item.Format);
                        var document = await LoadDocumentAsync(item, options, token).ConfigureAwait(true);
                        item.MarkLoaded(document);
                    }
                    catch (OperationCanceledException)
                    {
                        item.MarkCancelled();
                    }
                    catch (Exception ex)
                    {
                        item.MarkFailed(ex.Message);
                    }
                }

                if (item.Document is not null && !loadedDocuments.Contains(item.Document))
                {
                    loadedDocuments.Add(item.Document);
                }

                processed++;
                UpdateProgress(processed, items.Count);
            }

            ScriptResultsStatusMessage = "Running scripts across batch...";
            UpdateProgress(0, items.Count);
            processed = 0;
            var successCount = 0;
            var failureCount = 0;

            foreach (var item in items)
            {
                if (token.IsCancellationRequested)
                {
                    item.MarkCancelled();
                    continue;
                }

                if (item.Document is null)
                {
                    AddScriptResult(item, CadScriptExecutionResult.FromException(
                        new InvalidOperationException("Document failed to load."),
                        string.Empty,
                        Array.Empty<string>(),
                        TimeSpan.Zero));
                    failureCount++;
                    processed++;
                    UpdateProgress(processed, items.Count);
                    continue;
                }

                var globals = new CadScriptGlobals
                {
                    Document = item.Document,
                    Documents = loadedDocuments,
                    Selection = null,
                    Format = item.Format,
                    DocumentName = item.FileName,
                    DocumentPath = item.Path,
                    CancellationToken = token
                };

                var result = await _scriptHost.ExecuteAsync(scriptText, globals, token).ConfigureAwait(true);
                AddScriptResult(item, result);

                if (result.Success)
                {
                    successCount++;
                }
                else
                {
                    failureCount++;
                }

                processed++;
                UpdateProgress(processed, items.Count);
            }

            ScriptResultsStatusMessage = token.IsCancellationRequested
                ? "Batch script run cancelled."
                : $"Batch script run complete. {successCount} succeeded, {failureCount} failed.";
        }
        finally
        {
            IsRunning = false;
            _processing?.Dispose();
            _processing = null;
        }
    }

    private async Task ExportCsvAsync(CancellationToken cancellationToken)
    {
        if (_resultModels.Count == 0)
        {
            ResultsStatusMessage = "No results to export.";
            return;
        }

        var result = await _exportService
            .SaveExportAsync(CadBatchExportFormat.Csv, "batch-results.csv", cancellationToken)
            .ConfigureAwait(true);
        if (result is null)
        {
            return;
        }

        var csv = BuildCsv(_resultModels);
        await using var stream = await result.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(csv.AsMemory(), cancellationToken).ConfigureAwait(false);
        ResultsStatusMessage = $"Exported CSV to {result.FileName}.";
    }

    private async Task ExportJsonAsync(CancellationToken cancellationToken)
    {
        if (_resultModels.Count == 0)
        {
            ResultsStatusMessage = "No results to export.";
            return;
        }

        var result = await _exportService
            .SaveExportAsync(CadBatchExportFormat.Json, "batch-results.json", cancellationToken)
            .ConfigureAwait(true);
        if (result is null)
        {
            return;
        }

        var export = new CadBatchSearchExport(QueryText ?? string.Empty, DateTimeOffset.UtcNow, _resultModels);
        var json = JsonSerializer.Serialize(export, BatchExportJsonContext.Default.CadBatchSearchExport);
        await using var stream = await result.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        ResultsStatusMessage = $"Exported JSON to {result.FileName}.";
    }

    private static string BuildCsv(IReadOnlyList<CadBatchSearchResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Document,Format,ObjectPath,Kind,Type,Name,Handle,Match,DocumentPath");

        foreach (var result in results)
        {
            builder.Append(EscapeCsv(result.DocumentName)).Append(',')
                .Append(EscapeCsv(result.Format.ToString())).Append(',')
                .Append(EscapeCsv(result.ObjectPath)).Append(',')
                .Append(EscapeCsv(result.Kind)).Append(',')
                .Append(EscapeCsv(result.TypeName)).Append(',')
                .Append(EscapeCsv(result.Name)).Append(',')
                .Append(EscapeCsv(result.Handle)).Append(',')
                .Append(EscapeCsv(result.MatchText)).Append(',')
                .Append(EscapeCsv(result.DocumentPath))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuotes = value.Contains(',') || value.Contains('\"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuotes)
        {
            return value;
        }

        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
