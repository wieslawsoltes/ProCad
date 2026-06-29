using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProCad.Editing.Prompt;
using ProCad.Services;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class CadBlocksToolViewModel : CadToolViewModelBase
{
    private readonly ObservableCollection<CadBlockRowViewModel> _blockRows = new();
    private readonly Dictionary<BlockRecord, CadBlockRowViewModel> _rowMap = new(ReferenceEqualityComparer.Instance);
    private readonly CadDocumentContextService _documentContext;
    private readonly CadBlockPreviewService _previewService;
    private readonly CadBlockEditorService _blockEditorService;
    private readonly CadEditorControllerHostService _controllerHost;
    private readonly CadEditorSessionHostService _sessionHost;
    private readonly CadSelectionService _selectionService;
    private CancellationTokenSource? _previewCts;
    private const int PreviewSize = 72;
    private bool _suppressSelection;

    [Reactive]
    public partial string SearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string FilterText { get; set; } = string.Empty;

    [Reactive]
    public partial CadBlockRowViewModel? SelectedBlock { get; set; }

    public DataGridCollectionView BlocksView { get; }
    public DataGridColumnDefinitionList ColumnDefinitions { get; }
    public SortingModel SortingModel { get; } = new();
    public FilteringModel FilteringModel { get; } = new();
    public SearchModel SearchModel { get; } = new();

    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearFilterCommand { get; }
    public ReactiveCommand<CadBlockRowViewModel?, Unit> OpenBlockCommand { get; }
    public ReactiveCommand<CadBlockRowViewModel?, Unit> InsertBlockCommand { get; }
    public ReactiveCommand<CadBlockRowViewModel?, Unit> ReloadXRefCommand { get; }
    public ReactiveCommand<CadBlockRowViewModel?, Unit> BindXRefCommand { get; }
    public ReactiveCommand<CadBlockRowViewModel?, Unit> DetachXRefCommand { get; }

    public CadBlocksToolViewModel(
        CadDocumentContextService documentContext,
        CadBlockPreviewService previewService,
        CadBlockEditorService blockEditorService,
        CadEditorControllerHostService controllerHost,
        CadEditorSessionHostService sessionHost,
        CadSelectionService selectionService)
    {
        _documentContext = documentContext;
        _previewService = previewService;
        _blockEditorService = blockEditorService;
        _controllerHost = controllerHost;
        _sessionHost = sessionHost;
        _selectionService = selectionService;

        BlocksView = new DataGridCollectionView(_blockRows);
        ColumnDefinitions = CadBlockColumnDefinitions.Create();

        SearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        SearchModel.HighlightCurrent = true;
        SearchModel.WrapNavigation = true;

        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => ApplySearch());

        this.WhenAnyValue(x => x.FilterText)
            .Subscribe(_ => ApplyFilter());

        ClearSearchCommand = ReactiveCommand.Create(() => { SearchText = string.Empty; });
        ClearFilterCommand = ReactiveCommand.Create(() => { FilterText = string.Empty; });

        var canOpen = this.WhenAnyValue(x => x.SelectedBlock)
            .CombineLatest(
                _documentContext.WhenAnyValue(x => x.ActiveDocument),
                static (selected, active) => selected is not null && active is not null);
        OpenBlockCommand = ReactiveCommand.Create<CadBlockRowViewModel?>(OpenSelectedBlock, canOpen);
        InsertBlockCommand = ReactiveCommand.CreateFromTask<CadBlockRowViewModel?>(StartInsertForBlockAsync, canOpen);
        var canApplyXRefWorkflow = canOpen.Select(static canOpenCurrent => canOpenCurrent)
            .CombineLatest(
                this.WhenAnyValue(x => x.SelectedBlock),
                static (canOpenCurrent, selected) => canOpenCurrent && selected?.IsXRef == true);
        ReloadXRefCommand = ReactiveCommand.CreateFromTask<CadBlockRowViewModel?>(
            row => StartXRefWorkflowAsync(row, "XREFRELOAD"),
            canApplyXRefWorkflow);
        BindXRefCommand = ReactiveCommand.CreateFromTask<CadBlockRowViewModel?>(
            row => StartXRefWorkflowAsync(row, "XREFBIND"),
            canApplyXRefWorkflow);
        DetachXRefCommand = ReactiveCommand.CreateFromTask<CadBlockRowViewModel?>(
            row => StartXRefWorkflowAsync(row, "XREFDETACH"),
            canApplyXRefWorkflow);

        this.WhenAnyValue(x => x.SelectedBlock)
            .Subscribe(OnSelectedBlockChanged);

        _selectionService.WhenAnyValue(x => x.SelectedObject)
            .Subscribe(UpdateSelectionFromService);

        _documentContext.WhenAnyValue(x => x.ActiveDocument)
            .Subscribe(LoadBlocks);
    }

    private void LoadBlocks(CadDocumentViewModel? documentViewModel)
    {
        _blockRows.Clear();
        _rowMap.Clear();
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
            _rowMap[record] = row;
            QueuePreview(documentViewModel, row);
        }

        BlocksView.Refresh();
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

    private async Task StartInsertForBlockAsync(CadBlockRowViewModel? row)
    {
        var activeDocument = _documentContext.ActiveDocument;
        var selected = row ?? SelectedBlock;
        if (activeDocument is null || selected is null)
        {
            return;
        }

        var controller = _controllerHost.GetOrCreate(activeDocument.Document);
        controller.BeginCommand("INSERT");
        await controller.SubmitTokenAsync(
                new CadPromptToken(CadPromptTokenType.Text, selected.Block.Name),
                commit: false)
            .ConfigureAwait(false);
    }

    private async Task StartXRefWorkflowAsync(CadBlockRowViewModel? row, string commandName)
    {
        var activeDocument = _documentContext.ActiveDocument;
        var selected = row ?? SelectedBlock;
        if (activeDocument is null || selected is null || !selected.IsXRef)
        {
            return;
        }

        var controller = _controllerHost.GetOrCreate(activeDocument.Document);
        controller.BeginCommand(commandName);
        var resolution = await controller.SubmitTokenAsync(
                new CadPromptToken(CadPromptTokenType.Text, selected.Block.Name),
                commit: true)
            .ConfigureAwait(false);
        if (resolution.Result?.Success != true)
        {
            return;
        }

        if (controller.Session is not null)
        {
            _sessionHost.NotifySessionChanged(controller.Session);
        }

        if (string.Equals(commandName, "XREFDETACH", StringComparison.OrdinalIgnoreCase))
        {
            _selectionService.SelectedObject = null;
        }

        LoadBlocks(activeDocument);
    }

    private void OnSelectedBlockChanged(CadBlockRowViewModel? row)
    {
        if (_suppressSelection)
        {
            return;
        }

        _selectionService.SelectedObject = row?.Block;
    }

    private void UpdateSelectionFromService(object? selected)
    {
        if (_suppressSelection)
        {
            return;
        }

        var block = ResolveBlockRecord(selected);
        _suppressSelection = true;
        SelectedBlock = block is not null && _rowMap.TryGetValue(block, out var row) ? row : null;
        _suppressSelection = false;
    }

    private static BlockRecord? ResolveBlockRecord(object? selected)
    {
        switch (selected)
        {
            case CadDocumentTreeNode node:
                return ResolveBlockRecord(node.Source);
            case BlockRecord block:
                return block;
            case ACadSharp.Entities.Insert insert:
                return insert.Block;
        }

        return null;
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

            RxSchedulers.MainThreadScheduler.Schedule(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    row.Preview = preview;
                }
            });
        }, CancellationToken.None);
    }
}
