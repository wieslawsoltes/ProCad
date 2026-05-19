using System;
using Avalonia.Platform.Storage;

namespace ProCad.Services;

public interface IStorageProviderAccessor
{
    IStorageProvider? StorageProvider { get; }
    void SetProvider(Func<IStorageProvider?> providerFactory);
}
