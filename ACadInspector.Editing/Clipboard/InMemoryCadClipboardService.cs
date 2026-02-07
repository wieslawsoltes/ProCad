namespace ACadInspector.Editing.Clipboard;

public sealed class InMemoryCadClipboardService : ICadClipboardService
{
    private CadClipboardPayload? _payload;

    public void SetPayload(CadClipboardPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _payload = payload;
    }

    public bool TryGetPayload(out CadClipboardPayload payload)
    {
        if (_payload is null)
        {
            payload = null!;
            return false;
        }

        payload = _payload;
        return true;
    }

    public void Clear()
    {
        _payload = null;
    }
}
