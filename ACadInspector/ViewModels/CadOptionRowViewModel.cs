using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadOptionRowViewModel : ViewModelBase
{
    public string Name { get; }
    public string Description { get; }

    [Reactive]
    public partial bool Value { get; set; }

    public CadOptionRowViewModel(string name, string description, bool value)
    {
        Name = name;
        Description = description;
        Value = value;
    }
}
