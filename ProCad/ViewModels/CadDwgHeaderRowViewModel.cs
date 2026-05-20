namespace ProCad.ViewModels;

public sealed class CadDwgHeaderRowViewModel
{
    public CadDwgHeaderRowViewModel(
        string variable,
        string property,
        string codes,
        string reference,
        string type,
        string value)
    {
        Variable = variable;
        Property = property;
        Codes = codes;
        Reference = reference;
        Type = type;
        Value = value;
    }

    public string Variable { get; }

    public string Property { get; }

    public string Codes { get; }

    public string Reference { get; }

    public string Type { get; }

    public string Value { get; }
}
