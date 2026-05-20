using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProCad.Core;
using Avalonia.Platform.Storage;

namespace ProCad.Services;

public sealed class AvaloniaCadFileDialogService : ICadFileDialogService
{
    private static readonly IReadOnlyList<FilePickerFileType> CadFileTypes = CreateCadFileTypes();
    private readonly IStorageProviderAccessor _storageProviderAccessor;

    public AvaloniaCadFileDialogService(IStorageProviderAccessor storageProviderAccessor)
    {
        _storageProviderAccessor = storageProviderAccessor;
    }

    public async Task<CadOpenFileResult?> OpenCadFileAsync(CadFileFormat? preferredFormat, CancellationToken cancellationToken)
    {
        var results = await OpenCadFilesAsync(preferredFormat, allowMultiple: false, cancellationToken).ConfigureAwait(true);
        if (results.Count == 0)
        {
            return null;
        }

        return results[0];
    }

    public Task<IReadOnlyList<CadOpenFileResult>> OpenCadFilesAsync(CadFileFormat? preferredFormat, CancellationToken cancellationToken)
    {
        return OpenCadFilesAsync(preferredFormat, allowMultiple: true, cancellationToken);
    }

    private async Task<IReadOnlyList<CadOpenFileResult>> OpenCadFilesAsync(
        CadFileFormat? preferredFormat,
        bool allowMultiple,
        CancellationToken cancellationToken)
    {
        var storageProvider = _storageProviderAccessor.StorageProvider;
        if (storageProvider is null)
        {
            return Array.Empty<CadOpenFileResult>();
        }

        var options = new FilePickerOpenOptions
        {
            AllowMultiple = allowMultiple,
            FileTypeFilter = CadFileTypes,
            SuggestedFileType = FindSuggestedFileType(preferredFormat)
        };

        cancellationToken.ThrowIfCancellationRequested();
        var files = await storageProvider.OpenFilePickerAsync(options).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (files.Count == 0)
        {
            return Array.Empty<CadOpenFileResult>();
        }

        var results = new List<CadOpenFileResult>(files.Count);
        foreach (var file in files)
        {
            var format = ResolveFormat(file.Name, preferredFormat, options.SuggestedFileType);
            results.Add(CreateOpenResult(file, format));
        }

        return results;
    }

    public async Task<CadSaveFileResult?> SaveCadFileAsync(CadFileFormat format, string? suggestedFileName, CancellationToken cancellationToken)
    {
        var storageProvider = _storageProviderAccessor.StorageProvider;
        if (storageProvider is null)
        {
            return null;
        }

        var options = new FilePickerSaveOptions
        {
            FileTypeChoices = CadFileTypes,
            SuggestedFileType = FindSuggestedFileType(format),
            SuggestedFileName = suggestedFileName,
            DefaultExtension = format == CadFileFormat.Dxf ? "dxf" : "dwg",
            ShowOverwritePrompt = true
        };

        var result = await storageProvider.SaveFilePickerWithResultAsync(options).ConfigureAwait(true);
        var file = result.File;
        if (file is null)
        {
            return null;
        }

        var resolvedFormat = ResolveFormat(file.Name, format, result.SelectedFileType);

        return new CadSaveFileResult(
            file.TryGetLocalPath(),
            file.Name,
            resolvedFormat,
            async ct =>
            {
                ct.ThrowIfCancellationRequested();
                return await file.OpenWriteAsync().ConfigureAwait(false);
            });
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

    private static FilePickerFileType? FindSuggestedFileType(CadFileFormat? format)
    {
        if (!format.HasValue)
        {
            return CadFileTypes.Count > 0 ? CadFileTypes[0] : null;
        }

        return FindSuggestedFileType(format.Value);
    }

    private static FilePickerFileType? FindSuggestedFileType(CadFileFormat format)
    {
        var pattern = format == CadFileFormat.Dxf ? "*.dxf" : "*.dwg";
        foreach (var fileType in CadFileTypes)
        {
            if (fileType.Patterns is null)
            {
                continue;
            }

            foreach (var entry in fileType.Patterns)
            {
                if (string.Equals(entry, pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return fileType;
                }
            }
        }

        return CadFileTypes.Count > 0 ? CadFileTypes[0] : null;
    }

    private static CadFileFormat ResolveFormat(string fileName, CadFileFormat? fallback, FilePickerFileType? selectedType)
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

        if (selectedType?.Patterns is not null)
        {
            foreach (var pattern in selectedType.Patterns)
            {
                if (string.Equals(pattern, "*.dxf", StringComparison.OrdinalIgnoreCase))
                {
                    return CadFileFormat.Dxf;
                }

                if (string.Equals(pattern, "*.dwg", StringComparison.OrdinalIgnoreCase))
                {
                    return CadFileFormat.Dwg;
                }
            }
        }

        return fallback ?? CadFileFormat.Dxf;
    }

    private static CadOpenFileResult CreateOpenResult(IStorageFile file, CadFileFormat format)
    {
        return new CadOpenFileResult(
            file.TryGetLocalPath(),
            file.Name,
            format,
            async ct =>
            {
                ct.ThrowIfCancellationRequested();
                return await file.OpenReadAsync().ConfigureAwait(false);
            });
    }
}
