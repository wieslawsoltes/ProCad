using System;
using Avalonia.Input.Platform;

namespace ProCad.Services;

public interface IClipboardAccessor
{
    IClipboard? Clipboard { get; }
    void SetProvider(Func<IClipboard?> providerFactory);
}
