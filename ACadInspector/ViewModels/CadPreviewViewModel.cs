using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ACadInspector.Core;
using ACadInspector.Diagnostics;
using ACadInspector.Services;
using ACadSharp;
using ACadSharp.IO;
using Avalonia.Media.Imaging;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadPreviewViewModel : CadToolViewModelBase
{
    private readonly CadSelectionService _selectionService;
    private readonly CadDocumentContextService _documentContext;
    private readonly IAppNotificationService _notificationService;
    private readonly HashSet<string> _reportedPreviewFailures = new(StringComparer.OrdinalIgnoreCase);
    private CadDocument? _previewDocument;
    private string? _previewPath;

    [Reactive]
    public partial string PreviewStatus { get; set; } = "Preview not available";

    [Reactive]
    public partial Bitmap? PreviewImage { get; set; }

    public CadPreviewViewModel(
        CadSelectionService selectionService,
        CadDocumentContextService documentContext,
        IAppNotificationService notificationService)
    {
        _selectionService = selectionService;
        _documentContext = documentContext;
        _notificationService = notificationService;

        _selectionService.WhenAnyValue(x => x.SelectedObject)
            .Subscribe(UpdatePreviewFromSelection);

        _documentContext.WhenAnyValue(x => x.ActiveDocument)
            .Subscribe(_ => UpdatePreviewFromSelection(_selectionService.SelectedObject));
    }

    private void UpdatePreviewFromSelection(object? selected)
    {
        try
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
        catch (Exception ex)
        {
            AppLog.Error("Preview update failed.", exception: ex);
            ClearPreview("Preview not available");
            _notificationService.ShowWarning("Preview Unavailable", ex.Message, TimeSpan.FromSeconds(8));
        }
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

        if (!CanReadDwgPreviewInCurrentBuild())
        {
            ClearPreview("Preview not available in Debug builds.");
            return;
        }

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
            NotifyPreviewFailure(viewModel.Path, ex);
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

    private static bool CanReadDwgPreviewInCurrentBuild()
    {
#if DEBUG
        // ACadSharp preview parser uses Debug.Assert on sentinel checks and can terminate the process in Debug builds.
        return false;
#else
        return true;
#endif
    }

    private void NotifyPreviewFailure(string path, Exception ex)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _notificationService.ShowWarning("Preview Unavailable", ex.Message, TimeSpan.FromSeconds(8));
            return;
        }

        if (!_reportedPreviewFailures.Add(path))
        {
            return;
        }

        var fileName = Path.GetFileName(path);
        _notificationService.ShowWarning(
            "Preview Unavailable",
            $"{fileName}: {ex.Message}",
            TimeSpan.FromSeconds(8));
    }
}
