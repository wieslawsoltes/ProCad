using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProCad.Core;
using Avalonia.Platform.Storage;

namespace ProCad.Services;

public sealed class AvaloniaCadBatchFileDialogService : ICadBatchFileDialogService
{
    private static readonly IReadOnlyList<FilePickerFileType> CadFileTypes = CreateCadFileTypes();
    private readonly IStorageProviderAccessor _storageProviderAccessor;

    public AvaloniaCadBatchFileDialogService(IStorageProviderAccessor storageProviderAccessor)
    {
        _storageProviderAccessor = storageProviderAccessor;
    }

    public async Task<IReadOnlyList<CadOpenFileResult>> OpenCadFilesAsync(CancellationToken cancellationToken)
    {
        var storageProvider = _storageProviderAccessor.StorageProvider;
        if (storageProvider is null)
        {
            return Array.Empty<CadOpenFileResult>();
        }

        var options = new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter = CadFileTypes,
            SuggestedFileType = CadFileTypes.Count > 0 ? CadFileTypes[0] : null
        };

        var files = await storageProvider.OpenFilePickerAsync(options).ConfigureAwait(true);
        if (files.Count == 0)
        {
            return Array.Empty<CadOpenFileResult>();
        }

        return BuildResults(files);
    }

    public async Task<IReadOnlyList<CadOpenFileResult>> OpenCadFolderAsync(CancellationToken cancellationToken)
    {
        var storageProvider = _storageProviderAccessor.StorageProvider;
        if (storageProvider is null)
        {
            return Array.Empty<CadOpenFileResult>();
        }

        var options = new FolderPickerOpenOptions
        {
            AllowMultiple = false
        };

        var folders = await storageProvider.OpenFolderPickerAsync(options).ConfigureAwait(true);
        if (folders.Count == 0)
        {
            return Array.Empty<CadOpenFileResult>();
        }

        var folder = folders[0];
        var results = new List<CadOpenFileResult>();
        await foreach (var item in folder.GetItemsAsync().WithCancellation(cancellationToken))
        {
            if (item is not IStorageFile file)
            {
                continue;
            }

            var format = ResolveFormat(file.Name);
            if (!format.HasValue)
            {
                continue;
            }

            results.Add(new CadOpenFileResult(
                file.TryGetLocalPath(),
                file.Name,
                format.Value,
                async ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    return await file.OpenReadAsync().ConfigureAwait(false);
                }));
        }

        return results;
    }

    private static IReadOnlyList<CadOpenFileResult> BuildResults(IReadOnlyList<IStorageFile> files)
    {
        var results = new List<CadOpenFileResult>(files.Count);
        foreach (var file in files)
        {
            var format = ResolveFormat(file.Name);
            if (!format.HasValue)
            {
                continue;
            }

            results.Add(new CadOpenFileResult(
                file.TryGetLocalPath(),
                file.Name,
                format.Value,
                async ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    return await file.OpenReadAsync().ConfigureAwait(false);
                }));
        }

        return results;
    }

    private static CadFileFormat? ResolveFormat(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.Equals(extension, ".dxf", StringComparison.OrdinalIgnoreCase))
        {
            return CadFileFormat.Dxf;
        }

        if (string.Equals(extension, ".dwg", StringComparison.OrdinalIgnoreCase))
        {
            return CadFileFormat.Dwg;
        }

        return null;
    }

    private static IReadOnlyList<FilePickerFileType> CreateCadFileTypes()
    {
        return new[]
        {
            new FilePickerFileType("CAD Files")
            {
                Patterns = new[] { "*.dxf", "*.dwg" }
            },
            new FilePickerFileType("DXF")
            {
                Patterns = new[] { "*.dxf" }
            },
            new FilePickerFileType("DWG")
            {
                Patterns = new[] { "*.dwg" }
            },
            FilePickerFileTypes.All
        };
    }
}
