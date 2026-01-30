using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ACadInspector.Core;

public sealed record CadBatchExportResult(
    string? Path,
    string FileName,
    CadBatchExportFormat Format,
    Func<CancellationToken, ValueTask<Stream>> OpenWriteAsync);
