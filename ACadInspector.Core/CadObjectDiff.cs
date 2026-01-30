using System.Collections.Generic;

namespace ACadInspector.Core;

public sealed class CadObjectDiff
{
    public CadObjectDiff(
        string path,
        string kind,
        string typeName,
        CadDiffKind diffKind,
        IReadOnlyList<CadPropertyDiff> propertyDiffs)
    {
        Path = path;
        Kind = kind;
        TypeName = typeName;
        DiffKind = diffKind;
        PropertyDiffs = propertyDiffs;
    }

    public string Path { get; }

    public string Kind { get; }

    public string TypeName { get; }

    public CadDiffKind DiffKind { get; }

    public IReadOnlyList<CadPropertyDiff> PropertyDiffs { get; }
}
