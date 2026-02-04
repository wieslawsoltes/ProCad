using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Services;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadBlocksToolViewModel : CadToolViewModelBase
{
    private readonly ObservableCollection<CadBlockRowViewModel> _blockRows = new();
    private readonly CadDocumentContextService _documentContext;
    private readonly CadBlockPreviewService _previewService;
    private readonly CadBlockEditorService _blockEditorService;
    private CancellationTokenSource? _previewCts;
    private const int PreviewSize = 72;

    [Reactive]
    public partial string SearchText { get; set; } = string.Empty;

    [Reactive]
    public partial CadBlockRowViewModel? SelectedBlock { get; set; }

    public DataGridCollectionView BlocksView { get; }
    public DataGridColumnDefinitionList ColumnDefinitions { get; }
    public SortingModel SortingModel { get; } = new();
    public FilteringModel FilteringModel { get; } = new();
    public SearchModel SearchModel { get; } = new();

    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
    public ReactiveCommand<CadBlockRowViewModel?, Unit> OpenBlockCommand { get; }

    public CadBlocksToolViewModel(
        CadDocumentContextService documentContext,
        CadBlockPreviewService previewService,
        CadBlockEditorService blockEditorService)
    {
        _documentContext = documentContext;
        _previewService = previewService;
        _blockEditorService = blockEditorService;

        BlocksView = new DataGridCollectionView(_blockRows);
        ColumnDefinitions = CadBlockColumnDefinitions.Create();

        SearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        SearchModel.HighlightCurrent = true;
        SearchModel.WrapNavigation = true;

        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => ApplySearch());

        ClearSearchCommand = ReactiveCommand.Create(() => { SearchText = string.Empty; });

        var canOpen = this.WhenAnyValue(x => x.SelectedBlock)
            .CombineLatest(
                _documentContext.WhenAnyValue(x => x.ActiveDocument),
                static (selected, active) => selected is not null && active is not null);
        OpenBlockCommand = ReactiveCommand.Create<CadBlockRowViewModel?>(OpenSelectedBlock, canOpen);

        _documentContext.WhenAnyValue(x => x.ActiveDocument)
            .Subscribe(LoadBlocks);
    }

    private void LoadBlocks(CadDocumentViewModel? documentViewModel)
    {
        _blockRows.Clear();
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();

        var document = documentViewModel?.Document;
        if (document?.BlockRecords is null)
        {
            BlocksView.Refresh();
            return;
        }

        foreach (var record in document.BlockRecords)
        {
            if (record is null)
            {
                continue;
            }

            var row = new CadBlockRowViewModel(record);
            _blockRows.Add(row);
            QueuePreview(documentViewModel, row);
        }

        BlocksView.Refresh();
        ApplySearch();
    }

    private void ApplySearch()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchModel.Clear();
            return;
        }

        var descriptor = new SearchDescriptor(
            SearchText.Trim(),
            matchMode: SearchMatchMode.Contains,
            termMode: SearchTermCombineMode.Any,
            scope: SearchScope.VisibleColumns,
            comparison: StringComparison.OrdinalIgnoreCase);

        SearchModel.SetOrUpdate(descriptor);
    }

    private void OpenSelectedBlock(CadBlockRowViewModel? row)
    {
        var activeDocument = _documentContext.ActiveDocument;
        var selected = row ?? SelectedBlock;
        if (activeDocument is null || selected is null)
        {
            return;
        }

        _blockEditorService.TryOpenBlockEditor(selected.Block, activeDocument);
    }

    private void QueuePreview(CadDocumentViewModel? documentViewModel, CadBlockRowViewModel row)
    {
        if (documentViewModel is null || _previewCts is null)
        {
            return;
        }

        var token = _previewCts.Token;
        _ = Task.Run(async () =>
        {
            var preview = await _previewService.GetPreviewAsync(
                    documentViewModel,
                    row.Block,
                    PreviewSize,
                    renderAttributes: true,
                    renderAttributeDefinitions: true,
                    cancellationToken: token)
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
}
