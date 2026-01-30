using System;
using Avalonia.Platform.Storage;

namespace ACadInspector.Services;

public interface IStorageProviderAccessor
{
    IStorageProvider? StorageProvider { get; }
    void SetProvider(Func<IStorageProvider?> providerFactory);
}
