using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProCad.Core;
using Avalonia.Platform.Storage;

namespace ProCad.Services;

public sealed class AvaloniaRenderStatsExportService : IRenderStatsExportService
{
    private static readonly IReadOnlyList<FilePickerFileType> FileTypes = CreateFileTypes();
    private readonly IStorageProviderAccessor _storageProviderAccessor;

    public AvaloniaRenderStatsExportService(IStorageProviderAccessor storageProviderAccessor)
    {
        _storageProviderAccessor = storageProviderAccessor;
    }

    public async Task<RenderStatsExportResult?> SaveStatsAsync(string? suggestedFileName, CancellationToken cancellationToken)
    {
        var storageProvider = _storageProviderAccessor.StorageProvider;
        if (storageProvider is null)
        {
            return null;
        }

        var options = new FilePickerSaveOptions
        {
            FileTypeChoices = FileTypes,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "json",
            ShowOverwritePrompt = true
        };

        var result = await storageProvider.SaveFilePickerWithResultAsync(options).ConfigureAwait(true);
        var file = result.File;
        if (file is null)
        {
            return null;
        }

        return new RenderStatsExportResult(
            file.TryGetLocalPath(),
            file.Name,
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
            new FilePickerFileType("Render Stats (JSON)") { Patterns = new[] { "*.json" } },
            FilePickerFileTypes.All
        };
    }
}
