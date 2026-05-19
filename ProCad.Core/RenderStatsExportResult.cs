using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ProCad.Core;

public sealed record RenderStatsExportResult(
    string? Path,
    string FileName,
    Func<CancellationToken, ValueTask<Stream>> OpenWriteAsync);
