using System.Threading;
using System.Threading.Tasks;
using ProCad.Editing.Clipboard;

namespace ProCad.Services;

public sealed class AvaloniaCadSystemClipboardBridge : ICadSystemClipboardBridge
{
    private readonly ICadClipboardPlatformFacade _clipboardPlatform;

    public AvaloniaCadSystemClipboardBridge(ICadClipboardPlatformFacade clipboardPlatform)
    {
        _clipboardPlatform = clipboardPlatform;
    }

    public async ValueTask WriteAsync(CadClipboardPayload payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = CadClipboardPayloadSerializer.Serialize(payload);
        string? dxfText = null;
        if (CadClipboardDxfFallbackCodec.TryExport(payload, out var exportedDxf) &&
            !string.IsNullOrWhiteSpace(exportedDxf))
        {
            dxfText = exportedDxf;
        }

        var textPayload = $"{CadClipboardFormats.CadTextPrefix}{json}";
        await _clipboardPlatform
            .WriteAsync(json, dxfText, textPayload, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<CadClipboardPayload?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var jsonMimeFormats = new[] { CadClipboardFormats.CadJsonMime, CadClipboardFormats.LegacyCadJsonMime };
        foreach (var jsonMimeFormat in jsonMimeFormats)
        {
            var json = await _clipboardPlatform
                .ReadFormatAsync(jsonMimeFormat, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(json) &&
                CadClipboardPayloadSerializer.TryDeserialize(json, out var payloadFromMime))
            {
                return payloadFromMime;
            }
        }

        var text = await _clipboardPlatform.ReadTextAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (CadClipboardFormats.TryRemoveCadTextPrefix(text, out var serialized))
        {
            if (CadClipboardPayloadSerializer.TryDeserialize(serialized, out var payloadFromText))
            {
                return payloadFromText;
            }
        }

        if (CadClipboardPayloadSerializer.TryDeserialize(text, out var payloadFromPlainJson))
        {
            return payloadFromPlainJson;
        }

        return null;
    }
}
