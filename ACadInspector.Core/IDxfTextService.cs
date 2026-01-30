using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ACadInspector.Core;

public interface IDxfTextService
{
    ValueTask<DxfTextLoadResult> TryLoadAsciiDxfAsync(
        CadFileFormat? format,
        string? path,
        Func<CancellationToken, ValueTask<Stream>>? openRead,
        CancellationToken cancellationToken);
}
