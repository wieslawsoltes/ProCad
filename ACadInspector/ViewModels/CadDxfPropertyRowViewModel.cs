namespace ACadInspector.ViewModels;

public sealed class CadDxfPropertyRowViewModel : ViewModelBase
{
    public string Name { get; }
    public string Codes { get; }
    public string ReferenceType { get; }
    public string Value { get; }

    public CadDxfPropertyRowViewModel(string name, string codes, string referenceType, string value)
    {
        Name = name;
        Codes = codes;
        ReferenceType = referenceType;
        Value = value;
    }
}
