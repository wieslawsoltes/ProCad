using System;
using Avalonia.Platform.Storage;

namespace ProCad.Services;

public sealed class StorageProviderAccessor : IStorageProviderAccessor
{
    private Func<IStorageProvider?>? _providerFactory;

    public IStorageProvider? StorageProvider => _providerFactory?.Invoke();

    public void SetProvider(Func<IStorageProvider?> providerFactory)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
    }
}
