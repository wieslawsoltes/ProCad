namespace ProCad.Core;

public sealed class CadBatchSearchResult
{
    public CadBatchSearchResult(
        string documentName,
        string documentPath,
        CadFileFormat format,
        string objectPath,
        string kind,
        string typeName,
        string name,
        string handle,
        string matchText)
    {
        DocumentName = documentName;
        DocumentPath = documentPath;
        Format = format;
        ObjectPath = objectPath;
        Kind = kind;
        TypeName = typeName;
        Name = name;
        Handle = handle;
        MatchText = matchText;
    }

    public string DocumentName { get; }

    public string DocumentPath { get; }

    public CadFileFormat Format { get; }

    public string ObjectPath { get; }

    public string Kind { get; }

    public string TypeName { get; }

    public string Name { get; }

    public string Handle { get; }

    public string MatchText { get; }
}
