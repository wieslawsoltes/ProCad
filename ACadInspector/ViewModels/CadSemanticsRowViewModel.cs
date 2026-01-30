namespace ACadInspector.ViewModels;

public sealed class CadSemanticsRowViewModel : ViewModelBase
{
    public string Name { get; }
    public string Value { get; }

    public CadSemanticsRowViewModel(string name, string value)
    {
        Name = name;
        Value = value;
    }
}
