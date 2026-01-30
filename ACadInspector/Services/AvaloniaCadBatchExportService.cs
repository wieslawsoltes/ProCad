using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Core;
using Avalonia.Platform.Storage;

namespace ACadInspector.Services;

public sealed class AvaloniaCadBatchExportService : ICadBatchExportService
{
    private readonly IStorageProviderAccessor _storageProviderAccessor;

    public AvaloniaCadBatchExportService(IStorageProviderAccessor storageProviderAccessor)
    {
        _storageProviderAccessor = storageProviderAccessor;
    }

    public async Task<CadBatchExportResult?> SaveExportAsync(
        CadBatchExportFormat format,
        string? suggestedFileName,
        CancellationToken cancellationToken)
    {
        var storageProvider = _storageProviderAccessor.StorageProvider;
        if (storageProvider is null)
        {
            return null;
        }

        var options = new FilePickerSaveOptions
        {
            FileTypeChoices = CreateFileTypes(),
            SuggestedFileName = suggestedFileName,
            DefaultExtension = format == CadBatchExportFormat.Csv ? "csv" : "json",
            ShowOverwritePrompt = true
        };

        var result = await storageProvider.SaveFilePickerWithResultAsync(options).ConfigureAwait(true);
        var file = result.File;
        if (file is null)
        {
            return null;
        }

        return new CadBatchExportResult(
            file.TryGetLocalPath(),
            file.Name,
            format,
            async ct =>
            {
                ct.ThrowIfCancellationRequested();
                return await file.OpenWriteAsync().ConfigureAwait(false);
            });
    }

    private static IReadOnlyList<FilePickerFileType> CreateFileTypes()
    {
        return new[]
        {
            new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } },
            new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } },
            FilePickerFileTypes.All
        };
    }
}
