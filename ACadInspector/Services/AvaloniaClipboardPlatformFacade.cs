using System;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Editing.Clipboard;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace ACadInspector.Services;

public sealed class AvaloniaClipboardPlatformFacade : ICadClipboardPlatformFacade
{
    private static readonly DataFormat<string> CadJsonFormat =
        DataFormat.CreateStringApplicationFormat(CadClipboardFormats.CadJsonMime);

    private static readonly DataFormat<string> CadDxfFormat =
        DataFormat.CreateStringApplicationFormat(CadClipboardFormats.CadDxfMime);

    private readonly IClipboardAccessor _clipboardAccessor;

    public AvaloniaClipboardPlatformFacade(IClipboardAccessor clipboardAccessor)
    {
        _clipboardAccessor = clipboardAccessor;
    }

    public async Task WriteAsync(
        string cadJson,
        string? dxfText,
        string textPayload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clipboard = _clipboardAccessor.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        var transferItem = new DataTransferItem();
        transferItem.Set(CadJsonFormat, cadJson);

        if (!string.IsNullOrWhiteSpace(dxfText))
        {
            transferItem.Set(CadDxfFormat, dxfText);
        }

        transferItem.SetText(textPayload);
        var transfer = new DataTransfer();
        transfer.Add(transferItem);
        await clipboard.SetDataAsync(transfer).ConfigureAwait(false);
        await clipboard.FlushAsync().ConfigureAwait(false);
    }

    public async Task<string?> ReadFormatAsync(
        string formatIdentifier,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clipboard = _clipboardAccessor.Clipboard;
        if (clipboard is null || string.IsNullOrWhiteSpace(formatIdentifier))
        {
            return null;
        }

        var format = string.Equals(formatIdentifier, CadClipboardFormats.CadJsonMime, StringComparison.Ordinal)
            ? CadJsonFormat
            : string.Equals(formatIdentifier, CadClipboardFormats.CadDxfMime, StringComparison.Ordinal)
                ? CadDxfFormat
                : DataFormat.CreateStringApplicationFormat(formatIdentifier);
        return await clipboard.TryGetValueAsync(format).ConfigureAwait(false);
    }

    public async Task<string?> ReadTextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clipboard = _clipboardAccessor.Clipboard;
        if (clipboard is null)
        {
            return null;
        }

        return await clipboard.TryGetTextAsync().ConfigureAwait(false);
    }
}
