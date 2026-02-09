using System;
using Avalonia.Input.Platform;

namespace ACadInspector.Services;

public interface IClipboardAccessor
{
    IClipboard? Clipboard { get; }
    void SetProvider(Func<IClipboard?> providerFactory);
}
