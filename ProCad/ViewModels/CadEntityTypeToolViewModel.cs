using System.Reactive.Linq;
using ProCad.Services;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class CadEntityTypeToolViewModel : CadToolViewModelBase
{
    private readonly CadRenderEntityTypeListViewModel _emptyList;

    [Reactive]
    public partial CadRenderEntityTypeListViewModel EntityTypeList { get; set; }

    [Reactive]
    public partial DataGridCollectionView RowsView { get; set; }

    [Reactive]
    public partial DataGridColumnDefinitionList ColumnDefinitions { get; set; }

    [Reactive]
    public partial SortingModel SortingModel { get; set; }

    [Reactive]
    public partial FilteringModel FilteringModel { get; set; }

    [Reactive]
    public partial SearchModel SearchModel { get; set; }

    public CadEntityTypeToolViewModel(CadDocumentContextService documentContext)
    {
        _emptyList = new CadRenderEntityTypeListViewModel(null);
        EntityTypeList = _emptyList;
        RowsView = _emptyList.RowsView;
        ColumnDefinitions = CadRenderEntityTypeColumnDefinitions.Create();
        SortingModel = _emptyList.SortingModel;
        FilteringModel = _emptyList.FilteringModel;
        SearchModel = _emptyList.SearchModel;

        documentContext.WhenAnyValue(x => x.ActiveDocument)
            .Select(document => document?.Render.EntityTypeList ?? _emptyList)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .DistinctUntilChanged()
            .BindTo(this, x => x.EntityTypeList);

        this.WhenAnyValue(x => x.EntityTypeList)
            .Select(list => list?.RowsView ?? _emptyList.RowsView)
            .BindTo(this, x => x.RowsView);

        this.WhenAnyValue(x => x.EntityTypeList)
            .Select(list => list?.SortingModel ?? _emptyList.SortingModel)
            .BindTo(this, x => x.SortingModel);

        this.WhenAnyValue(x => x.EntityTypeList)
            .Select(list => list?.FilteringModel ?? _emptyList.FilteringModel)
            .BindTo(this, x => x.FilteringModel);

        this.WhenAnyValue(x => x.EntityTypeList)
            .Select(list => list?.SearchModel ?? _emptyList.SearchModel)
            .BindTo(this, x => x.SearchModel);
    }
}
