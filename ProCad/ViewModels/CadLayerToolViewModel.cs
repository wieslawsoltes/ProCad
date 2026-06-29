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

public sealed partial class CadLayerToolViewModel : CadToolViewModelBase
{
    private readonly CadRenderLayerListViewModel _emptyLayerList;

    [Reactive]
    public partial CadRenderLayerListViewModel LayerList { get; set; }

    [Reactive]
    public partial DataGridCollectionView LayerRowsView { get; set; }

    [Reactive]
    public partial DataGridColumnDefinitionList LayerColumnDefinitions { get; set; }

    [Reactive]
    public partial SortingModel LayerSortingModel { get; set; }

    [Reactive]
    public partial FilteringModel LayerFilteringModel { get; set; }

    [Reactive]
    public partial SearchModel LayerSearchModel { get; set; }

    public CadLayerToolViewModel(CadDocumentContextService documentContext)
    {
        _emptyLayerList = new CadRenderLayerListViewModel(null);
        LayerList = _emptyLayerList;
        LayerRowsView = _emptyLayerList.LayerRowsView;
        LayerColumnDefinitions = CadRenderLayerColumnDefinitions.Create();
        LayerSortingModel = _emptyLayerList.LayerSortingModel;
        LayerFilteringModel = _emptyLayerList.LayerFilteringModel;
        LayerSearchModel = _emptyLayerList.LayerSearchModel;

        documentContext.WhenAnyValue(x => x.ActiveDocument)
            .Select(document => document?.Render?.LayerList ?? _emptyLayerList)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .DistinctUntilChanged()
            .BindTo(this, x => x.LayerList);

        this.WhenAnyValue(x => x.LayerList)
            .Select(static list => list.LayerRowsView)
            .BindTo(this, x => x.LayerRowsView);

        this.WhenAnyValue(x => x.LayerList)
            .Select(static list => list.LayerSortingModel)
            .BindTo(this, x => x.LayerSortingModel);

        this.WhenAnyValue(x => x.LayerList)
            .Select(static list => list.LayerFilteringModel)
            .BindTo(this, x => x.LayerFilteringModel);

        this.WhenAnyValue(x => x.LayerList)
            .Select(static list => list.LayerSearchModel)
            .BindTo(this, x => x.LayerSearchModel);
    }
}
