using System.Reactive.Linq;
using ACadInspector.Services;
using Dock.Model.ReactiveUI.Controls;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadLayerToolViewModel : Tool
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
