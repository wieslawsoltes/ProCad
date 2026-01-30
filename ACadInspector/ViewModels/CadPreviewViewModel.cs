using System;
using System.Globalization;
using System.IO;
using ACadInspector.Core;
using ACadInspector.Services;
using ACadSharp;
using ACadSharp.IO;
using Avalonia.Media.Imaging;
using Dock.Model.ReactiveUI.Controls;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadPreviewViewModel : Tool
{
    private readonly CadSelectionService _selectionService;
    private readonly CadDocumentContextService _documentContext;
    private CadDocument? _previewDocument;
    private string? _previewPath;

    [Reactive]
    public partial string PreviewStatus { get; set; } = "Preview not available";

    [Reactive]
    public partial Bitmap? PreviewImage { get; set; }

    public CadPreviewViewModel(
        CadSelectionService selectionService,
        CadDocumentContextService documentContext)
    {
        _selectionService = selectionService;
        _documentContext = documentContext;

        _selectionService.WhenAnyValue(x => x.SelectedObject)
            .Subscribe(UpdatePreviewFromSelection);

        _documentContext.WhenAnyValue(x => x.ActiveDocument)
            .Subscribe(_ => UpdatePreviewFromSelection(_selectionService.SelectedObject));
    }

    private void UpdatePreviewFromSelection(object? selected)
    {
        var viewModel = _documentContext.ResolveViewModel(selected);
        var document = _documentContext.ResolveDocument(selected) ?? viewModel?.Document;

        if (document is null)
        {
            _previewDocument = null;
            _previewPath = null;
            ClearPreview("Preview not available");
            return;
        }

        _documentContext.TrySetActiveFromSelection(selected);
        UpdatePreview(document, viewModel);
    }

    private void UpdatePreview(CadDocument document, CadDocumentViewModel? viewModel)
    {
        if (viewModel is null || viewModel.Format != CadFileFormat.Dwg || string.IsNullOrWhiteSpace(viewModel.Path))
        {
            _previewDocument = document;
            _previewPath = viewModel?.Path;
            ClearPreview("Preview not available");
            return;
        }

        if (ReferenceEquals(document, _previewDocument) &&
            string.Equals(viewModel.Path, _previewPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _previewDocument = document;
        _previewPath = viewModel.Path;

        try
        {
            var preview = ReadPreview(viewModel.Path);
            if (preview is null || preview.RawImage.Length == 0)
            {
                ClearPreview("Preview not available");
                return;
            }

            var sizeText = preview.RawImage.Length.ToString("N0", CultureInfo.InvariantCulture);
            PreviewStatus = $"Preview: {preview.Code} ({sizeText} bytes)";

            if (preview.Code is DwgPreview.PreviewType.Png or DwgPreview.PreviewType.Bmp)
            {
                using var stream = new MemoryStream(preview.RawImage, writable: false);
                var bitmap = new Bitmap(stream);
                SetPreviewImage(bitmap);
            }
            else
            {
                SetPreviewImage(null);
                PreviewStatus = $"Preview format not supported: {preview.Code}.";
            }
        }
        catch (Exception ex)
        {
            ClearPreview($"Preview error: {ex.Message}");
        }
    }

    private static DwgPreview? ReadPreview(string path)
    {
        using var reader = new DwgReader(path);
        return reader.ReadPreview();
    }

    private void ClearPreview(string status)
    {
        SetPreviewImage(null);
        PreviewStatus = status;
    }

    private void SetPreviewImage(Bitmap? image)
    {
        if (ReferenceEquals(PreviewImage, image))
        {
            return;
        }

        PreviewImage?.Dispose();
        PreviewImage = image;
    }
}
