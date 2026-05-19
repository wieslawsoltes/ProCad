using System;
using Avalonia.Input.Platform;

namespace ProCad.Services;

public sealed class ClipboardAccessor : IClipboardAccessor
{
    private Func<IClipboard?>? _providerFactory;

    public IClipboard? Clipboard => _providerFactory?.Invoke();

    public void SetProvider(Func<IClipboard?> providerFactory)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
    }
}
