using System.Collections.Generic;

namespace ACadInspector.Core;

public sealed class CadDocumentDiffResult
{
    public CadDocumentDiffResult(
        IReadOnlyList<CadObjectDiff> added,
        IReadOnlyList<CadObjectDiff> removed,
        IReadOnlyList<CadObjectDiff> modified,
        IReadOnlyList<CadObjectDiff> unchanged)
    {
        Added = added;
        Removed = removed;
        Modified = modified;
        Unchanged = unchanged;
    }

    public IReadOnlyList<CadObjectDiff> Added { get; }

    public IReadOnlyList<CadObjectDiff> Removed { get; }

    public IReadOnlyList<CadObjectDiff> Modified { get; }

    public IReadOnlyList<CadObjectDiff> Unchanged { get; }
}
