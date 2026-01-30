using System.Collections.Generic;

namespace ACadInspector.Core;

public sealed class CadBatchQuery
{
    public CadBatchQuery(IReadOnlyList<CadBatchQueryTerm> terms)
    {
        Terms = terms;
    }

    public IReadOnlyList<CadBatchQueryTerm> Terms { get; }
}
