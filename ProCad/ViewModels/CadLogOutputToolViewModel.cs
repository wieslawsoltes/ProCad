using System.Collections.Specialized;
using System.Reactive;
using ProCad.Diagnostics;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class CadLogOutputToolViewModel : CadToolViewModelBase
{
    private readonly IAppLogService _logService;

    public DataGridCollectionView EntriesView { get; }
    public DataGridColumnDefinitionList ColumnDefinitions { get; }
    public SortingModel SortingModel { get; } = new();
    public FilteringModel FilteringModel { get; } = new();
    public SearchModel SearchModel { get; } = new();

    [Reactive]
    public partial string SearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string FilterText { get; set; } = string.Empty;

    [Reactive]
    public partial string StatusText { get; set; } = "No log entries.";

    public ReactiveCommand<Unit, Unit> ClearLogCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearFilterCommand { get; }

    public CadLogOutputToolViewModel(IAppLogService logService)
    {
        _logService = logService;

        EntriesView = new DataGridCollectionView(logService.Entries);
        ColumnDefinitions = CadLogOutputColumnDefinitions.Create();

        SearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        SearchModel.HighlightCurrent = true;
        SearchModel.WrapNavigation = true;

        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(Observer.Create<string>(_ => ApplySearch()));

        this.WhenAnyValue(x => x.FilterText)
            .Subscribe(Observer.Create<string>(_ => ApplyFilter()));

        if (logService.Entries is INotifyCollectionChanged changed)
        {
            changed.CollectionChanged += OnEntriesChanged;
        }

        ClearLogCommand = ReactiveCommand.Create(logService.Clear);
        ClearSearchCommand = ReactiveCommand.Create(() => { SearchText = string.Empty; });
        ClearFilterCommand = ReactiveCommand.Create(() => { FilterText = string.Empty; });

        UpdateStatus();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        EntriesView.Refresh();
        UpdateStatus();
    }

    private void ApplySearch()
    {
        DataGridFilterHelper.ApplySearch(SearchModel, SearchText);
    }

    private void ApplyFilter()
    {
        DataGridFilterHelper.ApplyFilter(FilteringModel, ColumnDefinitions, FilterText);
    }

    private void UpdateStatus()
    {
        var count = _logService.Entries.Count;
        StatusText = count == 0
            ? $"No log entries. File: {_logService.LogPath}"
            : $"{count:n0} entries. File: {_logService.LogPath}";
    }
}
