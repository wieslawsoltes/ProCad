using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.Services;

public sealed partial class CadSelectionService : ReactiveObject
{
    [Reactive]
    public partial object? SelectedObject { get; set; }
}
