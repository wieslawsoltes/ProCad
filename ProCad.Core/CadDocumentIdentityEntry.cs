namespace ProCad.Core;

public sealed class CadDocumentIdentityEntry
{
    public CadDocumentIdentityEntry(string path, string kind, string typeName, object? value)
    {
        Path = path;
        Kind = kind;
        TypeName = typeName;
        Value = value;
    }

    public string Path { get; }

    public string Kind { get; }

    public string TypeName { get; }

    public object? Value { get; }
}
