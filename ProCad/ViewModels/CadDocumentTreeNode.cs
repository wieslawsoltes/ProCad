using System.Collections.Generic;

namespace ProCad.ViewModels;

public sealed class CadDocumentTreeNode
{
    private readonly IReadOnlyList<CadDocumentTreeNode> _children;

    public string Name { get; }
    public string Kind { get; }
    public string TypeName { get; }
    public string? Handle { get; }
    public string HandleText => Handle ?? string.Empty;
    public IReadOnlyList<CadDocumentTreeNode> Children => _children;
    public int ChildCount => _children.Count;
    public string ChildCountText => _children.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public object? Source { get; }

    public CadDocumentTreeNode(
        string name,
        string kind,
        string typeName,
        string? handle,
        IReadOnlyList<CadDocumentTreeNode> children,
        object? source)
    {
        Name = name;
        Kind = kind;
        TypeName = typeName;
        Handle = handle;
        _children = children;
        Source = source;
    }
}
