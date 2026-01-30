using System;
using System.Collections.Generic;
using System.Threading;
using ACadInspector.Core;
using ACadSharp;

namespace ACadInspector.Scripting;

public sealed class CadScriptGlobals
{
    public CadDocument? Document { get; init; }

    public IReadOnlyList<CadDocument> Documents { get; init; } = Array.Empty<CadDocument>();

    public object? Selection { get; init; }

    public CadFileFormat? Format { get; init; }

    public string? DocumentName { get; init; }

    public string? DocumentPath { get; init; }

    public Action<string>? Log { get; init; }

    public CancellationToken CancellationToken { get; init; }
}
