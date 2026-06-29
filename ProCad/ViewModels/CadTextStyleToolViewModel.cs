using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProCad.Services;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class CadTextStyleToolViewModel : CadToolViewModelBase
{
    private readonly ObservableCollection<CadTextStyleRowViewModel> _rows = new();
    private readonly Dictionary<TextStyle, CadTextStyleRowViewModel> _rowMap = new(ReferenceEqualityComparer.Instance);
    private readonly CadSelectionService _selectionService;
    private readonly CadDocumentContextService _documentContext;
    private readonly CadStylePreviewService _previewService;
    private readonly CadEditorSessionHostService _sessionHost;
    private CancellationTokenSource? _previewCts;
    private const int PreviewSize = 48;
    private bool _suppressSelection;
    private bool _suppressEditorSync;
    private CadTextStyleEditorSnapshot? _baselineSnapshot;

    [Reactive]
    public partial string SearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string FilterText { get; set; } = string.Empty;

    [Reactive]
    public partial CadTextStyleRowViewModel? SelectedStyle { get; set; }

    [Reactive]
    public partial string EditorName { get; set; } = string.Empty;

    [Reactive]
    public partial string EditorFontFile { get; set; } = string.Empty;

    [Reactive]
    public partial string EditorBigFontFile { get; set; } = string.Empty;

    [Reactive]
    public partial string EditorHeight { get; set; } = string.Empty;

    [Reactive]
    public partial string EditorWidth { get; set; } = string.Empty;

    [Reactive]
    public partial string EditorLastHeight { get; set; } = string.Empty;

    [Reactive]
    public partial string EditorObliqueDegrees { get; set; } = string.Empty;

    [Reactive]
    public partial bool EditorIsShapeFile { get; set; }

    [Reactive]
    public partial bool EditorIsVerticalText { get; set; }

    [Reactive]
    public partial bool EditorMirrorBackward { get; set; }

    [Reactive]
    public partial bool EditorMirrorUpsideDown { get; set; }

    [Reactive]
    public partial bool EditorBold { get; set; }

    [Reactive]
    public partial bool EditorItalic { get; set; }

    [Reactive]
    public partial bool IsCurrentStyle { get; set; }

    [Reactive]
    public partial bool IsDirty { get; set; }

    [Reactive]
    public partial bool CanApplyChanges { get; set; }

    [Reactive]
    public partial bool CanDeleteStyle { get; set; }

    [Reactive]
    public partial bool CanDuplicateStyle { get; set; }

    [Reactive]
    public partial bool CanSetCurrentStyle { get; set; }

    [Reactive]
    public partial string ValidationMessage { get; set; } = string.Empty;

    [Reactive]
    public partial string StatusMessage { get; set; } = string.Empty;

    [Reactive]
    public partial string EditorPreviewSummary { get; set; } = string.Empty;

    public DataGridCollectionView StylesView { get; }
    public DataGridColumnDefinitionList ColumnDefinitions { get; }
    public SortingModel SortingModel { get; } = new();
    public FilteringModel FilteringModel { get; } = new();
    public SearchModel SearchModel { get; } = new();

    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearFilterCommand { get; }
    public ReactiveCommand<Unit, Unit> NewStyleCommand { get; }
    public ReactiveCommand<Unit, Unit> DuplicateStyleCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteStyleCommand { get; }
    public ReactiveCommand<Unit, Unit> SetCurrentStyleCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyStyleCommand { get; }
    public ReactiveCommand<Unit, Unit> RevertStyleCommand { get; }

    public CadTextStyleToolViewModel(
        CadSelectionService selectionService,
        CadDocumentContextService documentContext,
        CadStylePreviewService previewService,
        CadEditorSessionHostService sessionHost)
    {
        _selectionService = selectionService;
        _documentContext = documentContext;
        _previewService = previewService;
        _sessionHost = sessionHost;

        StylesView = new DataGridCollectionView(_rows);
        ColumnDefinitions = CadTextStyleColumnDefinitions.Create();

        SearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        SearchModel.HighlightCurrent = true;
        SearchModel.WrapNavigation = true;

        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => ApplySearch());

        this.WhenAnyValue(x => x.FilterText)
            .Subscribe(_ => ApplyFilter());

        this.WhenAnyValue(x => x.SelectedStyle)
            .Subscribe(OnSelectedStyleChanged);

        _selectionService.WhenAnyValue(x => x.SelectedObject)
            .Subscribe(UpdateSelectionFromService);

        _documentContext.WhenAnyValue(x => x.ActiveDocument)
            .Subscribe(LoadStyles);

        var editorChanges = Observable.Merge(
            this.WhenAnyValue(x => x.EditorName).Select(_ => Unit.Default),
            this.WhenAnyValue(x => x.EditorFontFile).Select(_ => Unit.Default),
            this.WhenAnyValue(x => x.EditorBigFontFile).Select(_ => Unit.Default),
            this.WhenAnyValue(x => x.EditorHeight).Select(_ => Unit.Default),
            this.WhenAnyValue(x => x.EditorWidth).Select(_ => Unit.Default),
            this.WhenAnyValue(x => x.EditorLastHeight).Select(_ => Unit.Default),
            this.WhenAnyValue(x => x.EditorObliqueDegrees).Select(_ => Unit.Default),
            this.WhenAnyValue(x => x.EditorIsShapeFile).Select(_ => Unit.Default),
            this.WhenAnyValue(x => x.EditorIsVerticalText).Select(_ => Unit.Default),
            this.WhenAnyValue(x => x.EditorMirrorBackward).Select(_ => Unit.Default),
            this.WhenAnyValue(x => x.EditorMirrorUpsideDown).Select(_ => Unit.Default),
            this.WhenAnyValue(x => x.EditorBold).Select(_ => Unit.Default),
            this.WhenAnyValue(x => x.EditorItalic).Select(_ => Unit.Default));
        editorChanges.Subscribe(_ => EvaluateEditorState());

        var canDuplicate = this.WhenAnyValue(x => x.CanDuplicateStyle);
        var canDelete = this.WhenAnyValue(x => x.CanDeleteStyle);
        var canSetCurrent = this.WhenAnyValue(x => x.CanSetCurrentStyle);
        var canApply = this.WhenAnyValue(x => x.CanApplyChanges);
        var canRevert = this.WhenAnyValue(x => x.IsDirty);

        ClearSearchCommand = ReactiveCommand.Create(() => { SearchText = string.Empty; });
        ClearFilterCommand = ReactiveCommand.Create(() => { FilterText = string.Empty; });
        NewStyleCommand = ReactiveCommand.Create(CreateStyle);
        DuplicateStyleCommand = ReactiveCommand.Create(DuplicateSelectedStyle, canDuplicate);
        DeleteStyleCommand = ReactiveCommand.Create(DeleteSelectedStyle, canDelete);
        SetCurrentStyleCommand = ReactiveCommand.Create(SetCurrentStyle, canSetCurrent);
        ApplyStyleCommand = ReactiveCommand.Create(ApplyStyleChanges, canApply);
        RevertStyleCommand = ReactiveCommand.Create(RevertStyleChanges, canRevert);
    }

    private void LoadStyles(CadDocumentViewModel? documentViewModel)
    {
        var preferredStyle = ResolveTextStyle(_selectionService.SelectedObject);

        _rows.Clear();
        _rowMap.Clear();
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();

        var document = documentViewModel?.Document;
        var styles = document?.TextStyles;
        if (styles is null)
        {
            StylesView.Refresh();
            ClearEditor();
            EvaluateEditorState();
            return;
        }

        var currentStyleName = document?.Header.CurrentTextStyleName;
        foreach (var style in styles)
        {
            if (style is null)
            {
                continue;
            }

            var row = new CadTextStyleRowViewModel(style);
            row.RefreshFromStyle(currentStyleName);
            _rows.Add(row);
            _rowMap[style] = row;
            QueuePreview(row);
        }

        StylesView.Refresh();
        ApplyFilter();
        ApplySearch();

        if (preferredStyle is not null && _rowMap.TryGetValue(preferredStyle, out var preferredRow))
        {
            SetSelectedStyle(preferredRow, publishSelection: false);
        }
        else
        {
            SetSelectedStyle(null, publishSelection: false);
        }

        EvaluateEditorState();
    }

    private void QueuePreview(CadTextStyleRowViewModel row)
    {
        if (_previewCts is null)
        {
            return;
        }

        var token = _previewCts.Token;
        _ = Task.Run(async () =>
        {
            var preview = await _previewService.GetTextStylePreviewAsync(row.Style, PreviewSize, token)
                .ConfigureAwait(false);

            if (preview is null || token.IsCancellationRequested)
            {
                return;
            }

            RxSchedulers.MainThreadScheduler.Schedule(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    row.Preview = preview;
                }
            });
        }, CancellationToken.None);
    }

    private void ApplySearch()
    {
        DataGridFilterHelper.ApplySearch(SearchModel, SearchText);
    }

    private void ApplyFilter()
    {
        DataGridFilterHelper.ApplyFilter(FilteringModel, ColumnDefinitions, FilterText);
    }

    private void OnSelectedStyleChanged(CadTextStyleRowViewModel? row)
    {
        if (_suppressSelection)
        {
            return;
        }

        LoadEditor(row?.Style);
        _selectionService.SelectedObject = row?.Style;
        EvaluateEditorState();
    }

    private void SetSelectedStyle(CadTextStyleRowViewModel? row, bool publishSelection)
    {
        _suppressSelection = true;
        SelectedStyle = row;
        _suppressSelection = false;

        LoadEditor(row?.Style);
        if (publishSelection)
        {
            _selectionService.SelectedObject = row?.Style;
        }
    }

    private void UpdateSelectionFromService(object? selected)
    {
        if (_suppressSelection)
        {
            return;
        }

        var style = ResolveTextStyle(selected);
        if (style is not null && _rowMap.TryGetValue(style, out var row))
        {
            SetSelectedStyle(row, publishSelection: false);
            return;
        }

        if (SelectedStyle is not null)
        {
            SetSelectedStyle(null, publishSelection: false);
        }
    }

    private void LoadEditor(TextStyle? style)
    {
        _suppressEditorSync = true;

        if (style is null)
        {
            _baselineSnapshot = null;
            ClearEditorCore();
            _suppressEditorSync = false;
            return;
        }

        EditorName = style.Name;
        EditorFontFile = style.Filename ?? string.Empty;
        EditorBigFontFile = style.BigFontFilename ?? string.Empty;
        EditorHeight = style.Height.ToString("0.###", CultureInfo.InvariantCulture);
        EditorWidth = style.Width.ToString("0.###", CultureInfo.InvariantCulture);
        EditorLastHeight = style.LastHeight.ToString("0.###", CultureInfo.InvariantCulture);
        EditorObliqueDegrees = (style.ObliqueAngle * 180.0 / Math.PI).ToString("0.###", CultureInfo.InvariantCulture);
        EditorIsShapeFile = style.Flags.HasFlag(StyleFlags.IsShape);
        EditorIsVerticalText = style.Flags.HasFlag(StyleFlags.VerticalText);
        EditorMirrorBackward = style.MirrorFlag.HasFlag(TextMirrorFlag.Backward);
        EditorMirrorUpsideDown = style.MirrorFlag.HasFlag(TextMirrorFlag.UpsideDown);
        EditorBold = style.TrueType.HasFlag(FontFlags.Bold);
        EditorItalic = style.TrueType.HasFlag(FontFlags.Italic);

        _baselineSnapshot = CadTextStyleEditorSnapshot.FromStyle(style);
        ValidationMessage = string.Empty;
        EditorPreviewSummary = BuildPreviewSummary(_baselineSnapshot.Value, previewUnavailableReason: null);

        var currentStyleName = _documentContext.ActiveDocument?.Document?.Header.CurrentTextStyleName;
        IsCurrentStyle = !string.IsNullOrWhiteSpace(currentStyleName) &&
                         string.Equals(currentStyleName, style.Name, StringComparison.OrdinalIgnoreCase);

        _suppressEditorSync = false;
    }

    private void ClearEditor()
    {
        _suppressEditorSync = true;
        _baselineSnapshot = null;
        ClearEditorCore();
        _suppressEditorSync = false;
    }

    private void ClearEditorCore()
    {
        EditorName = string.Empty;
        EditorFontFile = string.Empty;
        EditorBigFontFile = string.Empty;
        EditorHeight = string.Empty;
        EditorWidth = string.Empty;
        EditorLastHeight = string.Empty;
        EditorObliqueDegrees = string.Empty;
        EditorIsShapeFile = false;
        EditorIsVerticalText = false;
        EditorMirrorBackward = false;
        EditorMirrorUpsideDown = false;
        EditorBold = false;
        EditorItalic = false;
        IsCurrentStyle = false;
        IsDirty = false;
        CanApplyChanges = false;
        CanDeleteStyle = false;
        CanDuplicateStyle = false;
        CanSetCurrentStyle = false;
        ValidationMessage = string.Empty;
        StatusMessage = string.Empty;
        EditorPreviewSummary = string.Empty;
    }

    private void EvaluateEditorState()
    {
        if (_suppressEditorSync)
        {
            return;
        }

        var selected = SelectedStyle?.Style;
        var document = _documentContext.ActiveDocument?.Document;
        CanDuplicateStyle = selected is not null;
        CanDeleteStyle = selected is not null &&
                         document is not null &&
                         document.TextStyles.Count > 1 &&
                         !string.Equals(selected.Name, TextStyle.DefaultName, StringComparison.OrdinalIgnoreCase);

        var currentStyleName = document?.Header.CurrentTextStyleName;
        IsCurrentStyle = selected is not null &&
                         !string.IsNullOrWhiteSpace(currentStyleName) &&
                         string.Equals(currentStyleName, selected.Name, StringComparison.OrdinalIgnoreCase);
        CanSetCurrentStyle = selected is not null && !IsCurrentStyle;

        if (selected is null || _baselineSnapshot is null)
        {
            IsDirty = false;
            CanApplyChanges = false;
            ValidationMessage = string.Empty;
            EditorPreviewSummary = string.Empty;
            return;
        }

        if (!TryBuildSnapshotFromEditor(out var editorSnapshot, out var error))
        {
            ValidationMessage = error;
            IsDirty = true;
            CanApplyChanges = false;
            EditorPreviewSummary = BuildPreviewSummary(_baselineSnapshot.Value, error);
            return;
        }

        ValidationMessage = string.Empty;
        IsDirty = !_baselineSnapshot.Value.Equals(editorSnapshot);
        CanApplyChanges = IsDirty;
        EditorPreviewSummary = BuildPreviewSummary(editorSnapshot, previewUnavailableReason: null);
    }

    private bool TryBuildSnapshotFromEditor(out CadTextStyleEditorSnapshot snapshot, out string error)
    {
        snapshot = default;
        error = string.Empty;

        var selected = SelectedStyle?.Style;
        var document = _documentContext.ActiveDocument?.Document;
        if (selected is null || document is null)
        {
            error = "No active text style selected.";
            return false;
        }

        var name = EditorName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Style name is required.";
            return false;
        }

        if (!string.Equals(name, selected.Name, StringComparison.OrdinalIgnoreCase) &&
            document.TextStyles.Contains(name))
        {
            error = $"A text style named '{name}' already exists.";
            return false;
        }

        if (!TryParseDouble(EditorHeight, out var height) || height < 0.0)
        {
            error = "Height must be a number greater than or equal to 0.";
            return false;
        }

        if (!TryParseDouble(EditorWidth, out var width) || width <= 0.0)
        {
            error = "Width factor must be a number greater than 0.";
            return false;
        }

        if (!TryParseDouble(EditorLastHeight, out var lastHeight) || lastHeight < 0.0)
        {
            error = "Last height must be a number greater than or equal to 0.";
            return false;
        }

        if (!TryParseDouble(EditorObliqueDegrees, out var obliqueDegrees) ||
            obliqueDegrees < -85.0 ||
            obliqueDegrees > 85.0)
        {
            error = "Oblique angle must be between -85 and 85 degrees.";
            return false;
        }

        var fontFile = EditorFontFile.Trim();
        if (EditorIsShapeFile && string.IsNullOrWhiteSpace(fontFile))
        {
            error = "Shape text styles require a font file.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(fontFile) &&
            Path.HasExtension(fontFile) &&
            !HasAcceptedFontExtension(fontFile))
        {
            error = "Font file must use .shx, .ttf, or .otf extension.";
            return false;
        }

        var bigFontFile = EditorBigFontFile.Trim();
        if (!string.IsNullOrWhiteSpace(bigFontFile) &&
            Path.HasExtension(bigFontFile) &&
            !bigFontFile.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))
        {
            error = "Big font file must use .shx extension.";
            return false;
        }

        snapshot = new CadTextStyleEditorSnapshot(
            Name: name,
            FontFile: fontFile,
            BigFontFile: bigFontFile,
            Height: height,
            Width: width,
            LastHeight: lastHeight,
            ObliqueDegrees: obliqueDegrees,
            IsShapeFile: EditorIsShapeFile,
            IsVerticalText: EditorIsVerticalText,
            MirrorBackward: EditorMirrorBackward,
            MirrorUpsideDown: EditorMirrorUpsideDown,
            Bold: EditorBold,
            Italic: EditorItalic);
        return true;
    }

    private void CreateStyle()
    {
        var document = _documentContext.ActiveDocument?.Document;
        if (document is null)
        {
            return;
        }

        var styleName = CreateUniqueName(document, "Style");
        var style = new TextStyle(styleName)
        {
            Filename = string.Empty,
            BigFontFilename = string.Empty,
            Height = 0.0,
            Width = 1.0,
            LastHeight = 1.0,
            ObliqueAngle = 0.0,
            MirrorFlag = TextMirrorFlag.None,
            TrueType = FontFlags.Regular,
            Flags = StyleFlags.None
        };

        document.TextStyles.Add(style);
        ReloadStyles(style, notifyDocumentChange: true, publishSelection: true);
        StatusMessage = $"Created text style '{style.Name}'.";
    }

    private void DuplicateSelectedStyle()
    {
        var source = SelectedStyle?.Style;
        var document = _documentContext.ActiveDocument?.Document;
        if (source is null || document is null)
        {
            return;
        }

        var copy = new TextStyle(CreateUniqueName(document, $"{source.Name}_Copy"))
        {
            Filename = source.Filename ?? string.Empty,
            BigFontFilename = source.BigFontFilename ?? string.Empty,
            Height = source.Height,
            Width = source.Width,
            LastHeight = source.LastHeight,
            ObliqueAngle = source.ObliqueAngle,
            MirrorFlag = source.MirrorFlag,
            TrueType = source.TrueType,
            Flags = source.Flags
        };

        document.TextStyles.Add(copy);
        _previewService.InvalidateTextStyle(copy);
        ReloadStyles(copy, notifyDocumentChange: true, publishSelection: true);
        StatusMessage = $"Duplicated '{source.Name}' to '{copy.Name}'.";
    }

    private void DeleteSelectedStyle()
    {
        var selected = SelectedStyle?.Style;
        var document = _documentContext.ActiveDocument?.Document;
        if (selected is null || document is null || !CanDeleteStyle)
        {
            return;
        }

        var removed = document.TextStyles.Remove(selected.Name);
        if (removed is null)
        {
            StatusMessage = $"Could not delete text style '{selected.Name}'.";
            return;
        }

        _previewService.InvalidateTextStyle(selected);

        var deletedCurrent = string.Equals(
            document.Header.CurrentTextStyleName,
            selected.Name,
            StringComparison.OrdinalIgnoreCase);
        if (deletedCurrent)
        {
            if (document.TextStyles.TryGetValue(TextStyle.DefaultName, out var standard))
            {
                document.Header.CurrentTextStyleName = standard.Name;
            }
            else
            {
                var first = document.TextStyles.FirstOrDefault();
                if (first is not null)
                {
                    document.Header.CurrentTextStyleName = first.Name;
                }
            }
        }

        TextStyle? preferred = null;
        var currentName = document.Header.CurrentTextStyleName;
        if (!string.IsNullOrWhiteSpace(currentName) && document.TextStyles.TryGetValue(currentName, out var current))
        {
            preferred = current;
        }
        else
        {
            preferred = document.TextStyles.FirstOrDefault();
        }

        ReloadStyles(preferred, notifyDocumentChange: true, publishSelection: true);
        StatusMessage = $"Deleted text style '{selected.Name}'.";
    }

    private void SetCurrentStyle()
    {
        var selected = SelectedStyle?.Style;
        var document = _documentContext.ActiveDocument?.Document;
        if (selected is null || document is null)
        {
            return;
        }

        document.Header.CurrentTextStyleName = selected.Name;
        ReloadStyles(selected, notifyDocumentChange: true, publishSelection: true);
        StatusMessage = $"Current text style set to '{selected.Name}'.";
    }

    private void ApplyStyleChanges()
    {
        var selected = SelectedStyle?.Style;
        var document = _documentContext.ActiveDocument?.Document;
        if (selected is null || document is null)
        {
            return;
        }

        if (!TryBuildSnapshotFromEditor(out var snapshot, out var error))
        {
            ValidationMessage = error;
            return;
        }

        try
        {
            var previousName = selected.Name;
            if (!string.Equals(selected.Name, snapshot.Name, StringComparison.Ordinal))
            {
                selected.Name = snapshot.Name;
                if (string.Equals(
                        document.Header.CurrentTextStyleName,
                        previousName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    document.Header.CurrentTextStyleName = snapshot.Name;
                }
            }

            selected.Filename = snapshot.FontFile;
            selected.BigFontFilename = snapshot.BigFontFile;
            selected.Height = snapshot.Height;
            selected.Width = snapshot.Width;
            selected.LastHeight = snapshot.LastHeight;
            selected.ObliqueAngle = snapshot.ObliqueDegrees * Math.PI / 180.0;
            selected.Flags = ApplyFlag(selected.Flags, StyleFlags.IsShape, snapshot.IsShapeFile);
            selected.Flags = ApplyFlag(selected.Flags, StyleFlags.VerticalText, snapshot.IsVerticalText);
            selected.MirrorFlag = ApplyFlag(selected.MirrorFlag, TextMirrorFlag.Backward, snapshot.MirrorBackward);
            selected.MirrorFlag = ApplyFlag(selected.MirrorFlag, TextMirrorFlag.UpsideDown, snapshot.MirrorUpsideDown);

            var trueType = FontFlags.Regular;
            trueType = ApplyFlag(trueType, FontFlags.Bold, snapshot.Bold);
            trueType = ApplyFlag(trueType, FontFlags.Italic, snapshot.Italic);
            selected.TrueType = trueType;
        }
        catch (Exception ex)
        {
            ValidationMessage = ex.Message;
            return;
        }

        _previewService.InvalidateTextStyle(selected);
        ReloadStyles(selected, notifyDocumentChange: true, publishSelection: true);
        StatusMessage = $"Updated text style '{selected.Name}'.";
    }

    private void RevertStyleChanges()
    {
        var selected = SelectedStyle?.Style;
        if (selected is null)
        {
            return;
        }

        LoadEditor(selected);
        EvaluateEditorState();
        StatusMessage = $"Reverted pending changes for '{selected.Name}'.";
    }

    private void ReloadStyles(TextStyle? preferredStyle, bool notifyDocumentChange, bool publishSelection)
    {
        var active = _documentContext.ActiveDocument;
        LoadStyles(active);

        if (preferredStyle is not null && _rowMap.TryGetValue(preferredStyle, out var row))
        {
            SetSelectedStyle(row, publishSelection);
            _previewService.InvalidateTextStyle(preferredStyle);
            QueuePreview(row);
        }

        if (notifyDocumentChange)
        {
            NotifyDocumentChanged();
        }
    }

    private void NotifyDocumentChanged()
    {
        var document = _documentContext.ActiveDocument?.Document;
        if (document is null)
        {
            return;
        }

        var session = _sessionHost.GetOrCreate(document);
        _sessionHost.NotifySessionChanged(session);
    }

    private static string CreateUniqueName(CadDocument document, string baseName)
    {
        var name = baseName;
        var counter = 1;
        while (document.TextStyles.Contains(name))
        {
            name = $"{baseName}{counter}";
            counter++;
        }

        return name;
    }

    private static bool TryParseDouble(string text, out double value)
    {
        var trimmed = text.Trim();
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool HasAcceptedFontExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".shx", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".ttf", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".otf", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPreviewSummary(
        CadTextStyleEditorSnapshot snapshot,
        string? previewUnavailableReason)
    {
        if (!string.IsNullOrWhiteSpace(previewUnavailableReason))
        {
            return $"Preview unavailable: {previewUnavailableReason}";
        }

        var flags = new List<string>(4);
        if (snapshot.IsShapeFile)
        {
            flags.Add("Shape");
        }

        if (snapshot.IsVerticalText)
        {
            flags.Add("Vertical");
        }

        if (snapshot.Bold)
        {
            flags.Add("Bold");
        }

        if (snapshot.Italic)
        {
            flags.Add("Italic");
        }

        var flagText = flags.Count == 0 ? "Regular" : string.Join(", ", flags);
        var font = string.IsNullOrWhiteSpace(snapshot.FontFile) ? "(default font)" : snapshot.FontFile;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Preview {snapshot.Name}: {font}; H={snapshot.Height:0.###}, W={snapshot.Width:0.###}, Oblique={snapshot.ObliqueDegrees:0.###}\u00B0; {flagText}");
    }

    private static StyleFlags ApplyFlag(StyleFlags value, StyleFlags flag, bool enabled)
    {
        return enabled ? value | flag : value & ~flag;
    }

    private static TextMirrorFlag ApplyFlag(TextMirrorFlag value, TextMirrorFlag flag, bool enabled)
    {
        return enabled ? value | flag : value & ~flag;
    }

    private static FontFlags ApplyFlag(FontFlags value, FontFlags flag, bool enabled)
    {
        return enabled ? value | flag : value & ~flag;
    }

    private static TextStyle? ResolveTextStyle(object? selected)
    {
        switch (selected)
        {
            case CadDocumentTreeNode node:
                return ResolveTextStyle(node.Source);
            case TextStyle style:
                return style;
            case IText text:
                return text.Style;
            case Dimension dimension:
                return dimension.Style?.Style;
        }

        return null;
    }

    private readonly record struct CadTextStyleEditorSnapshot(
        string Name,
        string FontFile,
        string BigFontFile,
        double Height,
        double Width,
        double LastHeight,
        double ObliqueDegrees,
        bool IsShapeFile,
        bool IsVerticalText,
        bool MirrorBackward,
        bool MirrorUpsideDown,
        bool Bold,
        bool Italic)
    {
        public static CadTextStyleEditorSnapshot FromStyle(TextStyle style)
        {
            return new CadTextStyleEditorSnapshot(
                Name: style.Name,
                FontFile: style.Filename?.Trim() ?? string.Empty,
                BigFontFile: style.BigFontFilename?.Trim() ?? string.Empty,
                Height: style.Height,
                Width: style.Width,
                LastHeight: style.LastHeight,
                ObliqueDegrees: style.ObliqueAngle * 180.0 / Math.PI,
                IsShapeFile: style.Flags.HasFlag(StyleFlags.IsShape),
                IsVerticalText: style.Flags.HasFlag(StyleFlags.VerticalText),
                MirrorBackward: style.MirrorFlag.HasFlag(TextMirrorFlag.Backward),
                MirrorUpsideDown: style.MirrorFlag.HasFlag(TextMirrorFlag.UpsideDown),
                Bold: style.TrueType.HasFlag(FontFlags.Bold),
                Italic: style.TrueType.HasFlag(FontFlags.Italic));
        }
    }
}
