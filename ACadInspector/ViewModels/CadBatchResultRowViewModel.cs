using ACadInspector.Core;

namespace ACadInspector.ViewModels;

public sealed class CadBatchResultRowViewModel
{
    public CadBatchResultRowViewModel(CadBatchSearchResult result)
    {
        Document = result.DocumentName;
        Format = result.Format.ToString();
        DocumentPath = result.DocumentPath;
        ObjectPath = result.ObjectPath;
        Kind = result.Kind;
        TypeName = result.TypeName;
        Name = result.Name;
        Handle = result.Handle;
        Match = result.MatchText;
    }

    public string Document { get; }

    public string Format { get; }

    public string DocumentPath { get; }

    public string ObjectPath { get; }

    public string Kind { get; }

    public string TypeName { get; }

    public string Name { get; }

    public string Handle { get; }

    public string Match { get; }
}
