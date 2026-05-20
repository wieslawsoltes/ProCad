using System;
using System.Collections.Generic;

namespace ProCad.Core;

public sealed class CadBatchSearchExport
{
    public CadBatchSearchExport(string query, DateTimeOffset createdAt, IReadOnlyList<CadBatchSearchResult> results)
    {
        Query = query;
        CreatedAt = createdAt;
        Results = results;
    }

    public string Query { get; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<CadBatchSearchResult> Results { get; }
}
