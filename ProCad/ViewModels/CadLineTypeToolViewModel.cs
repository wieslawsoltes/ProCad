using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
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
using CSMath;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class CadLineTypeToolViewModel : CadToolViewModelBase
{
    private readonly ObservableCollection<CadLineTypeRowViewModel> _rows = new();
    private readonly Dictionary<LineType, CadLineTypeRowViewModel> _rowMap = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CadLineTypeSegmentEditorRowViewModel, IDisposable> _segmentSubscriptions = new();
    private readonly CadSelectionService _selectionService;
    private readonly CadDocumentContextService _documentContext;
    private readonly CadStylePreviewService _previewService;
    private readonly CadEditorSessionHostService _sessionHost;
    private readonly ObservableCollection<CadLineTypeSegmentEditorRowViewModel> _segmentRows = new();
    private CancellationTokenSource? _previewCts;
    private const int PreviewSize = 48;
    private bool _suppressSelection;
    private bool _suppressEditorSync;
    private CadLineTypeEditorSnapshot? _baselineSnapshot;

    [Reactive]
    public partial string SearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string FilterText { get; set; } = string.Empty;

    [Reactive]
    public partial CadLineTypeRowViewModel? SelectedLineType { get; set; }

    [Reactive]
    public partial CadLineTypeSegmentEditorRowViewModel? SelectedSegment { get; set; }

    [Reactive]
    public partial string EditorName { get; set; } = string.Empty;

    [Reactive]
    public partial string EditorDescription { get; set; } = string.Empty;

    [Reactive]
    public partial bool IsCurrentLineType { get; set; }

    [Reactive]
    public partial bool IsDirty { get; set; }

    [Reactive]
    public partial bool CanApplyChanges { get; set; }

    [Reactive]
    public partial bool CanDeleteLineType { get; set; }

    [Reactive]
    public partial bool CanDuplicateLineType { get; set; }

    [Reactive]
    public partial bool CanSetCurrentLineType { get; set; }

    [Reactive]
    public partial bool CanRemoveSegment { get; set; }

    [Reactive]
    public partial bool CanMoveSegmentUp { get; set; }

    [Reactive]
    public partial bool CanMoveSegmentDown { get; set; }

    [Reactive]
    public partial string ValidationMessage { get; set; } = string.Empty;

    [Reactive]
    public partial string StatusMessage { get; set; } = string.Empty;

    [Reactive]
    public partial string SegmentSummaryText { get; set; } = string.Empty;

    [Reactive]
    public partial string EditorPreviewSummary { get; set; } = string.Empty;

    public ObservableCollection<CadLineTypeSegmentEditorRowViewModel> SegmentRows => _segmentRows;
    public DataGridCollectionView LineTypesView { get; }
    public DataGridColumnDefinitionList ColumnDefinitions { get; }
    public SortingModel SortingModel { get; } = new();
    public FilteringModel FilteringModel { get; } = new();
    public SearchModel SearchModel { get; } = new();

    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearFilterCommand { get; }
    public ReactiveCommand<Unit, Unit> NewLineTypeCommand { get; }
    public ReactiveCommand<Unit, Unit> DuplicateLineTypeCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteLineTypeCommand { get; }
    public ReactiveCommand<Unit, Unit> SetCurrentLineTypeCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyLineTypeCommand { get; }
    public ReactiveCommand<Unit, Unit> RevertLineTypeCommand { get; }
    public ReactiveCommand<Unit, Unit> AddDashSegmentCommand { get; }
    public ReactiveCommand<Unit, Unit> AddSpaceSegmentCommand { get; }
    public ReactiveCommand<Unit, Unit> AddDotSegmentCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveSegmentCommand { get; }
    public ReactiveCommand<Unit, Unit> MoveSegmentUpCommand { get; }
    public ReactiveCommand<Unit, Unit> MoveSegmentDownCommand { get; }

    public CadLineTypeToolViewModel(
        CadSelectionService selectionService,
        CadDocumentContextService documentContext,
        CadStylePreviewService previewService,
        CadEditorSessionHostService sessionHost)
    {
        _selectionService = selectionService;
        _documentContext = documentContext;
        _previewService = previewService;
        _sessionHost = sessionHost;

        LineTypesView = new DataGridCollectionView(_rows);
        ColumnDefinitions = CadLineTypeColumnDefinitions.Create();

        SearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        SearchModel.HighlightCurrent = true;
        SearchModel.WrapNavigation = true;

        _segmentRows.CollectionChanged += OnSegmentRowsChanged;

        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => ApplySearch());

        this.WhenAnyValue(x => x.FilterText)
            .Subscribe(_ => ApplyFilter());

        this.WhenAnyValue(x => x.SelectedLineType)
            .Subscribe(OnSelectedLineTypeChanged);

        this.WhenAnyValue(x => x.SelectedSegment)
            .Subscribe(_ => EvaluateEditorState());

        _selectionService.WhenAnyValue(x => x.SelectedObject)
            .Subscribe(UpdateSelectionFromService);

        _documentContext.WhenAnyValue(x => x.ActiveDocument)
            .Subscribe(LoadLineTypes);

        var editorChanges = Observable.Merge(
            this.WhenAnyValue(x => x.EditorName).Select(_ => Unit.Default),
            this.WhenAnyValue(x => x.EditorDescription).Select(_ => Unit.Default));
        editorChanges.Subscribe(_ => EvaluateEditorState());

        ClearSearchCommand = ReactiveCommand.Create(() => { SearchText = string.Empty; });
        ClearFilterCommand = ReactiveCommand.Create(() => { FilterText = string.Empty; });
        NewLineTypeCommand = ReactiveCommand.Create(CreateLineType);
        DuplicateLineTypeCommand = ReactiveCommand.Create(DuplicateSelectedLineType, this.WhenAnyValue(x => x.CanDuplicateLineType));
        DeleteLineTypeCommand = ReactiveCommand.Create(DeleteSelectedLineType, this.WhenAnyValue(x => x.CanDeleteLineType));
        SetCurrentLineTypeCommand = ReactiveCommand.Create(SetCurrentLineType, this.WhenAnyValue(x => x.CanSetCurrentLineType));
        ApplyLineTypeCommand = ReactiveCommand.Create(ApplyLineTypeChanges, this.WhenAnyValue(x => x.CanApplyChanges));
        RevertLineTypeCommand = ReactiveCommand.Create(RevertLineTypeChanges, this.WhenAnyValue(x => x.IsDirty));
        AddDashSegmentCommand = ReactiveCommand.Create(() => AddSegment(CadLineTypeSegmentKind.Dash));
        AddSpaceSegmentCommand = ReactiveCommand.Create(() => AddSegment(CadLineTypeSegmentKind.Space));
        AddDotSegmentCommand = ReactiveCommand.Create(() => AddSegment(CadLineTypeSegmentKind.Dot));
        RemoveSegmentCommand = ReactiveCommand.Create(RemoveSelectedSegment, this.WhenAnyValue(x => x.CanRemoveSegment));
        MoveSegmentUpCommand = ReactiveCommand.Create(MoveSelectedSegmentUp, this.WhenAnyValue(x => x.CanMoveSegmentUp));
        MoveSegmentDownCommand = ReactiveCommand.Create(MoveSelectedSegmentDown, this.WhenAnyValue(x => x.CanMoveSegmentDown));
    }

    private void LoadLineTypes(CadDocumentViewModel? documentViewModel)
    {
        var preferredLineType = ResolveLineType(_selectionService.SelectedObject);

        _rows.Clear();
        _rowMap.Clear();
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();

        var document = documentViewModel?.Document;
        var lineTypes = document?.LineTypes;
        if (lineTypes is null)
        {
            LineTypesView.Refresh();
            ClearEditor();
            EvaluateEditorState();
            return;
        }

        var currentLineTypeName = document?.Header.CurrentLineTypeName;
        foreach (var lineType in lineTypes)
        {
            if (lineType is null)
            {
                continue;
            }

            var row = new CadLineTypeRowViewModel(lineType);
            row.RefreshFromLineType(currentLineTypeName);
            _rows.Add(row);
            _rowMap[lineType] = row;
            QueuePreview(row);
        }

        LineTypesView.Refresh();
        ApplySearch();
        ApplyFilter();

        if (preferredLineType is not null && _rowMap.TryGetValue(preferredLineType, out var preferredRow))
        {
            SetSelectedLineType(preferredRow, publishSelection: false);
        }
        else
        {
            SetSelectedLineType(null, publishSelection: false);
        }

        EvaluateEditorState();
    }

    private void QueuePreview(CadLineTypeRowViewModel row)
    {
        if (_previewCts is null)
        {
            return;
        }

        var token = _previewCts.Token;
        _ = Task.Run(async () =>
        {
            var preview = await _previewService.GetLineTypePreviewAsync(row.LineType, PreviewSize, token)
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

    private void OnSelectedLineTypeChanged(CadLineTypeRowViewModel? row)
    {
        if (_suppressSelection)
        {
            return;
        }

        LoadEditor(row?.LineType);
        _selectionService.SelectedObject = row?.LineType;
        EvaluateEditorState();
    }

    private void SetSelectedLineType(CadLineTypeRowViewModel? row, bool publishSelection)
    {
        _suppressSelection = true;
        SelectedLineType = row;
        _suppressSelection = false;

        LoadEditor(row?.LineType);
        if (publishSelection)
        {
            _selectionService.SelectedObject = row?.LineType;
        }
    }

    private void UpdateSelectionFromService(object? selected)
    {
        if (_suppressSelection)
        {
            return;
        }

        var lineType = ResolveLineType(selected);
        if (lineType is not null && _rowMap.TryGetValue(lineType, out var row))
        {
            SetSelectedLineType(row, publishSelection: false);
            return;
        }

        if (SelectedLineType is not null)
        {
            SetSelectedLineType(null, publishSelection: false);
        }
    }

    private void LoadEditor(LineType? lineType)
    {
        _suppressEditorSync = true;

        if (lineType is null)
        {
            _baselineSnapshot = null;
            ClearEditorCore();
            _suppressEditorSync = false;
            return;
        }

        EditorName = lineType.Name;
        EditorDescription = lineType.Description ?? string.Empty;
        LoadSegments(lineType.Segments);

        var currentLineTypeName = _documentContext.ActiveDocument?.Document?.Header.CurrentLineTypeName;
        IsCurrentLineType = !string.IsNullOrWhiteSpace(currentLineTypeName) &&
                            string.Equals(currentLineTypeName, lineType.Name, StringComparison.OrdinalIgnoreCase);

        _baselineSnapshot = BuildSnapshotFromLineType(lineType);
        ValidationMessage = string.Empty;
        SegmentSummaryText = _baselineSnapshot.Value.SegmentSummary;
        EditorPreviewSummary = BuildPreviewSummary(_baselineSnapshot.Value, previewUnavailableReason: null);

        _suppressEditorSync = false;
    }

    private void LoadSegments(IEnumerable<LineType.Segment> segments)
    {
        ClearSegmentRows();
        foreach (var segment in segments)
        {
            var row = new CadLineTypeSegmentEditorRowViewModel
            {
                Kind = ResolveSegmentKind(segment),
                Length = Math.Abs(segment.Length).ToString("0.###", CultureInfo.InvariantCulture),
                IsText = segment.IsText,
                IsShape = segment.IsShape,
                TextValue = segment.Text ?? string.Empty,
                ShapeNumber = segment.ShapeNumber.ToString(CultureInfo.InvariantCulture),
                StyleName = segment.Style?.Name ?? string.Empty,
                Scale = segment.Scale.ToString("0.###", CultureInfo.InvariantCulture),
                RotationDegrees = (segment.Rotation * 180.0 / Math.PI).ToString("0.###", CultureInfo.InvariantCulture),
                OffsetX = segment.Offset.X.ToString("0.###", CultureInfo.InvariantCulture),
                OffsetY = segment.Offset.Y.ToString("0.###", CultureInfo.InvariantCulture),
                RotationIsAbsolute = segment.Flags.HasFlag(LineTypeShapeFlags.RotationIsAbsolute)
            };
            AddSegmentRow(row);
        }

        SelectedSegment = _segmentRows.Count > 0 ? _segmentRows[0] : null;
    }

    private void AddSegment(CadLineTypeSegmentKind kind)
    {
        var row = new CadLineTypeSegmentEditorRowViewModel
        {
            Kind = kind,
            Length = kind switch
            {
                CadLineTypeSegmentKind.Dash => "0.5",
                CadLineTypeSegmentKind.Space => "0.25",
                _ => "0"
            },
            Scale = "1"
        };

        AddSegmentRow(row);
        SelectedSegment = row;
        EvaluateEditorState();
    }

    private void RemoveSelectedSegment()
    {
        var selected = SelectedSegment;
        if (selected is null)
        {
            return;
        }

        var index = _segmentRows.IndexOf(selected);
        if (index < 0)
        {
            return;
        }

        RemoveSegmentRow(selected);
        if (_segmentRows.Count == 0)
        {
            SelectedSegment = null;
        }
        else
        {
            var nextIndex = Math.Min(index, _segmentRows.Count - 1);
            SelectedSegment = _segmentRows[nextIndex];
        }

        EvaluateEditorState();
    }

    private void MoveSelectedSegmentUp()
    {
        var selected = SelectedSegment;
        if (selected is null)
        {
            return;
        }

        var index = _segmentRows.IndexOf(selected);
        if (index <= 0)
        {
            return;
        }

        _segmentRows.Move(index, index - 1);
        EvaluateEditorState();
    }

    private void MoveSelectedSegmentDown()
    {
        var selected = SelectedSegment;
        if (selected is null)
        {
            return;
        }

        var index = _segmentRows.IndexOf(selected);
        if (index < 0 || index >= _segmentRows.Count - 1)
        {
            return;
        }

        _segmentRows.Move(index, index + 1);
        EvaluateEditorState();
    }

    private void OnSegmentRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is CadLineTypeSegmentEditorRowViewModel row)
                {
                    DetachSegmentRow(row);
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is CadLineTypeSegmentEditorRowViewModel row)
                {
                    AttachSegmentRow(row);
                }
            }
        }

        EvaluateEditorState();
    }

    private void AttachSegmentRow(CadLineTypeSegmentEditorRowViewModel row)
    {
        if (_segmentSubscriptions.ContainsKey(row))
        {
            return;
        }

        _segmentSubscriptions[row] = row.Changed.Subscribe(_ => EvaluateEditorState());
    }

    private void DetachSegmentRow(CadLineTypeSegmentEditorRowViewModel row)
    {
        if (!_segmentSubscriptions.Remove(row, out var subscription))
        {
            return;
        }

        subscription.Dispose();
    }

    private void AddSegmentRow(CadLineTypeSegmentEditorRowViewModel row)
    {
        _segmentRows.Add(row);
    }

    private void RemoveSegmentRow(CadLineTypeSegmentEditorRowViewModel row)
    {
        _segmentRows.Remove(row);
    }

    private void ClearSegmentRows()
    {
        foreach (var subscription in _segmentSubscriptions.Values)
        {
            subscription.Dispose();
        }

        _segmentSubscriptions.Clear();
        _segmentRows.Clear();
    }

    private void EvaluateEditorState()
    {
        if (_suppressEditorSync)
        {
            return;
        }

        var selected = SelectedLineType?.LineType;
        var document = _documentContext.ActiveDocument?.Document;

        CanDuplicateLineType = selected is not null;
        CanDeleteLineType = selected is not null &&
                            document is not null &&
                            !IsProtectedLineType(selected.Name) &&
                            document.LineTypes.Count > 3;

        var currentLineTypeName = document?.Header.CurrentLineTypeName;
        IsCurrentLineType = selected is not null &&
                            !string.IsNullOrWhiteSpace(currentLineTypeName) &&
                            string.Equals(currentLineTypeName, selected.Name, StringComparison.OrdinalIgnoreCase);
        CanSetCurrentLineType = selected is not null && !IsCurrentLineType;

        var selectedIndex = SelectedSegment is null ? -1 : _segmentRows.IndexOf(SelectedSegment);
        CanRemoveSegment = selectedIndex >= 0;
        CanMoveSegmentUp = selectedIndex > 0;
        CanMoveSegmentDown = selectedIndex >= 0 && selectedIndex < _segmentRows.Count - 1;

        if (selected is null || document is null || _baselineSnapshot is null)
        {
            IsDirty = false;
            CanApplyChanges = false;
            ValidationMessage = string.Empty;
            SegmentSummaryText = string.Empty;
            EditorPreviewSummary = string.Empty;
            return;
        }

        if (!TryBuildSnapshotFromEditor(out var snapshot, out _, out var error))
        {
            ValidationMessage = error;
            IsDirty = true;
            CanApplyChanges = false;
            SegmentSummaryText = string.Empty;
            EditorPreviewSummary = BuildPreviewSummary(_baselineSnapshot.Value, error);
            return;
        }

        ValidationMessage = string.Empty;
        IsDirty = !_baselineSnapshot.Value.Equals(snapshot);
        CanApplyChanges = IsDirty;
        SegmentSummaryText = snapshot.SegmentSummary;
        EditorPreviewSummary = BuildPreviewSummary(snapshot, previewUnavailableReason: null);
    }

    private bool TryBuildSnapshotFromEditor(
        out CadLineTypeEditorSnapshot snapshot,
        out List<LineType.Segment> compiledSegments,
        out string error)
    {
        snapshot = default;
        compiledSegments = [];
        error = string.Empty;

        var selected = SelectedLineType?.LineType;
        var document = _documentContext.ActiveDocument?.Document;
        if (selected is null || document is null)
        {
            error = "No active line type selected.";
            return false;
        }

        var name = EditorName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Line type name is required.";
            return false;
        }

        if (!string.Equals(name, selected.Name, StringComparison.OrdinalIgnoreCase) &&
            document.LineTypes.Contains(name))
        {
            error = $"A line type named '{name}' already exists.";
            return false;
        }

        var segmentTokens = new List<string>(_segmentRows.Count);
        for (var i = 0; i < _segmentRows.Count; i++)
        {
            var row = _segmentRows[i];
            if (!TryCompileSegment(document, row, i + 1, out var segment, out var token, out error))
            {
                return false;
            }

            compiledSegments.Add(segment);
            segmentTokens.Add(token);
        }

        var summary = segmentTokens.Count == 0
            ? "Continuous"
            : string.Join(", ", segmentTokens);
        var signature = segmentTokens.Count == 0
            ? "CONTINUOUS"
            : string.Join("|", segmentTokens);

        snapshot = new CadLineTypeEditorSnapshot(
            Name: name,
            Description: EditorDescription.Trim(),
            SegmentSignature: signature,
            SegmentSummary: summary);
        return true;
    }

    private bool TryCompileSegment(
        CadDocument document,
        CadLineTypeSegmentEditorRowViewModel row,
        int segmentNumber,
        out LineType.Segment segment,
        out string token,
        out string error)
    {
        segment = new LineType.Segment();
        token = string.Empty;
        error = string.Empty;

        if (row.IsShape && row.IsText)
        {
            error = $"Segment {segmentNumber}: choose either Text or Shape, not both.";
            return false;
        }

        var length = 0.0;
        if (row.Kind != CadLineTypeSegmentKind.Dot)
        {
            if (!TryParseDouble(row.Length, out length) || length <= 0.0)
            {
                error = $"Segment {segmentNumber}: length must be greater than 0.";
                return false;
            }
        }

        if (!TryParseDouble(row.Scale, out var scale) || scale <= 0.0)
        {
            error = $"Segment {segmentNumber}: scale must be greater than 0.";
            return false;
        }

        if (!TryParseDouble(row.RotationDegrees, out var rotationDegrees))
        {
            error = $"Segment {segmentNumber}: rotation must be a valid number.";
            return false;
        }

        if (!TryParseDouble(row.OffsetX, out var offsetX) ||
            !TryParseDouble(row.OffsetY, out var offsetY))
        {
            error = $"Segment {segmentNumber}: offset values must be valid numbers.";
            return false;
        }

        short shapeNumber = 0;
        if (row.IsShape)
        {
            if (!short.TryParse(row.ShapeNumber.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out shapeNumber))
            {
                error = $"Segment {segmentNumber}: shape number must be an integer.";
                return false;
            }

            if (shapeNumber <= 0)
            {
                error = $"Segment {segmentNumber}: shape number must be greater than 0.";
                return false;
            }
        }

        var styleName = row.StyleName.Trim();
        TextStyle? style = null;
        if (row.IsShape || row.IsText)
        {
            if (string.IsNullOrWhiteSpace(styleName))
            {
                styleName = document.Header.CurrentTextStyleName;
            }

            if (string.IsNullOrWhiteSpace(styleName) || !document.TextStyles.TryGetValue(styleName, out style))
            {
                error = $"Segment {segmentNumber}: text style '{styleName}' was not found.";
                return false;
            }
        }

        var flags = LineTypeShapeFlags.None;
        if (row.RotationIsAbsolute)
        {
            flags |= LineTypeShapeFlags.RotationIsAbsolute;
        }

        if (row.IsText)
        {
            if (string.IsNullOrWhiteSpace(row.TextValue))
            {
                error = $"Segment {segmentNumber}: text value is required when Text is enabled.";
                return false;
            }

            flags |= LineTypeShapeFlags.Text;
        }
        else if (row.IsShape)
        {
            flags |= LineTypeShapeFlags.Shape;
        }

        segment.Flags = flags;
        segment.Length = row.Kind switch
        {
            CadLineTypeSegmentKind.Dash => Math.Abs(length),
            CadLineTypeSegmentKind.Space => -Math.Abs(length),
            _ => 0.0
        };
        segment.Text = row.IsText ? row.TextValue.Trim() : string.Empty;
        segment.ShapeNumber = row.IsShape ? shapeNumber : (short)0;
        segment.Style = style;
        segment.Scale = scale;
        segment.Rotation = rotationDegrees * Math.PI / 180.0;
        segment.Offset = new XY(offsetX, offsetY);

        token = BuildSegmentToken(segment);
        return true;
    }

    private void CreateLineType()
    {
        var document = _documentContext.ActiveDocument?.Document;
        if (document is null)
        {
            return;
        }

        var name = CreateUniqueName(document, "LTYPE");
        var lineType = new LineType(name)
        {
            Description = $"Line type {name}"
        };
        lineType.AddSegment(new LineType.Segment
        {
            Length = 0.5
        });
        lineType.AddSegment(new LineType.Segment
        {
            Length = -0.25
        });

        document.LineTypes.Add(lineType);
        _previewService.InvalidateLineType(lineType);
        ReloadLineTypes(lineType, notifyDocumentChange: true, publishSelection: true);
        StatusMessage = $"Created line type '{lineType.Name}'.";
    }

    private void DuplicateSelectedLineType()
    {
        var source = SelectedLineType?.LineType;
        var document = _documentContext.ActiveDocument?.Document;
        if (source is null || document is null)
        {
            return;
        }

        var duplicate = new LineType(CreateUniqueName(document, $"{source.Name}_Copy"))
        {
            Description = source.Description
        };

        foreach (var segment in source.Segments)
        {
            duplicate.AddSegment(segment.Clone());
        }

        document.LineTypes.Add(duplicate);
        _previewService.InvalidateLineType(duplicate);
        ReloadLineTypes(duplicate, notifyDocumentChange: true, publishSelection: true);
        StatusMessage = $"Duplicated '{source.Name}' to '{duplicate.Name}'.";
    }

    private void DeleteSelectedLineType()
    {
        var selected = SelectedLineType?.LineType;
        var document = _documentContext.ActiveDocument?.Document;
        if (selected is null || document is null || !CanDeleteLineType)
        {
            return;
        }

        var selectedName = selected.Name;
        var deletedCurrent = string.Equals(
            document.Header.CurrentLineTypeName,
            selectedName,
            StringComparison.OrdinalIgnoreCase);
        if (deletedCurrent)
        {
            document.Header.CurrentLineTypeName = LineType.ByLayerName;
        }

        var removed = document.LineTypes.Remove(selectedName);
        if (removed is null)
        {
            StatusMessage = $"Could not delete line type '{selectedName}'.";
            return;
        }

        _previewService.InvalidateLineType(selected);

        LineType? preferred = null;
        var currentName = document.Header.CurrentLineTypeName;
        if (!string.IsNullOrWhiteSpace(currentName) &&
            document.LineTypes.TryGetValue(currentName, out var current))
        {
            preferred = current;
        }
        else if (document.LineTypes.TryGetValue(LineType.ByLayerName, out var byLayer))
        {
            preferred = byLayer;
        }
        else
        {
            preferred = document.LineTypes.FirstOrDefault();
        }

        ReloadLineTypes(preferred, notifyDocumentChange: true, publishSelection: true);
        StatusMessage = $"Deleted line type '{selectedName}'.";
    }

    private void SetCurrentLineType()
    {
        var selected = SelectedLineType?.LineType;
        var document = _documentContext.ActiveDocument?.Document;
        if (selected is null || document is null)
        {
            return;
        }

        document.Header.CurrentLineTypeName = selected.Name;
        ReloadLineTypes(selected, notifyDocumentChange: true, publishSelection: true);
        StatusMessage = $"Current line type set to '{selected.Name}'.";
    }

    private void ApplyLineTypeChanges()
    {
        var selected = SelectedLineType?.LineType;
        var document = _documentContext.ActiveDocument?.Document;
        if (selected is null || document is null)
        {
            return;
        }

        if (!TryBuildSnapshotFromEditor(out var snapshot, out var segments, out var error))
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
                        document.Header.CurrentLineTypeName,
                        previousName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    document.Header.CurrentLineTypeName = snapshot.Name;
                }
            }

            selected.Description = snapshot.Description;
            ReplaceSegments(selected, segments);
        }
        catch (Exception ex)
        {
            ValidationMessage = ex.Message;
            return;
        }

        _previewService.InvalidateLineType(selected);
        ReloadLineTypes(selected, notifyDocumentChange: true, publishSelection: true);
        StatusMessage = $"Updated line type '{selected.Name}'.";
    }

    private static void ReplaceSegments(LineType lineType, IReadOnlyList<LineType.Segment> segments)
    {
        if (lineType.Segments is not IList<LineType.Segment> existingSegments)
        {
            return;
        }

        existingSegments.Clear();
        foreach (var segment in segments)
        {
            lineType.AddSegment(segment);
        }
    }

    private void RevertLineTypeChanges()
    {
        var selected = SelectedLineType?.LineType;
        if (selected is null)
        {
            return;
        }

        LoadEditor(selected);
        EvaluateEditorState();
        StatusMessage = $"Reverted pending changes for '{selected.Name}'.";
    }

    private void ReloadLineTypes(LineType? preferredLineType, bool notifyDocumentChange, bool publishSelection)
    {
        var active = _documentContext.ActiveDocument;
        LoadLineTypes(active);

        if (preferredLineType is not null && _rowMap.TryGetValue(preferredLineType, out var row))
        {
            SetSelectedLineType(row, publishSelection);
            _previewService.InvalidateLineType(preferredLineType);
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
        EditorDescription = string.Empty;
        ClearSegmentRows();
        SelectedSegment = null;
        IsCurrentLineType = false;
        IsDirty = false;
        CanApplyChanges = false;
        CanDeleteLineType = false;
        CanDuplicateLineType = false;
        CanSetCurrentLineType = false;
        CanRemoveSegment = false;
        CanMoveSegmentUp = false;
        CanMoveSegmentDown = false;
        ValidationMessage = string.Empty;
        StatusMessage = string.Empty;
        SegmentSummaryText = string.Empty;
        EditorPreviewSummary = string.Empty;
    }

    private static CadLineTypeEditorSnapshot BuildSnapshotFromLineType(LineType lineType)
    {
        var tokens = lineType.Segments.Select(BuildSegmentToken).ToList();
        var summary = tokens.Count == 0 ? "Continuous" : string.Join(", ", tokens);
        var signature = tokens.Count == 0 ? "CONTINUOUS" : string.Join("|", tokens);
        return new CadLineTypeEditorSnapshot(
            Name: lineType.Name,
            Description: lineType.Description?.Trim() ?? string.Empty,
            SegmentSignature: signature,
            SegmentSummary: summary);
    }

    private static string BuildSegmentToken(LineType.Segment segment)
    {
        if (segment.IsPoint)
        {
            return "0";
        }

        var magnitude = Math.Abs(segment.Length).ToString("0.###", CultureInfo.InvariantCulture);
        var token = segment.IsSpace ? $"-{magnitude}" : magnitude;

        if (segment.IsText && !string.IsNullOrWhiteSpace(segment.Text))
        {
            token += $"[\"{segment.Text}\"]";
        }
        else if (segment.IsShape)
        {
            token += $"[#{segment.ShapeNumber}]";
        }

        if (segment.Style is not null)
        {
            token += $"@{segment.Style.Name}";
        }

        token += string.Create(
            CultureInfo.InvariantCulture,
            $":S{segment.Scale:0.###}:R{segment.Rotation * 180.0 / Math.PI:0.###}:X{segment.Offset.X:0.###}:Y{segment.Offset.Y:0.###}:A{(segment.Flags.HasFlag(LineTypeShapeFlags.RotationIsAbsolute) ? 1 : 0)}");
        return token;
    }

    private static CadLineTypeSegmentKind ResolveSegmentKind(LineType.Segment segment)
    {
        if (segment.IsPoint)
        {
            return CadLineTypeSegmentKind.Dot;
        }

        return segment.IsSpace ? CadLineTypeSegmentKind.Space : CadLineTypeSegmentKind.Dash;
    }

    private static bool IsProtectedLineType(string name)
    {
        return string.Equals(name, LineType.ByLayerName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, LineType.ByBlockName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, LineType.ContinuousName, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateUniqueName(CadDocument document, string baseName)
    {
        var name = baseName;
        var counter = 1;
        while (document.LineTypes.Contains(name))
        {
            name = $"{baseName}{counter}";
            counter++;
        }

        return name;
    }

    private static bool TryParseDouble(string text, out double value)
    {
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static LineType? ResolveLineType(object? selected)
    {
        switch (selected)
        {
            case CadDocumentTreeNode node:
                return ResolveLineType(node.Source);
            case LineType lineType:
                return lineType;
            case Entity entity:
                return entity.GetActiveLineType();
        }

        return null;
    }

    private static string BuildPreviewSummary(
        CadLineTypeEditorSnapshot snapshot,
        string? previewUnavailableReason)
    {
        if (!string.IsNullOrWhiteSpace(previewUnavailableReason))
        {
            return $"Preview unavailable: {previewUnavailableReason}";
        }

        var description = string.IsNullOrWhiteSpace(snapshot.Description)
            ? "(no description)"
            : snapshot.Description;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Preview {snapshot.Name}: {description}; Pattern: {snapshot.SegmentSummary}");
    }

    private readonly record struct CadLineTypeEditorSnapshot(
        string Name,
        string Description,
        string SegmentSignature,
        string SegmentSummary);
}
