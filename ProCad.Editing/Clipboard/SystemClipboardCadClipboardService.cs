namespace ProCad.Editing.Clipboard;

public sealed class SystemClipboardCadClipboardService : ICadClipboardService, ICadSystemClipboardSync
{
    private readonly InMemoryCadClipboardService _localClipboard = new();
    private readonly ICadSystemClipboardBridge? _systemBridge;

    public SystemClipboardCadClipboardService(ICadSystemClipboardBridge? systemBridge = null)
    {
        _systemBridge = systemBridge;
    }

    public void SetPayload(CadClipboardPayload payload)
    {
        _localClipboard.SetPayload(payload);
    }

    public bool TryGetPayload(out CadClipboardPayload payload)
    {
        return _localClipboard.TryGetPayload(out payload);
    }

    public void Clear()
    {
        _localClipboard.Clear();
    }

    public async ValueTask PublishAsync(CadClipboardPayload payload, CancellationToken cancellationToken = default)
    {
        _localClipboard.SetPayload(payload);
        if (_systemBridge is null)
        {
            return;
        }

        await _systemBridge.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> TryHydrateAsync(CancellationToken cancellationToken = default)
    {
        if (_localClipboard.TryGetPayload(out _))
        {
            return true;
        }

        if (_systemBridge is null)
        {
            return false;
        }

        var payload = await _systemBridge.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            return false;
        }

        _localClipboard.SetPayload(payload);
        return true;
    }
}
