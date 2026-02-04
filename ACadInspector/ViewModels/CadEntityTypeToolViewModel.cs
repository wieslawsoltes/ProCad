using System.Reactive.Linq;
using ACadInspector.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadEntityTypeToolViewModel : CadToolViewModelBase
{
    private readonly CadRenderEntityTypeListViewModel _emptyList;

    [Reactive]
    public partial CadRenderEntityTypeListViewModel EntityTypeList { get; set; }

    public CadEntityTypeToolViewModel(CadDocumentContextService documentContext)
    {
        _emptyList = new CadRenderEntityTypeListViewModel(null);
        EntityTypeList = _emptyList;

        documentContext.WhenAnyValue(x => x.ActiveDocument)
            .Select(document => document?.Render.EntityTypeList ?? _emptyList)
            .DistinctUntilChanged()
            .BindTo(this, x => x.EntityTypeList);
    }
}
