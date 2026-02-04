using System.Reactive.Linq;
using ACadInspector.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadLayerToolViewModel : CadToolViewModelBase
{
    private readonly CadRenderLayerListViewModel _emptyLayerList;

    [Reactive]
    public partial CadRenderLayerListViewModel LayerList { get; set; }

    public CadLayerToolViewModel(CadDocumentContextService documentContext)
    {
        _emptyLayerList = new CadRenderLayerListViewModel(null);
        LayerList = _emptyLayerList;

        documentContext.WhenAnyValue(x => x.ActiveDocument)
            .Select(document => document?.Render?.LayerList ?? _emptyLayerList)
            .DistinctUntilChanged()
            .BindTo(this, x => x.LayerList);
    }
}
