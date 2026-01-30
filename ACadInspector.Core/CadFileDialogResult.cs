using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ACadInspector.Core;

public sealed record CadOpenFileResult(
    string? Path,
    string FileName,
    CadFileFormat Format,
    Func<CancellationToken, ValueTask<Stream>> OpenReadAsync);

public sealed record CadSaveFileResult(
    string? Path,
    string FileName,
    CadFileFormat Format,
    Func<CancellationToken, ValueTask<Stream>> OpenWriteAsync);
