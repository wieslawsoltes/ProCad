using System.Threading;
using System.Threading.Tasks;

namespace ProCad.Services;

public interface ICadClipboardPlatformFacade
{
    Task WriteAsync(
        string cadJson,
        string? dxfText,
        string textPayload,
        CancellationToken cancellationToken = default);

    Task<string?> ReadFormatAsync(
        string formatIdentifier,
        CancellationToken cancellationToken = default);

    Task<string?> ReadTextAsync(CancellationToken cancellationToken = default);
}
