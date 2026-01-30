namespace ACadInspector.ViewModels;

public sealed class CadDwgSummaryRowViewModel
{
    public CadDwgSummaryRowViewModel(string category, string name, string value)
    {
        Category = category;
        Name = name;
        Value = value;
    }

    public string Category { get; }

    public string Name { get; }

    public string Value { get; }
}
