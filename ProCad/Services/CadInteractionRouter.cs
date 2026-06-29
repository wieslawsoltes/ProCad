using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ProCad.Editing.Identifiers;
using ProCad.Editing.Interaction;
using ProCad.Editing.Operations;
using ProCad.Editing.Prompt;
using ProCad.Editing.Commands;
using ProCad.Editing.Selection;
using ProCad.Editing.Sessions;
using ProCad.Editing.Undo;
using ProCad.Rendering;
using ProCad.ViewModels;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Services;

public sealed class CadInteractionRouter : ICadInteractionRouter
{
    private enum CadSelectionDragMode
    {
        Rectangle,
        Lasso,
        Fence,
        Polygon
    }

    private readonly CadToolManager _toolManager;
    private readonly CadSelectionService _selectionService;
    private readonly CadSelectionAnnotationService _annotationService;
    private readonly ICadCommandRuntime _commandRuntime;
    private readonly ICadInteractiveCommandAdapterRegistry _interactiveAdapters;
    private readonly IReadOnlyList<CadShortcutBinding> _shortcutBindings;
    private readonly ICadSnapService _snapService;
    private readonly ICadTrackingService _trackingService;
    private readonly ICadGripService _gripService;
    private readonly RenderHitTestEngine _hitTestEngine = new();
    private readonly List<RenderHitTestResult> _hitResults = new();
    private readonly List<CadSnapCandidate> _snapCandidates = new();
    private IReadOnlyDictionary<string, GripBinding> _gripBindings =
        new Dictionary<string, GripBinding>(StringComparer.Ordinal);
    private IReadOnlyList<CadGripPoint> _activeGripSet = Array.Empty<CadGripPoint>();
    private CadGripPoint? _hotGrip;
    private CadGripPoint? _dragGrip;
    private bool _isGripDragging;
    private Vector2? _trackingBasePoint;
    private CadSnapResult? _activeSnapResult;
    private Vector2? _lastPointerWorldPoint;
    private bool _isSelectionWindowCandidate;
    private bool _isSelectionWindowDragging;
    private Vector2 _selectionWindowStartPoint;
    private Vector2 _selectionWindowCurrentPoint;
    private CadInputModifiers _selectionWindowModifiers;
    private CadSelectionDragMode _selectionDragMode = CadSelectionDragMode.Rectangle;
    private CadSelectionDragMode? _armedSelectionDragMode;
    private readonly List<Vector2> _selectionPathPoints = new();
    private bool _isEntityDragCandidate;
    private bool _isEntityDragging;
    private bool _isEntityDragCopy;
    private Vector2 _entityDragStartPoint;
    private Vector2 _entityDragCurrentPoint;
    private string _keyboardInputBuffer = string.Empty;
    private IReadOnlyList<CadCommandCompletionItem> _keyboardCompletionItems = Array.Empty<CadCommandCompletionItem>();
    private int _keyboardCompletionIndex = -1;
    private string _keyboardCompletionSeed = string.Empty;
    private Vector2? _selectionCyclePoint;
    private readonly List<Entity> _selectionCycleCandidates = new();
    private int _selectionCycleIndex = -1;
    private ICadEditorSession? _currentSession;

    public event EventHandler<IReadOnlyList<CadOperation>>? OperationsCommitted;

    private readonly record struct GripBinding(
        CadEntityId EntityId,
        string EntityType,
        string Role,
        int SegmentIndex);

    public CadInteractionRouter(
        CadToolManager toolManager,
        CadSelectionService selectionService,
        CadSelectionAnnotationService annotationService,
        ICadCommandRuntime commandRuntime,
        ICadInteractiveCommandAdapterRegistry? interactiveAdapters = null,
        IReadOnlyList<CadShortcutBinding>? shortcutBindings = null,
        ICadSnapService? snapService = null,
        ICadTrackingService? trackingService = null,
        ICadGripService? gripService = null)
    {
        _toolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
        _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
        _annotationService = annotationService ?? throw new ArgumentNullException(nameof(annotationService));
        _commandRuntime = commandRuntime ?? throw new ArgumentNullException(nameof(commandRuntime));
        _interactiveAdapters = interactiveAdapters ?? new CadInteractiveCommandAdapterRegistry(Array.Empty<ICadInteractiveCommandAdapter>());
        _shortcutBindings = shortcutBindings is { Count: > 0 }
            ? shortcutBindings
            : CadShortcutBindingCatalog.Create(CadShortcutProfile.AutoCadLike);
        _snapService = snapService ?? new CadSnapService();
        _trackingService = trackingService ?? new CadTrackingService();
        _gripService = gripService ?? new CadGripService();
    }

    public RenderScene? Scene { get; set; }
    public RenderSpatialIndex? SpatialIndex { get; set; }

    public void UpdateSnapEnabled(bool enabled)
    {
        _snapService.Enabled = enabled;
        if (!enabled)
        {
            _activeSnapResult = null;
        }
    }

    public void UpdateTracking(bool trackingEnabled, bool orthoEnabled, bool polarEnabled)
    {
        _trackingService.Enabled = trackingEnabled;
        _trackingService.OrthoEnabled = orthoEnabled;
        _trackingService.PolarEnabled = polarEnabled;
    }

    public void ResetTransientState(ICadEditorSession? session)
    {
        _isGripDragging = false;
        _dragGrip = null;
        _trackingBasePoint = null;
        _activeSnapResult = null;
        _hotGrip = null;
        _armedSelectionDragMode = null;
        ResetSelectionCycle();
        ResetEntityDragState();
        ResetSelectionWindowState();
        ResetKeyboardInput();
        _interactiveAdapters.Reset(session, _commandRuntime.State.ActiveCommand);
        _toolManager.HandleInput(CadToolInput.ClearHover(), BuildToolContext());
    }

    public CadToolVisualSnapshot BuildCurrentVisualSnapshot(ICadEditorSession? session)
    {
        _currentSession = session;
        UpdateSelectionAnnotationFromService(session);
        var anchor = ResolveCurrentAnchor(session);
        if (_lastPointerWorldPoint.HasValue)
        {
            UpdateGripFeedback(session, anchor, tolerance: 2f);
        }
        else
        {
            RebuildGripSet(session);
            _hotGrip = null;
        }

        if (_commandRuntime.State.IsActive)
        {
            return BuildSnapshotWithInteractivePreview(
                session,
                anchor,
                handled: false,
                status: null);
        }

        return BuildSnapshot(anchor, handled: false, status: null);
    }

    public async ValueTask<CadToolVisualSnapshot> RouteAsync(
        CadInteractionEvent interactionEvent,
        CadInteractionContext context,
        CancellationToken cancellationToken = default)
    {
        _currentSession = context.Session;
        switch (interactionEvent.Kind)
        {
            case CadInteractionEventKind.PointerMove:
            {
                var resolvedPoint = ResolvePoint(interactionEvent.WorldPoint, interactionEvent.Tolerance, out var snapStatus);

                if (_isEntityDragCandidate && !_commandRuntime.State.IsActive && interactionEvent.PointerButtons.HasFlag(CadInteractionPointerButtons.Left))
                {
                    _entityDragCurrentPoint = resolvedPoint;
                    _isEntityDragCopy = interactionEvent.Modifiers.HasFlag(CadInteractionModifiers.Control);
                    if (!_isEntityDragging)
                    {
                        var activationTolerance = MathF.Max(interactionEvent.Tolerance * 1.5f, 1e-4f);
                        var dragDelta = _entityDragCurrentPoint - _entityDragStartPoint;
                        if (dragDelta.LengthSquared() >= activationTolerance * activationTolerance)
                        {
                            _isEntityDragging = true;
                        }
                    }

                    if (_isEntityDragging)
                    {
                        return BuildEntityDragSnapshot(context.Session, resolvedPoint, handled: true, status: null);
                    }

                    return BuildSnapshot(resolvedPoint, handled: true, status: null);
                }

                if (_isGripDragging && interactionEvent.PointerButtons.HasFlag(CadInteractionPointerButtons.Left))
                {
                    return BuildGripDragSnapshot(resolvedPoint, handled: true, status: snapStatus);
                }

                if (_isSelectionWindowCandidate &&
                    !_commandRuntime.State.IsActive &&
                    interactionEvent.PointerButtons.HasFlag(CadInteractionPointerButtons.Left))
                {
                    _selectionWindowCurrentPoint = interactionEvent.WorldPoint;
                    AppendSelectionPathPoint(_selectionWindowCurrentPoint, interactionEvent.Tolerance);
                    if (!_isSelectionWindowDragging)
                    {
                        var activationTolerance = MathF.Max(interactionEvent.Tolerance * 1.5f, 1e-4f);
                        var dragDelta = _selectionWindowCurrentPoint - _selectionWindowStartPoint;
                        if (dragDelta.LengthSquared() >= activationTolerance * activationTolerance ||
                            (_selectionDragMode != CadSelectionDragMode.Rectangle && _selectionPathPoints.Count > 2))
                        {
                            _isSelectionWindowDragging = true;
                        }
                    }

                    if (_isSelectionWindowDragging)
                    {
                        return BuildSelectionWindowSnapshot(resolvedPoint, handled: true, status: snapStatus);
                    }
                }

                if (interactionEvent.PointerButtons == CadInteractionPointerButtons.None)
                {
                    HandleToolInput(
                        new CadRenderHitTestRequest(
                            resolvedPoint,
                            interactionEvent.Tolerance,
                            CadHitTestKind.Hover,
                            MapModifiers(interactionEvent.Modifiers)),
                        context.Session);

                    if (_commandRuntime.State.IsActive)
                    {
                        return BuildSnapshotWithInteractivePreview(
                            context.Session,
                            resolvedPoint,
                            handled: true,
                            status: snapStatus);
                    }

                    UpdateGripFeedback(context.Session, resolvedPoint, interactionEvent.Tolerance);
                    return BuildSnapshot(resolvedPoint, handled: true, status: snapStatus);
                }

                break;
            }
            case CadInteractionEventKind.PointerDown:
                if (interactionEvent.PointerButtons.HasFlag(CadInteractionPointerButtons.Left))
                {
                    var resolvedPoint = ResolvePoint(interactionEvent.WorldPoint, interactionEvent.Tolerance, out var snapStatus);
                    ResetSelectionWindowState();
                    if (_commandRuntime.State.IsActive)
                    {
                        var requiredSelectionCount = GetRequiredSelectionCount(_commandRuntime.State.ActiveCommand);
                        var selectedCount = GetSessionSelectionCount(context.Session);
                        if (requiredSelectionCount > 0 &&
                            selectedCount < requiredSelectionCount)
                        {
                            var selectionModifiers = MapModifiers(interactionEvent.Modifiers);
                            if (requiredSelectionCount > 1 &&
                                selectedCount > 0 &&
                                !selectionModifiers.HasFlag(CadInputModifiers.Control) &&
                                !selectionModifiers.HasFlag(CadInputModifiers.Shift))
                            {
                                selectionModifiers |= CadInputModifiers.Shift;
                            }

                            HandleToolInput(
                                new CadRenderHitTestRequest(
                                    interactionEvent.WorldPoint,
                                    interactionEvent.Tolerance,
                                    CadHitTestKind.Select,
                                    selectionModifiers),
                                context.Session);

                            var refreshedSelectionCount = GetSessionSelectionCount(context.Session);
                            var status = refreshedSelectionCount >= requiredSelectionCount
                                ? "Selection ready. Specify point."
                                : requiredSelectionCount > 1
                                    ? $"Select objects ({refreshedSelectionCount}/{requiredSelectionCount})"
                                    : "Select objects";
                            return BuildSnapshotWithInteractivePreview(
                                context.Session,
                                resolvedPoint,
                                handled: true,
                                status: status);
                        }

                        var token = new CadPromptToken(
                            CadPromptTokenType.Coordinate,
                            FormatCoordinate(resolvedPoint));
                        var resolution = await SubmitInteractiveAsync(token, context.Session, commit: false, cancellationToken)
                            .ConfigureAwait(false);
                        _trackingBasePoint = resolvedPoint;
                        return BuildSnapshotWithInteractivePreview(
                            context.Session,
                            resolvedPoint,
                            handled: true,
                            status: resolution.State.LastMessage ?? snapStatus);
                    }

                    RebuildGripSet(context.Session);
                    if (!interactionEvent.Modifiers.HasFlag(CadInteractionModifiers.Alt) &&
                        _gripService.TryResolveHotGrip(
                            resolvedPoint,
                            ResolveGripTolerance(interactionEvent.Tolerance),
                            _activeGripSet,
                            out var grip))
                    {
                        _isGripDragging = true;
                        _dragGrip = grip;
                        _trackingBasePoint = grip.Position;
                        return BuildGripDragSnapshot(resolvedPoint, handled: true, status: $"Grip: {grip.Kind}");
                    }

                    if (TryResolveEntityHit(
                            interactionEvent.WorldPoint,
                            interactionEvent.Tolerance,
                            context.Session,
                            out var hitEntity,
                            out var cycleStatus))
                    {
                        var selectionModifiers = MapModifiers(interactionEvent.Modifiers);
                        if (hitEntity is not null &&
                            interactionEvent.Modifiers.HasFlag(CadInteractionModifiers.Alt) &&
                            TryHandleSubSelectionGesture(
                                context.Session,
                                hitEntity,
                                interactionEvent.Modifiers,
                                interactionEvent.Tolerance,
                                resolvedPoint,
                                out var subSelectionStatus))
                        {
                            return BuildSnapshot(
                                resolvedPoint,
                                handled: true,
                                status: subSelectionStatus);
                        }

                        if (!selectionModifiers.HasFlag(CadInputModifiers.Control) &&
                            !selectionModifiers.HasFlag(CadInputModifiers.Shift) &&
                            string.IsNullOrWhiteSpace(cycleStatus) &&
                            hitEntity is not null &&
                            IsEntitySelected(context.Session, hitEntity))
                        {
                            BeginEntityDrag(resolvedPoint);
                            return BuildEntityDragSnapshot(
                                context.Session,
                                resolvedPoint,
                                handled: true,
                                status: "Drag selected entities to move. Hold Ctrl while dragging to copy.");
                        }

                        HandleToolInput(
                            new CadRenderHitTestRequest(
                                interactionEvent.WorldPoint,
                                interactionEvent.Tolerance,
                                CadHitTestKind.Select,
                                selectionModifiers),
                            context.Session);

                        if (hitEntity is not null)
                        {
                            _selectionService.ApplySelection(
                                [hitEntity],
                                ResolveSelectionMode(selectionModifiers));
                            NormalizeSelectionForSession(context.Session);
                            UpdateSelectionAnnotationFromService(context.Session);
                        }

                        UpdateGripFeedback(context.Session, resolvedPoint, interactionEvent.Tolerance);
                        return BuildSnapshot(
                            resolvedPoint,
                            handled: true,
                            status: string.IsNullOrWhiteSpace(cycleStatus) ? snapStatus : cycleStatus);
                    }
                    else
                    {
                        ResetSelectionCycle();
                        BeginSelectionWindow(interactionEvent.WorldPoint, MapModifiers(interactionEvent.Modifiers));
                        var modeStatus = _selectionDragMode switch
                        {
                            CadSelectionDragMode.Lasso => "Lasso selection started.",
                            CadSelectionDragMode.Fence => "Fence selection started.",
                            CadSelectionDragMode.Polygon => "Polygon selection started.",
                            _ => snapStatus
                        };
                        return BuildSnapshot(resolvedPoint, handled: true, status: modeStatus);
                    }
                }
                break;
            case CadInteractionEventKind.PointerUp:
                if (_isEntityDragCandidate)
                {
                    var resolvedPoint = ResolvePoint(interactionEvent.WorldPoint, interactionEvent.Tolerance, out _);
                    string status;
                    if (_isEntityDragging)
                    {
                        status = await CommitEntityDragAsync(
                                context.Session,
                                resolvedPoint,
                                _isEntityDragCopy,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        status = _isEntityDragCopy
                            ? "Copy drag canceled."
                            : "Move drag canceled.";
                    }

                    ResetEntityDragState();
                    UpdateGripFeedback(context.Session, resolvedPoint, interactionEvent.Tolerance);
                    return BuildSnapshot(resolvedPoint, handled: true, status);
                }

                if (_isGripDragging)
                {
                    var resolvedPoint = ResolvePoint(interactionEvent.WorldPoint, interactionEvent.Tolerance, out _);
                    var status = await CommitGripDragAsync(context.Session, resolvedPoint, cancellationToken).ConfigureAwait(false);
                    _isGripDragging = false;
                    _dragGrip = null;
                    _trackingBasePoint = null;
                    UpdateGripFeedback(context.Session, resolvedPoint, interactionEvent.Tolerance);
                    return BuildSnapshot(resolvedPoint, handled: true, status);
                }

                if (_isSelectionWindowCandidate)
                {
                    var resolvedPoint = ResolvePoint(interactionEvent.WorldPoint, interactionEvent.Tolerance, out var snapStatus);
                    var status = snapStatus;
                    if (_isSelectionWindowDragging)
                    {
                        _selectionWindowCurrentPoint = interactionEvent.WorldPoint;
                        AppendSelectionPathPoint(_selectionWindowCurrentPoint, interactionEvent.Tolerance);
                        status = ApplySelectionWindow(context.Session);
                    }
                    else if (!_selectionWindowModifiers.HasFlag(CadInputModifiers.Control) &&
                             !_selectionWindowModifiers.HasFlag(CadInputModifiers.Shift))
                    {
                        _selectionService.ClearSelection();
                        _annotationService.UpdateSelection(null, null);
                        status = "Selection cleared.";
                    }

                    ResetSelectionWindowState();
                    UpdateGripFeedback(context.Session, resolvedPoint, interactionEvent.Tolerance);
                    return BuildSnapshot(resolvedPoint, handled: true, status);
                }
                break;
            case CadInteractionEventKind.KeyDown:
            {
                if (await TryHandleShortcutAsync(interactionEvent, context.Session, cancellationToken).ConfigureAwait(false) is { } shortcutStatus)
                {
                    return BuildSnapshot(interactionEvent.WorldPoint, handled: true, shortcutStatus);
                }

                if (TryHandleBufferNavigation(interactionEvent, out var bufferStatus))
                {
                    return BuildSnapshot(interactionEvent.WorldPoint, handled: true, bufferStatus);
                }

                if (IsCommitKey(interactionEvent.Key) &&
                    !interactionEvent.Modifiers.HasFlag(CadInteractionModifiers.Control) &&
                    !interactionEvent.Modifiers.HasFlag(CadInteractionModifiers.Alt))
                {
                    var status = await CommitKeyboardInputAsync(context.Session, cancellationToken)
                        .ConfigureAwait(false);
                    return BuildSnapshotWithInteractivePreview(
                        context.Session,
                        interactionEvent.WorldPoint,
                        handled: true,
                        status);
                }

                if (string.Equals(interactionEvent.Key, "Escape", StringComparison.OrdinalIgnoreCase))
                {
                    var status = _isGripDragging ? "Grip edit canceled." : _commandRuntime.State.LastMessage;
                    ResetTransientState(context.Session);
                    if (_commandRuntime.State.IsActive)
                    {
                        _commandRuntime.Cancel();
                        status = _commandRuntime.State.LastMessage;
                    }

                    RefreshRuntimePreviewForKeyboardInput();
                    return new CadToolVisualSnapshot(
                        Handled: true,
                        Prompt: _commandRuntime.State.Prompt,
                        Status: status,
                        Hints: Array.Empty<CadToolVisualHint>());
                }
                break;
            }
            case CadInteractionEventKind.TextInput:
                if (AppendKeyboardInput(interactionEvent.Text, out var textStatus))
                {
                    return BuildSnapshot(interactionEvent.WorldPoint, handled: true, textStatus);
                }
                break;
            case CadInteractionEventKind.CommandInput:
                if (AppendKeyboardInput(interactionEvent.Text, out var commandStatus))
                {
                    return BuildSnapshot(interactionEvent.WorldPoint, handled: true, commandStatus);
                }
                break;
        }

        return BuildSnapshot(interactionEvent.WorldPoint, handled: false, status: null);
    }

    private async ValueTask<string> CommitEntityDragAsync(
        ICadEditorSession? session,
        Vector2 targetPoint,
        bool copyMode,
        CancellationToken cancellationToken)
    {
        if (session is null)
        {
            return "No active editor session.";
        }

        var delta = targetPoint - _entityDragStartPoint;
        if (delta.LengthSquared() <= float.Epsilon)
        {
            return copyMode ? "Copy drag canceled." : "Move drag canceled.";
        }

        var command = copyMode ? "COPY" : "MOVE";
        var commandText = string.Create(
            CultureInfo.InvariantCulture,
            $"{command} {delta.X:0.###},{delta.Y:0.###}");

        IDisposable? undoScope = null;
        try
        {
            undoScope = CadUndoExecutionContext.Push(new CadUndoRecordOptions(
                CommandId: command,
                Label: copyMode ? "Drag Copy" : "Drag Move",
                ActorId: session.SessionId.Value,
                Source: CadUndoSource.Tool,
                MergeKey: copyMode ? "drag:copy" : "drag:move"));

            var resolution = await _commandRuntime
                .SubmitAsync(commandText, session, cancellationToken)
                .ConfigureAwait(false);
            if (resolution.Result?.Operations is { Count: > 0 } operations)
            {
                OperationsCommitted?.Invoke(this, operations);
            }

            return resolution.Result?.Message ??
                   resolution.State.LastMessage ??
                   (copyMode ? "Copied selection." : "Moved selection.");
        }
        finally
        {
            undoScope?.Dispose();
        }
    }

    private async ValueTask<string> CommitGripDragAsync(
        ICadEditorSession? session,
        Vector2 targetPoint,
        CancellationToken cancellationToken)
    {
        if (_dragGrip is not { } grip || session is null)
        {
            return "Grip edit was not applied.";
        }

        var delta = targetPoint - grip.Position;
        if (delta.LengthSquared() <= float.Epsilon)
        {
            return "Grip edit canceled.";
        }

        if (session is CadDocumentSession documentSession &&
            TryApplyGripEdit(documentSession, grip, targetPoint, delta, out var appliedStatus, out var operations))
        {
            if (operations.Count > 0)
            {
                OperationsCommitted?.Invoke(this, operations);
            }

            return appliedStatus;
        }

        var command = string.Create(
            CultureInfo.InvariantCulture,
            $"MOVE {delta.X:0.###},{delta.Y:0.###}");
        var resolution = await _commandRuntime.SubmitAsync(command, session, cancellationToken).ConfigureAwait(false);
        if (resolution.Result?.Operations is { Count: > 0 } commandOperations)
        {
            OperationsCommitted?.Invoke(this, commandOperations);
        }

        return resolution.Result?.Message ?? resolution.State.LastMessage ?? "Grip edit applied.";
    }

    private bool TryApplyGripEdit(
        CadDocumentSession session,
        CadGripPoint grip,
        Vector2 targetPoint,
        Vector2 delta,
        out string status,
        out IReadOnlyList<CadOperation> operations)
    {
        status = "Grip edit applied.";
        operations = Array.Empty<CadOperation>();
        var gripBindings = _gripBindings;
        if (string.IsNullOrWhiteSpace(grip.Tag) ||
            !gripBindings.TryGetValue(grip.Tag, out var binding) ||
            !session.EntityIndex.TryGetEntity(binding.EntityId, out var entity))
        {
            return false;
        }

        var forward = new List<CadOperation>(1);
        var inverse = new List<CadOperation>(1);
        switch (entity)
        {
            case Line line:
            {
                var fromStart = line.StartPoint;
                var fromEnd = line.EndPoint;
                var toStart = fromStart;
                var toEnd = fromEnd;
                switch (binding.Role)
                {
                    case "Start":
                        toStart = ToXyz(targetPoint, fromStart.Z);
                        break;
                    case "End":
                        toEnd = ToXyz(targetPoint, fromEnd.Z);
                        break;
                    case "Mid":
                        var move = new XYZ(delta.X, delta.Y, 0.0);
                        toStart = Translate(fromStart, move);
                        toEnd = Translate(fromEnd, move);
                        break;
                    default:
                        return false;
                }

                forward.Add(CadOperationPayloadCodec.TransformLine(binding.EntityId, fromStart, fromEnd, toStart, toEnd));
                inverse.Add(CadOperationPayloadCodec.TransformLine(binding.EntityId, toStart, toEnd, fromStart, fromEnd));
                status = $"Stretched LINE ({binding.Role}).";
                break;
            }
            case LwPolyline polyline:
            {
                var fromVertices = ToPolylineVertices(polyline).ToArray();
                if (fromVertices.Length == 0)
                {
                    status = "Grip edit canceled.";
                    return false;
                }

                var closed = polyline.IsClosed;
                var toVertices = fromVertices.ToList();
                if (binding.Role == "Vertex")
                {
                    var index = Math.Clamp(binding.SegmentIndex, 0, toVertices.Count - 1);
                    var source = toVertices[index];
                    toVertices[index] = ToXyz(targetPoint, source.Z);
                    status = "Edited polyline vertex.";
                }
                else if (binding.Role == "SegmentMidpoint")
                {
                    var insertIndex = Math.Clamp(binding.SegmentIndex + 1, 0, toVertices.Count);
                    toVertices.Insert(insertIndex, ToXyz(targetPoint, polyline.Elevation));
                    status = "Inserted polyline vertex.";
                }
                else
                {
                    return false;
                }

                var toArray = toVertices.ToArray();
                forward.Add(CadOperationPayloadCodec.TransformLwPolyline(binding.EntityId, fromVertices, closed, toArray, closed));
                inverse.Add(CadOperationPayloadCodec.TransformLwPolyline(binding.EntityId, toArray, closed, fromVertices, closed));
                break;
            }
            case Circle circle when circle is not Arc:
            {
                var fromCenter = circle.Center;
                var fromRadius = Math.Max(1e-6, circle.Radius);
                var toCenter = fromCenter;
                var toRadius = fromRadius;
                if (binding.Role == "Center")
                {
                    toCenter = ToXyz(targetPoint, fromCenter.Z);
                    status = "Moved circle center.";
                }
                else if (binding.Role == "Quadrant")
                {
                    var center = ToVector2(fromCenter);
                    var radius = Vector2.Distance(center, targetPoint);
                    if (radius <= 1e-6f)
                    {
                        status = "Circle radius must be greater than zero.";
                        return false;
                    }

                    toRadius = radius;
                    status = "Adjusted circle radius.";
                }
                else
                {
                    return false;
                }

                forward.Add(CadOperationPayloadCodec.TransformCircle(binding.EntityId, fromCenter, fromRadius, toCenter, toRadius));
                inverse.Add(CadOperationPayloadCodec.TransformCircle(binding.EntityId, toCenter, toRadius, fromCenter, fromRadius));
                break;
            }
            case Arc arc:
            {
                var fromCenter = arc.Center;
                var fromRadius = Math.Max(1e-6, arc.Radius);
                var fromStartAngle = arc.StartAngle;
                var fromEndAngle = arc.EndAngle;
                var toCenter = fromCenter;
                var toRadius = fromRadius;
                var toStartAngle = fromStartAngle;
                var toEndAngle = fromEndAngle;
                if (binding.Role == "Center")
                {
                    toCenter = ToXyz(targetPoint, fromCenter.Z);
                    status = "Moved arc center.";
                }
                else if (binding.Role == "Mid")
                {
                    var move = new XYZ(delta.X, delta.Y, 0.0);
                    toCenter = Translate(fromCenter, move);
                    status = "Moved arc.";
                }
                else if (binding.Role == "Start" || binding.Role == "End")
                {
                    var center = ToVector2(fromCenter);
                    var radius = Vector2.Distance(center, targetPoint);
                    if (radius <= 1e-6f)
                    {
                        status = "Arc radius must be greater than zero.";
                        return false;
                    }

                    var angle = NormalizeAngle(MathF.Atan2(targetPoint.Y - center.Y, targetPoint.X - center.X));
                    toRadius = radius;
                    if (binding.Role == "Start")
                    {
                        toStartAngle = angle;
                        status = "Adjusted arc start.";
                    }
                    else
                    {
                        toEndAngle = angle;
                        status = "Adjusted arc end.";
                    }
                }
                else
                {
                    return false;
                }

                forward.Add(CadOperationPayloadCodec.TransformArc(
                    binding.EntityId,
                    fromCenter,
                    fromRadius,
                    fromStartAngle,
                    fromEndAngle,
                    toCenter,
                    toRadius,
                    toStartAngle,
                    toEndAngle));
                inverse.Add(CadOperationPayloadCodec.TransformArc(
                    binding.EntityId,
                    toCenter,
                    toRadius,
                    toStartAngle,
                    toEndAngle,
                    fromCenter,
                    fromRadius,
                    fromStartAngle,
                    fromEndAngle));
                break;
            }
            case Point point:
            {
                var fromLocation = point.Location;
                var toLocation = ToXyz(targetPoint, fromLocation.Z);
                forward.Add(CadOperationPayloadCodec.TransformPoint(binding.EntityId, fromLocation, toLocation));
                inverse.Add(CadOperationPayloadCodec.TransformPoint(binding.EntityId, toLocation, fromLocation));
                status = "Moved point.";
                break;
            }
            case TextEntity text:
            {
                var fromInsert = text.InsertPoint;
                var fromAlignment = text.AlignmentPoint;
                var fromHeight = Math.Max(text.Height, 1e-6d);
                var fromRotation = text.Rotation;
                var toInsert = fromInsert;
                var toAlignment = fromAlignment;
                var toHeight = fromHeight;
                var toRotation = fromRotation;
                switch (binding.Role)
                {
                    case "Insert":
                    {
                        toInsert = ToXyz(targetPoint, fromInsert.Z);
                        var translation = new XYZ(
                            toInsert.X - fromInsert.X,
                            toInsert.Y - fromInsert.Y,
                            toInsert.Z - fromInsert.Z);
                        toAlignment = Translate(fromAlignment, translation);
                        status = "Moved text.";
                        break;
                    }
                    case "Alignment":
                        toAlignment = ToXyz(targetPoint, fromAlignment.Z);
                        status = "Adjusted text alignment.";
                        break;
                    case "BaselineEnd":
                    {
                        var insert = ToVector2(fromInsert);
                        var directionDelta = targetPoint - insert;
                        if (directionDelta.LengthSquared() <= 1e-8f)
                        {
                            status = "Text rotation handle requires a non-zero direction.";
                            return false;
                        }

                        var direction = Vector2.Normalize(directionDelta);
                        toRotation = NormalizeAngle(MathF.Atan2(direction.Y, direction.X));
                        var alignmentVector = ToVector2(fromAlignment) - insert;
                        if (alignmentVector.LengthSquared() > 1e-8f)
                        {
                            var alignmentDistance = alignmentVector.Length();
                            toAlignment = ToXyz(insert + direction * alignmentDistance, fromAlignment.Z);
                        }

                        status = "Adjusted text rotation.";
                        break;
                    }
                    case "Height":
                    {
                        var insert = ToVector2(fromInsert);
                        var direction = ResolveTextDirectionVector(text);
                        var normal = new Vector2(-direction.Y, direction.X);
                        var projectedHeight = Math.Abs(Vector2.Dot(targetPoint - insert, normal));
                        toHeight = Math.Max(projectedHeight, 1e-3f);
                        status = "Adjusted text height.";
                        break;
                    }
                    default:
                        return false;
                }

                forward.Add(CadOperationPayloadCodec.TransformText(
                    binding.EntityId,
                    fromInsert,
                    fromAlignment,
                    fromHeight,
                    fromRotation,
                    toInsert,
                    toAlignment,
                    toHeight,
                    toRotation));
                inverse.Add(CadOperationPayloadCodec.TransformText(
                    binding.EntityId,
                    toInsert,
                    toAlignment,
                    toHeight,
                    toRotation,
                    fromInsert,
                    fromAlignment,
                    fromHeight,
                    fromRotation));
                break;
            }
            case MText mtext:
            {
                var fromInsert = mtext.InsertPoint;
                var fromTextDirection = NormalizeDirection(mtext.AlignmentPoint);
                if (fromTextDirection.X == 0.0 && fromTextDirection.Y == 0.0 && fromTextDirection.Z == 0.0)
                {
                    fromTextDirection = new XYZ(1.0, 0.0, 0.0);
                }

                var fromHeight = Math.Max(mtext.Height, 1e-6d);
                var fromRectangleWidth = Math.Max(mtext.RectangleWidth, 1e-6d);
                var toInsert = fromInsert;
                var toTextDirection = fromTextDirection;
                var toHeight = fromHeight;
                var toRectangleWidth = fromRectangleWidth;
                var insert = ToVector2(fromInsert);
                var direction = NormalizeOrFallback(
                    new Vector2((float)fromTextDirection.X, (float)fromTextDirection.Y),
                    Vector2.UnitX);
                switch (binding.Role)
                {
                    case "Insert":
                        toInsert = ToXyz(targetPoint, fromInsert.Z);
                        status = "Moved mtext.";
                        break;
                    case "Direction":
                    {
                        var directionDelta = targetPoint - insert;
                        if (directionDelta.LengthSquared() <= 1e-8f)
                        {
                            status = "MText direction handle requires a non-zero direction.";
                            return false;
                        }

                        var normalizedDirection = Vector2.Normalize(directionDelta);
                        toTextDirection = new XYZ(normalizedDirection.X, normalizedDirection.Y, fromTextDirection.Z);
                        status = "Adjusted mtext direction.";
                        break;
                    }
                    case "Width":
                    {
                        var projectedWidth = Math.Abs(Vector2.Dot(targetPoint - insert, direction));
                        toRectangleWidth = Math.Max(projectedWidth, 1e-3f);
                        status = "Adjusted mtext width.";
                        break;
                    }
                    case "Height":
                    {
                        var normal = new Vector2(-direction.Y, direction.X);
                        var projectedHeight = Math.Abs(Vector2.Dot(targetPoint - insert, normal));
                        toHeight = Math.Max(projectedHeight, 1e-3f);
                        status = "Adjusted mtext height.";
                        break;
                    }
                    default:
                        return false;
                }

                forward.Add(CadOperationPayloadCodec.TransformMText(
                    binding.EntityId,
                    fromInsert,
                    fromTextDirection,
                    fromHeight,
                    fromRectangleWidth,
                    toInsert,
                    toTextDirection,
                    toHeight,
                    toRectangleWidth));
                inverse.Add(CadOperationPayloadCodec.TransformMText(
                    binding.EntityId,
                    toInsert,
                    toTextDirection,
                    toHeight,
                    toRectangleWidth,
                    fromInsert,
                    fromTextDirection,
                    fromHeight,
                    fromRectangleWidth));
                break;
            }
            default:
                return false;
        }

        if (forward.Count == 0 || inverse.Count == 0)
        {
            return false;
        }

        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forward);
        var inverseBatch = session.NextBatch(actorId, inverse.AsEnumerable().Reverse().ToArray());
        session.Apply(forwardBatch);
        session.UndoRedo.Record(
            forwardBatch,
            inverseBatch,
            new CadUndoRecordOptions(
                CommandId: "GRIP",
                Label: "Grip Edit",
                ActorId: actorId,
                Source: CadUndoSource.Tool,
                MergeKey: $"grip:{binding.EntityId.Value:D}"));
        operations = forward;
        return true;
    }

    private bool TryHandleSubSelectionGesture(
        ICadEditorSession? session,
        Entity hitEntity,
        CadInteractionModifiers modifiers,
        float tolerance,
        Vector2 resolvedPoint,
        out string status)
    {
        status = string.Empty;
        if (!modifiers.HasFlag(CadInteractionModifiers.Alt) ||
            _selectionService.SelectedObjects.Count == 0)
        {
            return false;
        }

        var targetEntity = hitEntity;
        var isSelected = TryResolveSelectedEntityReference(session, targetEntity, out var selectedReference);
        if (modifiers.HasFlag(CadInteractionModifiers.Control))
        {
            if (!isSelected &&
                TryResolveSelectedEntityHit(resolvedPoint, tolerance, session, out var selectedHit))
            {
                targetEntity = selectedHit;
                isSelected = TryResolveSelectedEntityReference(session, targetEntity, out selectedReference);
            }

            if (!isSelected)
            {
                status = "Sub-selection remove ignored.";
                return true;
            }

            _selectionService.ApplySelection([selectedReference], CadSelectionMode.Remove);
            NormalizeSelectionForSession(session);
            UpdateSelectionAnnotationFromService(session);
            UpdateGripFeedback(session, resolvedPoint, tolerance);
            status = "Sub-selection removed.";
            return true;
        }

        if (modifiers.HasFlag(CadInteractionModifiers.Shift))
        {
            var changed = _selectionService.ApplySelection([hitEntity], CadSelectionMode.Add);
            var primaryChanged = _selectionService.SetPrimarySelection(hitEntity);
            if (changed || primaryChanged)
            {
                NormalizeSelectionForSession(session);
                UpdateSelectionAnnotationFromService(session);
                UpdateGripFeedback(session, resolvedPoint, tolerance);
            }

            status = changed || primaryChanged
                ? "Sub-selection updated."
                : "Sub-selection unchanged.";
            return true;
        }

        if (_selectionService.SelectedObjects.Count > 1 && isSelected)
        {
            if (_selectionService.SetPrimarySelection(hitEntity))
            {
                NormalizeSelectionForSession(session);
                UpdateSelectionAnnotationFromService(session);
                UpdateGripFeedback(session, resolvedPoint, tolerance);
            }

            status = "Sub-selection updated.";
            return true;
        }

        return false;
    }

    private bool TryResolveSelectedEntityHit(
        Vector2 worldPoint,
        float tolerance,
        ICadEditorSession? session,
        out Entity selectedEntity)
    {
        selectedEntity = null!;
        if (Scene is null)
        {
            return false;
        }

        _hitResults.Clear();
        _hitTestEngine.HitTestPoint(Scene, SpatialIndex, worldPoint, tolerance, _hitResults);
        for (var index = 0; index < _hitResults.Count; index++)
        {
            var hit = _hitResults[index];
            var resolved = hit.OwnerEntity ?? hit.SourceEntity;
            if (resolved is not Entity candidate ||
                !TryResolveSessionEntity(session, candidate, out var canonicalEntity) ||
                !IsEntitySelected(session, canonicalEntity))
            {
                continue;
            }

            selectedEntity = canonicalEntity;
            return true;
        }

        return false;
    }

    private void HandleToolInput(CadRenderHitTestRequest request, ICadEditorSession? session)
    {
        var kind = request.Kind == CadHitTestKind.Hover
            ? CadToolInput.Hover(request)
            : CadToolInput.Select(request);
        _toolManager.HandleInput(kind, BuildToolContext());
        if (request.Kind != CadHitTestKind.Hover)
        {
            NormalizeSelectionForSession(session);
        }
    }

    private CadToolContext BuildToolContext()
    {
        return new CadToolContext(Scene, SpatialIndex, _selectionService, _annotationService);
    }

    private async ValueTask<string?> TryHandleShortcutAsync(
        CadInteractionEvent interactionEvent,
        ICadEditorSession? session,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(interactionEvent.Key))
        {
            return null;
        }

        var commandActive = _commandRuntime.State.IsActive;
        if (!TryResolveShortcutBinding(interactionEvent, commandActive, out var binding))
        {
            return null;
        }

        switch (binding.Action)
        {
            case CadShortcutActionKind.SelectAll:
                return TrySelectAll(session, out var selectionStatus) ? selectionStatus : null;
            case CadShortcutActionKind.ArmSelectionMode:
            {
                if (!TryResolveSelectionDragMode(binding.SelectionMode, out var mode, out var modeStatus))
                {
                    return null;
                }

                _armedSelectionDragMode = mode;
                return modeStatus;
            }
            case CadShortcutActionKind.CycleSelection:
                return TryCycleSelectionShortcut(session, interactionEvent, out var cycleStatus)
                    ? cycleStatus
                    : null;
            case CadShortcutActionKind.Command:
            default:
            {
                if (string.IsNullOrWhiteSpace(binding.CommandName))
                {
                    return null;
                }

                return await ExecuteShortcutCommandAsync(
                        binding.CommandName!,
                        session,
                        cancellationToken,
                        binding.TransparentWhenCommandActive)
                    .ConfigureAwait(false);
            }
        }
    }

    private bool TryResolveShortcutBinding(
        CadInteractionEvent interactionEvent,
        bool commandActive,
        out CadShortcutBinding binding)
    {
        binding = null!;
        var found = false;
        var bestSpecificity = int.MinValue;
        var bestPriority = int.MinValue;
        var bestTransparencyRank = int.MinValue;
        for (var index = 0; index < _shortcutBindings.Count; index++)
        {
            var candidate = _shortcutBindings[index];
            if (!candidate.Gesture.Matches(interactionEvent) ||
                !IsShortcutScopeMatch(candidate.Scope, commandActive))
            {
                continue;
            }

            var specificity = candidate.Scope == CadShortcutScope.Always ? 1 : 2;
            var transparencyRank =
                commandActive &&
                candidate.Action == CadShortcutActionKind.Command &&
                !candidate.TransparentWhenCommandActive
                    ? 1
                    : 0;
            if (!found ||
                specificity > bestSpecificity ||
                (specificity == bestSpecificity && candidate.Priority > bestPriority) ||
                (specificity == bestSpecificity &&
                 candidate.Priority == bestPriority &&
                 transparencyRank > bestTransparencyRank))
            {
                found = true;
                binding = candidate;
                bestSpecificity = specificity;
                bestPriority = candidate.Priority;
                bestTransparencyRank = transparencyRank;
            }
        }

        return found;
    }

    private bool TryCycleSelectionShortcut(
        ICadEditorSession? session,
        CadInteractionEvent interactionEvent,
        out string status)
    {
        status = string.Empty;
        var pickPoint = _lastPointerWorldPoint ?? interactionEvent.WorldPoint;
        var pickTolerance = interactionEvent.Tolerance > 0f
            ? interactionEvent.Tolerance
            : 4f;
        if (!TryResolveEntityHit(
                pickPoint,
                pickTolerance,
                session,
                out var candidate,
                out var cycleStatus) ||
            candidate is null)
        {
            status = "No object under cursor.";
            return true;
        }

        if (_selectionService.SelectedObjects.Count > 1 &&
            IsEntitySelected(session, candidate))
        {
            _selectionService.SetPrimarySelection(candidate);
        }
        else
        {
            _selectionService.ApplySelection([candidate], CadSelectionMode.Replace);
        }

        NormalizeSelectionForSession(session);
        UpdateSelectionAnnotationFromService(session);
        RebuildGripSet(session);
        status = cycleStatus ?? "Selection cycled.";
        return true;
    }

    private bool TryHandleBufferNavigation(CadInteractionEvent interactionEvent, out string status)
    {
        status = string.Empty;
        if (string.IsNullOrWhiteSpace(interactionEvent.Key))
        {
            return false;
        }

        if (string.Equals(interactionEvent.Key, "Back", StringComparison.OrdinalIgnoreCase))
        {
            if (_keyboardInputBuffer.Length == 0)
            {
                return false;
            }

            _keyboardInputBuffer = _keyboardInputBuffer[..^1];
            ResetCompletionCycle();
            RefreshRuntimePreviewForKeyboardInput();
            status = string.IsNullOrWhiteSpace(_keyboardInputBuffer) ? "Input cleared." : $"Input: {_keyboardInputBuffer}";
            return true;
        }

        if (string.Equals(interactionEvent.Key, "Tab", StringComparison.OrdinalIgnoreCase) &&
            !interactionEvent.Modifiers.HasFlag(CadInteractionModifiers.Control) &&
            !interactionEvent.Modifiers.HasFlag(CadInteractionModifiers.Alt))
        {
            if (TryCycleKeyboardCompletion(
                    forward: !interactionEvent.Modifiers.HasFlag(CadInteractionModifiers.Shift),
                    out status))
            {
                return true;
            }

            status = _commandRuntime.State.ParameterHelp ?? "No completion candidates.";
            return true;
        }

        return false;
    }

    private bool AppendKeyboardInput(string? rawText, out string status)
    {
        status = string.Empty;
        if (string.IsNullOrEmpty(rawText))
        {
            return false;
        }

        var buffer = new System.Text.StringBuilder(rawText.Length);
        for (var index = 0; index < rawText.Length; index++)
        {
            var ch = rawText[index];
            if (char.IsControl(ch) || ch == '\r' || ch == '\n')
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            buffer.Append(ch);
        }

        if (buffer.Length == 0)
        {
            return false;
        }

        _keyboardInputBuffer += buffer.ToString();
        ResetCompletionCycle();
        RefreshRuntimePreviewForKeyboardInput();
        status = $"Input: {_keyboardInputBuffer}";
        return true;
    }

    private async ValueTask<string> CommitKeyboardInputAsync(
        ICadEditorSession? session,
        CancellationToken cancellationToken)
    {
        var trimmed = _keyboardInputBuffer.Trim();
        if (trimmed.Length == 0)
        {
            ResetKeyboardInput();
            if (!_commandRuntime.State.IsActive)
            {
                var repeat = await _commandRuntime.SubmitAsync(string.Empty, session, cancellationToken).ConfigureAwait(false);
                if (repeat.Result?.Operations is { Count: > 0 } repeatOperations)
                {
                    OperationsCommitted?.Invoke(this, repeatOperations);
                }

                return repeat.Result?.Message ?? repeat.State.LastMessage ?? "Command is empty.";
            }

            var commitResolution = await SubmitInteractiveAsync(
                    new CadPromptToken(CadPromptTokenType.Raw, string.Empty),
                    session,
                    commit: true,
                    cancellationToken)
                .ConfigureAwait(false);

            return commitResolution.Result?.Message ??
                   commitResolution.State.LastMessage ??
                   commitResolution.State.ParameterHelp ??
                   "Command finished.";
        }

        var runtimeState = _commandRuntime.State;
        ResetKeyboardInput();
        if (!ContainsWhitespace(trimmed) &&
            _interactiveAdapters.TryGet(trimmed, out _) &&
            (!runtimeState.IsActive ||
             string.Equals(runtimeState.ActiveCommand, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            _commandRuntime.BeginCommand(trimmed);
            return _commandRuntime.State.LastMessage ??
                   _commandRuntime.State.ParameterHelp ??
                   string.Create(CultureInfo.InvariantCulture, $"Started {trimmed.ToUpperInvariant()}.");
        }

        if (runtimeState.IsActive &&
            !ContainsWhitespace(trimmed) &&
            string.Equals(runtimeState.ActiveCommand, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            var directResolution = await _commandRuntime.SubmitAsync(trimmed, session, cancellationToken).ConfigureAwait(false);
            if (directResolution.Result?.Operations is { Count: > 0 } directOperations)
            {
                OperationsCommitted?.Invoke(this, directOperations);
            }

            return directResolution.Result?.Message ?? directResolution.State.LastMessage ?? trimmed;
        }

        if (runtimeState.IsActive)
        {
            var token = BuildPromptToken(trimmed);
            var resolution = await SubmitInteractiveAsync(token, session, commit: false, cancellationToken)
                .ConfigureAwait(false);
            return resolution.Result?.Message ?? resolution.State.LastMessage ?? resolution.State.ParameterHelp ?? trimmed;
        }

        var commandResolution = await _commandRuntime.SubmitAsync(trimmed, session, cancellationToken).ConfigureAwait(false);
        if (commandResolution.Result?.Operations is { Count: > 0 } commandOperations)
        {
            OperationsCommitted?.Invoke(this, commandOperations);
        }

        return commandResolution.Result?.Message ?? commandResolution.State.LastMessage ?? trimmed;
    }

    private async ValueTask<string> ExecuteShortcutCommandAsync(
        string command,
        ICadEditorSession? session,
        CancellationToken cancellationToken,
        bool transparentWhenCommandActive = true)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "Command is empty.";
        }

        ResetKeyboardInput();
        if (_commandRuntime.State.IsActive && !transparentWhenCommandActive)
        {
            _commandRuntime.BeginCommand(command);
            return _commandRuntime.State.LastMessage ??
                   _commandRuntime.State.ParameterHelp ??
                   string.Create(CultureInfo.InvariantCulture, $"Started {command.ToUpperInvariant()}.");
        }

        var input = _commandRuntime.State.IsActive
            ? string.Create(CultureInfo.InvariantCulture, $"'{command}")
            : command;
        var resolution = await _commandRuntime.SubmitAsync(input, session, cancellationToken).ConfigureAwait(false);
        if (resolution.Result?.Operations is { Count: > 0 } operations)
        {
            OperationsCommitted?.Invoke(this, operations);
        }

        return resolution.Result?.Message ?? resolution.State.LastMessage ?? command;
    }

    private static bool IsShortcutScopeMatch(CadShortcutScope scope, bool commandActive)
    {
        return scope switch
        {
            CadShortcutScope.Always => true,
            CadShortcutScope.CommandInactiveOnly => !commandActive,
            CadShortcutScope.CommandActiveOnly => commandActive,
            _ => true
        };
    }

    private static bool TryResolveSelectionDragMode(
        CadSelectionShortcutMode? mode,
        out CadSelectionDragMode dragMode,
        out string status)
    {
        dragMode = CadSelectionDragMode.Rectangle;
        status = string.Empty;
        if (!mode.HasValue)
        {
            return false;
        }

        switch (mode.Value)
        {
            case CadSelectionShortcutMode.Lasso:
                dragMode = CadSelectionDragMode.Lasso;
                status = "Lasso selection armed.";
                return true;
            case CadSelectionShortcutMode.Fence:
                dragMode = CadSelectionDragMode.Fence;
                status = "Fence selection armed.";
                return true;
            case CadSelectionShortcutMode.Polygon:
                dragMode = CadSelectionDragMode.Polygon;
                status = "Polygon selection armed.";
                return true;
            default:
                return false;
        }
    }

    private bool TrySelectAll(ICadEditorSession? session, out string status)
    {
        status = string.Empty;
        if (Scene is null)
        {
            status = "No scene available.";
            return true;
        }

        var selected = new HashSet<Entity>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        foreach (var layer in Scene.Layers)
        {
            if (!layer.IsVisible)
            {
                continue;
            }

            foreach (var primitive in layer.Primitives)
            {
                if (!Scene.PrimitiveMetadata.TryGetValue(primitive, out var metadata))
                {
                    continue;
                }

                var entity = metadata.OwnerEntity ?? metadata.SourceEntity;
                if (entity is null ||
                    !TryResolveSessionEntity(session, entity, out var canonicalEntity))
                {
                    continue;
                }

                selected.Add(canonicalEntity);
            }
        }

        if (selected.Count == 0)
        {
            _selectionService.ClearSelection();
            _annotationService.UpdateSelection(null, null);
            status = "No selectable objects.";
            return true;
        }

        _selectionService.ApplySelection(selected.Cast<object>(), CadSelectionMode.Replace);
        UpdateSelectionAnnotationFromService(session);
        RebuildGripSet(session);
        status = string.Create(CultureInfo.InvariantCulture, $"Selected {selected.Count} object(s).");
        return true;
    }

    private bool TryCycleKeyboardCompletion(bool forward, out string status)
    {
        status = string.Empty;
        if (_keyboardCompletionItems.Count == 0)
        {
            var previewInput = BuildPreviewInputForKeyboard();
            var state = _commandRuntime.Preview(previewInput, previewInput.Length);
            _keyboardCompletionItems = state.Completions;
            _keyboardCompletionSeed = _keyboardInputBuffer;
            _keyboardCompletionIndex = -1;
        }

        if (_keyboardCompletionItems.Count == 0)
        {
            return false;
        }

        if (_keyboardCompletionIndex < 0 || _keyboardCompletionIndex >= _keyboardCompletionItems.Count)
        {
            _keyboardCompletionIndex = forward ? 0 : _keyboardCompletionItems.Count - 1;
        }
        else
        {
            _keyboardCompletionIndex = forward
                ? (_keyboardCompletionIndex + 1) % _keyboardCompletionItems.Count
                : (_keyboardCompletionIndex - 1 + _keyboardCompletionItems.Count) % _keyboardCompletionItems.Count;
        }

        var completion = _keyboardCompletionItems[_keyboardCompletionIndex];
        _keyboardInputBuffer = ReplaceLastToken(_keyboardCompletionSeed, completion.Value, completion.Kind);
        RefreshRuntimePreviewForKeyboardInput();
        status = string.Create(CultureInfo.InvariantCulture, $"Input: {_keyboardInputBuffer}");
        return true;
    }

    private void RefreshRuntimePreviewForKeyboardInput()
    {
        var previewInput = BuildPreviewInputForKeyboard();
        _commandRuntime.Preview(previewInput, previewInput.Length);
    }

    private string BuildPreviewInputForKeyboard()
    {
        if (_commandRuntime.State.IsActive &&
            !string.IsNullOrWhiteSpace(_commandRuntime.State.ActiveCommand))
        {
            if (string.IsNullOrWhiteSpace(_keyboardInputBuffer))
            {
                return _commandRuntime.State.ActiveCommand!;
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{_commandRuntime.State.ActiveCommand} {_keyboardInputBuffer}");
        }

        return _keyboardInputBuffer;
    }

    private void ResetKeyboardInput()
    {
        _keyboardInputBuffer = string.Empty;
        ResetCompletionCycle();
    }

    private void ResetCompletionCycle()
    {
        _keyboardCompletionItems = Array.Empty<CadCommandCompletionItem>();
        _keyboardCompletionIndex = -1;
        _keyboardCompletionSeed = string.Empty;
    }

    private static bool ContainsWhitespace(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsWhiteSpace(value[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReplaceLastToken(string input, string replacement, string kind)
    {
        input ??= string.Empty;
        replacement ??= string.Empty;

        var trimmedEnd = input.TrimEnd();
        if (trimmedEnd.Length == 0)
        {
            return replacement + " ";
        }

        var separatorIndex = trimmedEnd.LastIndexOfAny([' ', '\t']);
        if (separatorIndex < 0)
        {
            return replacement + " ";
        }

        var prefix = trimmedEnd[..(separatorIndex + 1)];
        var suffix = kind is "Command" or "Alias" ? " " : string.Empty;
        return prefix + replacement + suffix;
    }

    private static bool IsCommitKey(string? key)
    {
        return string.Equals(key, "Enter", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, "Return", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, "Space", StringComparison.OrdinalIgnoreCase);
    }

    private void AppendActiveCommandHelperHints(
        ICollection<CadToolVisualHint> hints,
        CadPromptState state,
        Vector2 anchor,
        ICadEditorSession? session)
    {
        if (!state.IsActive)
        {
            return;
        }

        if (!ContainsHintKind(hints, "Cursor"))
        {
            hints.Add(new CadToolVisualHint(
                Kind: "Cursor",
                Anchor: anchor,
                SecondaryAnchor: null,
                Text: state.ActiveCommand));
        }

        if (string.IsNullOrWhiteSpace(state.ActiveCommand))
        {
            return;
        }

        var requiredSelectionCount = GetRequiredSelectionCount(state.ActiveCommand);
        if (requiredSelectionCount <= 0)
        {
            if (!ContainsHintKind(hints, "PickPoint") &&
                !ContainsHintKind(hints, "RubberBand") &&
                !ContainsHintKind(hints, "PreviewCircle") &&
                !ContainsHintKind(hints, "PreviewArc"))
            {
                hints.Add(new CadToolVisualHint(
                    Kind: "PickPoint",
                    Anchor: anchor,
                    SecondaryAnchor: null,
                    Text: "Specify point"));
            }

            return;
        }

        var selectedCount = GetSessionSelectionCount(session);
        if (selectedCount >= requiredSelectionCount)
        {
            return;
        }

        var text = requiredSelectionCount == 1
            ? "Select object"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Select objects ({selectedCount}/{requiredSelectionCount})");
        hints.Add(new CadToolVisualHint(
            Kind: "Prompt",
            Anchor: anchor,
            SecondaryAnchor: null,
            Text: text));

        if (TryGetSelectionBounds(session, out var selectionBounds))
        {
            hints.Add(new CadToolVisualHint(
                Kind: "SelectionBounds",
                Anchor: selectionBounds.Min,
                SecondaryAnchor: selectionBounds.Max,
                Text: "Current selection"));
        }
    }

    private static bool ContainsHintKind(ICollection<CadToolVisualHint> hints, string kind)
    {
        foreach (var hint in hints)
        {
            if (string.Equals(hint.Kind, kind, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendTokenCallout(
        ICollection<CadToolVisualHint> hints,
        Vector2 anchor,
        CadPromptState state)
    {
        if (!state.IsActive || string.IsNullOrWhiteSpace(state.ParameterHelp))
        {
            return;
        }

        var index = Math.Max(0, state.ActiveParameterIndex) + 1;
        hints.Add(new CadToolVisualHint(
            Kind: "TokenCallout",
            Anchor: anchor,
            SecondaryAnchor: null,
            Text: string.Create(CultureInfo.InvariantCulture, $"[{index}] {state.ParameterHelp}"),
            Color: "#D8F4FF"));
    }

    private static void AppendDynamicDimensionHints(ICollection<CadToolVisualHint> hints)
    {
        CadToolVisualHint? sourceHint = null;
        foreach (var hint in hints)
        {
            if (!hint.SecondaryAnchor.HasValue)
            {
                continue;
            }

            if (string.Equals(hint.Kind, "RubberBand", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(hint.Kind, "HelperLine", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(hint.Kind, "TrackingGuide", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(hint.Kind, "SnapGuide", StringComparison.OrdinalIgnoreCase))
            {
                sourceHint = hint;
            }
        }

        if (sourceHint is null)
        {
            return;
        }

        var source = sourceHint;
        var end = source.SecondaryAnchor!.Value;
        var delta = end - source.Anchor;
        var length = delta.Length();
        if (length <= 1e-6f)
        {
            return;
        }

        var angle = NormalizeDegrees(MathF.Atan2(delta.Y, delta.X) * (180f / MathF.PI));
        var midpoint = source.Anchor + (delta * 0.5f);
        hints.Add(new CadToolVisualHint(
            Kind: "DynamicDimension",
            Anchor: midpoint,
            SecondaryAnchor: end,
            Text: string.Create(CultureInfo.InvariantCulture, $"{length:0.###} < {angle:0.#}°"),
            Color: "#9FE6FF"));
    }

    private CadToolVisualSnapshot BuildSnapshot(Vector2 anchor, bool handled, string? status)
    {
        var state = _commandRuntime.State;
        var hints = new List<CadToolVisualHint>();
        if (!string.IsNullOrWhiteSpace(_keyboardInputBuffer))
        {
            hints.Add(new CadToolVisualHint(
                Kind: "Prompt",
                Anchor: anchor,
                SecondaryAnchor: null,
                Text: $"Input: {_keyboardInputBuffer}"));
        }

        if (state.IsActive &&
            !string.IsNullOrWhiteSpace(state.ParameterHelp))
        {
            hints.Add(new CadToolVisualHint(
                Kind: "Prompt",
                Anchor: anchor,
                SecondaryAnchor: null,
                Text: state.ParameterHelp));
        }

        AppendSnapAndTrackingHints(hints, anchor);
        AppendActiveCommandHelperHints(hints, state, anchor, _currentSession);
        AppendGripHints(hints);
        AppendDynamicDimensionHints(hints);
        AppendTokenCallout(hints, anchor, state);
        return new CadToolVisualSnapshot(
            Handled: handled,
            Prompt: state.Prompt,
            Status: status ?? (!string.IsNullOrWhiteSpace(_keyboardInputBuffer) ? $"Input: {_keyboardInputBuffer}" : null),
            Hints: hints.Count == 0 ? Array.Empty<CadToolVisualHint>() : hints);
    }

    private CadToolVisualSnapshot BuildGripDragSnapshot(Vector2 anchor, bool handled, string? status)
    {
        var state = _commandRuntime.State;
        var hints = new List<CadToolVisualHint>();
        AppendGripHints(hints, includeAll: true);
        AppendSnapAndTrackingHints(hints, anchor);
        AppendActiveCommandHelperHints(hints, state, anchor, _currentSession);
        if (_dragGrip is { } grip)
        {
            var delta = anchor - grip.Position;
            hints.Add(new CadToolVisualHint(
                Kind: "RubberBand",
                Anchor: grip.Position,
                SecondaryAnchor: anchor,
                Text: string.Create(
                    CultureInfo.InvariantCulture,
                    $"{delta.X:0.###},{delta.Y:0.###}")));
        }

        AppendDynamicDimensionHints(hints);
        AppendTokenCallout(hints, anchor, state);
        return new CadToolVisualSnapshot(
            Handled: handled,
            Prompt: state.IsActive ? state.Prompt : "Grip Edit",
            Status: status,
            Hints: hints);
    }

    private CadToolVisualSnapshot BuildSnapshotWithInteractivePreview(
        ICadEditorSession? session,
        Vector2 anchor,
        bool handled,
        string? status)
    {
        var state = _commandRuntime.State;
        if (TryGetInteractivePreview(session, anchor, out var preview))
        {
            var hints = new List<CadToolVisualHint>(preview.Hints.Count + 8);
            if (!string.IsNullOrWhiteSpace(_keyboardInputBuffer))
            {
                hints.Add(new CadToolVisualHint(
                    Kind: "Prompt",
                    Anchor: anchor,
                    SecondaryAnchor: null,
                    Text: $"Input: {_keyboardInputBuffer}"));
            }

            hints.AddRange(preview.Hints);
            AppendSnapAndTrackingHints(hints, anchor);
            AppendActiveCommandHelperHints(hints, state, anchor, session);
            AppendGripHints(hints);
            AppendDynamicDimensionHints(hints);
            AppendTokenCallout(hints, anchor, state);
            return new CadToolVisualSnapshot(
                Handled: handled,
                Prompt: preview.Prompt ?? state.Prompt,
                Status: preview.Status ?? status ?? (!string.IsNullOrWhiteSpace(_keyboardInputBuffer) ? $"Input: {_keyboardInputBuffer}" : null),
                Hints: hints);
        }

        return BuildSnapshot(anchor, handled, status);
    }

    private CadToolVisualSnapshot BuildSelectionWindowSnapshot(Vector2 anchor, bool handled, string? status)
    {
        var hints = new List<CadToolVisualHint>();
        string selectionStatus;
        switch (_selectionDragMode)
        {
            case CadSelectionDragMode.Lasso:
            {
                AppendSelectionPathHints(hints, "SelectionLasso", closePath: true, label: "Lasso");
                selectionStatus = "Lasso selection";
                break;
            }
            case CadSelectionDragMode.Fence:
            {
                AppendSelectionPathHints(hints, "SelectionFence", closePath: false, label: "Fence");
                selectionStatus = "Fence selection";
                break;
            }
            case CadSelectionDragMode.Polygon:
            {
                AppendSelectionPathHints(hints, "SelectionPolygon", closePath: true, label: "Polygon");
                selectionStatus = "Polygon selection";
                break;
            }
            default:
            {
                var crossing = _selectionWindowCurrentPoint.X < _selectionWindowStartPoint.X;
                var kind = crossing ? "SelectionCrossing" : "SelectionWindow";
                hints.Add(new CadToolVisualHint(
                    Kind: kind,
                    Anchor: _selectionWindowStartPoint,
                    SecondaryAnchor: _selectionWindowCurrentPoint,
                    Text: crossing ? "Crossing" : "Window"));
                selectionStatus = crossing ? "Crossing selection" : "Window selection";
                break;
            }
        }

        AppendSnapAndTrackingHints(hints, anchor);
        var state = _commandRuntime.State;
        AppendActiveCommandHelperHints(hints, state, anchor, _currentSession);
        return new CadToolVisualSnapshot(
            Handled: handled,
            Prompt: state.Prompt,
            Status: status ?? selectionStatus,
            Hints: hints);
    }

    private void AppendSelectionPathHints(
        ICollection<CadToolVisualHint> hints,
        string kind,
        bool closePath,
        string label)
    {
        var points = GetSelectionPathPoints();
        if (points.Length < 2)
        {
            return;
        }

        for (var index = 1; index < points.Length; index++)
        {
            hints.Add(new CadToolVisualHint(
                Kind: kind,
                Anchor: points[index - 1],
                SecondaryAnchor: points[index],
                Text: index == 1 ? label : null));
        }

        if (closePath && points.Length > 2)
        {
            hints.Add(new CadToolVisualHint(
                Kind: kind,
                Anchor: points[^1],
                SecondaryAnchor: points[0],
                Text: null));
        }
    }

    private CadToolVisualSnapshot BuildEntityDragSnapshot(
        ICadEditorSession? session,
        Vector2 anchor,
        bool handled,
        string? status)
    {
        var state = _commandRuntime.State;
        var delta = _entityDragCurrentPoint - _entityDragStartPoint;
        var mode = _isEntityDragCopy ? "Copy" : "Move";
        var hints = new List<CadToolVisualHint>(8)
        {
            new(
                Kind: "RubberBand",
                Anchor: _entityDragStartPoint,
                SecondaryAnchor: _entityDragCurrentPoint,
                Text: string.Create(CultureInfo.InvariantCulture, $"{mode} {delta.X:0.###},{delta.Y:0.###}"))
        };

        if (TryGetSelectionBounds(session, out var selectionBounds))
        {
            var translatedMin = selectionBounds.Min + delta;
            var translatedMax = selectionBounds.Max + delta;
            hints.Add(new CadToolVisualHint(
                Kind: "SelectionBounds",
                Anchor: translatedMin,
                SecondaryAnchor: translatedMax,
                Text: _isEntityDragCopy ? "Copy Preview" : "Move Preview"));
        }

        AppendSnapAndTrackingHints(hints, anchor);
        AppendActiveCommandHelperHints(hints, state, anchor, session);
        AppendGripHints(hints);
        AppendDynamicDimensionHints(hints);
        AppendTokenCallout(hints, anchor, state);
        return new CadToolVisualSnapshot(
            Handled: handled,
            Prompt: state.Prompt,
            Status: status ?? (_isEntityDragCopy ? "Dragging copy preview." : "Dragging move preview."),
            Hints: hints);
    }

    private async ValueTask<CadPromptResolution> SubmitInteractiveAsync(
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit,
        CancellationToken cancellationToken)
    {
        CadPromptResolution resolution;
        if (TryGetActiveAdapter(out var adapter))
        {
            resolution = await adapter
                .SubmitAsync(_commandRuntime, token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            resolution = await _commandRuntime
                .SubmitTokenAsync(token, session, commit, cancellationToken)
                .ConfigureAwait(false);
        }

        if (resolution.Result?.Operations is { Count: > 0 } operations)
        {
            OperationsCommitted?.Invoke(this, operations);
        }

        return resolution;
    }

    private bool TryGetInteractivePreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        out CadInteractiveCommandPreview preview)
    {
        preview = default!;
        if (!TryGetActiveAdapter(out var adapter) ||
            adapter is not ICadInteractiveCommandPreviewProvider previewProvider)
        {
            return false;
        }

        return previewProvider.TryBuildPreview(
            session,
            cursorPoint,
            _commandRuntime.State.Prompt,
            _commandRuntime.State.LastMessage,
            out preview);
    }

    private bool TryGetActiveAdapter(out ICadInteractiveCommandAdapter adapter)
    {
        var command = _commandRuntime.State.ActiveCommand;
        if (string.IsNullOrWhiteSpace(command))
        {
            adapter = null!;
            return false;
        }

        return _interactiveAdapters.TryGet(command, out adapter!);
    }

    private CadPromptToken BuildPromptToken(string input)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new CadPromptToken(CadPromptTokenType.Text, string.Empty);
        }

        if (!trimmed.Contains(' ', StringComparison.Ordinal))
        {
            return new CadPromptToken(CadPromptTokenType.Keyword, trimmed);
        }

        return new CadPromptToken(CadPromptTokenType.Text, trimmed);
    }

    private static CadInputModifiers MapModifiers(CadInteractionModifiers modifiers)
    {
        var result = CadInputModifiers.None;
        if (modifiers.HasFlag(CadInteractionModifiers.Shift))
        {
            result |= CadInputModifiers.Shift;
        }
        if (modifiers.HasFlag(CadInteractionModifiers.Control))
        {
            result |= CadInputModifiers.Control;
        }
        if (modifiers.HasFlag(CadInteractionModifiers.Alt))
        {
            result |= CadInputModifiers.Alt;
        }

        return result;
    }

    private static string FormatCoordinate(Vector2 point)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{point.X:0.###},{point.Y:0.###}");
    }

    private Vector2 ResolvePoint(Vector2 worldPoint, float tolerance, out string? snapStatus)
    {
        var resolved = worldPoint;
        _lastPointerWorldPoint = worldPoint;
        _activeSnapResult = null;
        snapStatus = null;
        if (Scene is not null && tolerance > 0f)
        {
            _hitResults.Clear();
            _hitTestEngine.HitTestPoint(Scene, SpatialIndex, worldPoint, tolerance, _hitResults);
            if (_hitResults.Count > 0)
            {
                _snapCandidates.Clear();
                BuildSnapCandidates(worldPoint, _hitResults, _snapCandidates);
                if (_snapService.TryResolve(worldPoint, tolerance, _snapCandidates, out var snap))
                {
                    resolved = snap.Point;
                    snapStatus = snap.Label;
                    _activeSnapResult = snap;
                }
            }
        }

        if (_trackingBasePoint.HasValue)
        {
            var trackedPoint = _trackingService.Apply(_trackingBasePoint.Value, resolved);
            if (Vector2.DistanceSquared(trackedPoint, resolved) > 1e-8f)
            {
                snapStatus ??= "Tracking";
            }

            resolved = trackedPoint;
        }

        return resolved;
    }

    private bool TryResolveEntityHit(Vector2 worldPoint, float tolerance, ICadEditorSession? session)
    {
        return TryResolveEntityHit(worldPoint, tolerance, session, out _, out _);
    }

    private bool TryResolveEntityHit(
        Vector2 worldPoint,
        float tolerance,
        ICadEditorSession? session,
        out Entity? entity,
        out string? cycleStatus)
    {
        entity = null;
        cycleStatus = null;
        if (Scene is null)
        {
            return false;
        }

        var currentCandidates = new List<Entity>();
        _hitResults.Clear();
        _hitTestEngine.HitTestPoint(Scene, SpatialIndex, worldPoint, tolerance, _hitResults);
        for (var index = 0; index < _hitResults.Count; index++)
        {
            var hit = _hitResults[index];
            var resolved = hit.OwnerEntity ?? hit.SourceEntity;
            if (resolved is Entity hitEntity &&
                TryResolveSessionEntity(session, hitEntity, out var canonicalEntity))
            {
                if (!currentCandidates.Contains(canonicalEntity, System.Collections.Generic.ReferenceEqualityComparer.Instance))
                {
                    currentCandidates.Add(canonicalEntity);
                }
            }
        }

        if (currentCandidates.Count == 0)
        {
            ResetSelectionCycle();
            return false;
        }

        var cycleTolerance = MathF.Max(tolerance, 1e-4f);
        var canReuseCycle = _selectionCyclePoint.HasValue &&
                            Vector2.DistanceSquared(_selectionCyclePoint.Value, worldPoint) <= cycleTolerance * cycleTolerance &&
                            HaveSameSelectionCandidates(_selectionCycleCandidates, currentCandidates);

        _selectionCycleCandidates.Clear();
        _selectionCycleCandidates.AddRange(currentCandidates);

        if (!canReuseCycle ||
            _selectionCycleIndex < 0 ||
            _selectionCycleIndex >= _selectionCycleCandidates.Count)
        {
            _selectionCycleIndex = 0;
        }
        else
        {
            _selectionCycleIndex = (_selectionCycleIndex + 1) % _selectionCycleCandidates.Count;
        }

        _selectionCyclePoint = worldPoint;
        entity = _selectionCycleCandidates[_selectionCycleIndex];
        if (_selectionCycleCandidates.Count > 1)
        {
            cycleStatus = string.Create(
                CultureInfo.InvariantCulture,
                $"Selection cycle {_selectionCycleIndex + 1}/{_selectionCycleCandidates.Count}");
        }

        return true;
    }

    private void BeginSelectionWindow(Vector2 startPoint, CadInputModifiers modifiers)
    {
        ResetEntityDragState();
        _isSelectionWindowCandidate = true;
        _isSelectionWindowDragging = false;
        _selectionWindowStartPoint = startPoint;
        _selectionWindowCurrentPoint = startPoint;
        _selectionWindowModifiers = modifiers;
        _selectionDragMode = ResolveSelectionDragMode(modifiers, _armedSelectionDragMode);
        _armedSelectionDragMode = null;
        _selectionPathPoints.Clear();
        _selectionPathPoints.Add(startPoint);
    }

    private void AppendSelectionPathPoint(Vector2 point, float tolerance)
    {
        if (_selectionDragMode == CadSelectionDragMode.Rectangle)
        {
            return;
        }

        if (_selectionPathPoints.Count == 0)
        {
            _selectionPathPoints.Add(point);
            return;
        }

        var minStep = MathF.Max(tolerance * 0.5f, 1e-4f);
        var previous = _selectionPathPoints[^1];
        if (Vector2.DistanceSquared(previous, point) >= minStep * minStep)
        {
            _selectionPathPoints.Add(point);
        }
        else
        {
            _selectionPathPoints[^1] = point;
        }
    }

    private static CadSelectionDragMode ResolveSelectionDragMode(
        CadInputModifiers modifiers,
        CadSelectionDragMode? armedMode)
    {
        if (armedMode.HasValue)
        {
            return armedMode.Value;
        }

        if (modifiers.HasFlag(CadInputModifiers.Alt))
        {
            if (modifiers.HasFlag(CadInputModifiers.Shift))
            {
                return CadSelectionDragMode.Fence;
            }

            if (modifiers.HasFlag(CadInputModifiers.Control))
            {
                return CadSelectionDragMode.Polygon;
            }

            return CadSelectionDragMode.Lasso;
        }

        return CadSelectionDragMode.Rectangle;
    }

    private void BeginEntityDrag(Vector2 startPoint)
    {
        _isEntityDragCandidate = true;
        _isEntityDragging = false;
        _isEntityDragCopy = false;
        _entityDragStartPoint = startPoint;
        _entityDragCurrentPoint = startPoint;
    }

    private void ResetSelectionWindowState()
    {
        _isSelectionWindowCandidate = false;
        _isSelectionWindowDragging = false;
        _selectionWindowStartPoint = default;
        _selectionWindowCurrentPoint = default;
        _selectionWindowModifiers = CadInputModifiers.None;
        _selectionDragMode = CadSelectionDragMode.Rectangle;
        _selectionPathPoints.Clear();
    }

    private void ResetSelectionCycle()
    {
        _selectionCyclePoint = null;
        _selectionCycleCandidates.Clear();
        _selectionCycleIndex = -1;
    }

    private static bool HaveSameSelectionCandidates(
        IReadOnlyList<Entity> previous,
        IReadOnlyList<Entity> current)
    {
        if (previous.Count != current.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            if (!ReferenceEquals(previous[index], current[index]))
            {
                return false;
            }
        }

        return true;
    }

    private void ResetEntityDragState()
    {
        _isEntityDragCandidate = false;
        _isEntityDragging = false;
        _isEntityDragCopy = false;
        _entityDragStartPoint = default;
        _entityDragCurrentPoint = default;
    }

    private string ApplySelectionWindow(ICadEditorSession? session)
    {
        if (Scene is null)
        {
            return "No scene available for selection.";
        }

        var selectionName = "Window";
        var crossing = false;
        var pathPoints = Array.Empty<Vector2>();
        var polygonPoints = Array.Empty<Vector2>();
        RenderBounds bounds;
        switch (_selectionDragMode)
        {
            case CadSelectionDragMode.Lasso:
            {
                selectionName = "Lasso";
                pathPoints = GetSelectionPathPoints();
                if (pathPoints.Length < 3)
                {
                    return "Lasso selection canceled.";
                }

                polygonPoints = pathPoints;
                bounds = CreateBounds(pathPoints);
                break;
            }
            case CadSelectionDragMode.Fence:
            {
                selectionName = "Fence";
                pathPoints = GetSelectionPathPoints();
                if (pathPoints.Length < 2)
                {
                    return "Fence selection canceled.";
                }

                bounds = CreateBounds(pathPoints);
                break;
            }
            case CadSelectionDragMode.Polygon:
            {
                selectionName = "Polygon";
                pathPoints = GetSelectionPathPoints();
                if (pathPoints.Length < 3)
                {
                    return "Polygon selection canceled.";
                }

                polygonPoints = pathPoints;
                bounds = CreateBounds(pathPoints);
                break;
            }
            default:
            {
                crossing = _selectionWindowCurrentPoint.X < _selectionWindowStartPoint.X;
                selectionName = crossing ? "Crossing" : "Window";
                bounds = CreateBounds(_selectionWindowStartPoint, _selectionWindowCurrentPoint);
                break;
            }
        }

        if (bounds.IsEmpty)
        {
            return "Selection canceled.";
        }

        _hitResults.Clear();
        _hitTestEngine.HitTestBounds(
            Scene,
            SpatialIndex,
            bounds,
            _hitResults,
            new RenderHitTestOptions(
                includeHiddenLayers: false,
                maxResults: 0,
                sortByDistance: false));

        var entities = new HashSet<Entity>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        for (var index = 0; index < _hitResults.Count; index++)
        {
            var hit = _hitResults[index];
            var entity = hit.OwnerEntity ?? hit.SourceEntity;
            if (entity is null)
            {
                continue;
            }

            var include = _selectionDragMode switch
            {
                CadSelectionDragMode.Rectangle => crossing || ContainsBounds(bounds, hit.Bounds),
                CadSelectionDragMode.Lasso => PolygonIntersectsBounds(polygonPoints, hit.Bounds),
                CadSelectionDragMode.Fence => PathIntersectsBounds(pathPoints, hit.Bounds),
                CadSelectionDragMode.Polygon => PolygonContainsBounds(polygonPoints, hit.Bounds),
                _ => false
            };
            if (!include)
            {
                continue;
            }

            if (!TryResolveSessionEntity(session, entity, out var canonicalEntity))
            {
                continue;
            }

            entities.Add(canonicalEntity);
        }

        var mode = ResolveSelectionMode(_selectionWindowModifiers);
        if (entities.Count > 0)
        {
            _selectionService.ApplySelection(entities.Cast<object>(), mode);
        }
        else if (mode == CadSelectionMode.Replace)
        {
            _selectionService.ClearSelection();
        }

        UpdateSelectionAnnotationFromService(session);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{selectionName} selected {entities.Count} object{(entities.Count == 1 ? string.Empty : "s")}.");
    }

    private Vector2[] GetSelectionPathPoints()
    {
        if (_selectionPathPoints.Count > 1)
        {
            return _selectionPathPoints.ToArray();
        }

        if (Vector2.DistanceSquared(_selectionWindowStartPoint, _selectionWindowCurrentPoint) > 1e-8f)
        {
            return [_selectionWindowStartPoint, _selectionWindowCurrentPoint];
        }

        return [_selectionWindowStartPoint];
    }

    private static bool PathIntersectsBounds(IReadOnlyList<Vector2> path, RenderBounds bounds)
    {
        if (path.Count < 2 || bounds.IsEmpty)
        {
            return false;
        }

        for (var index = 1; index < path.Count; index++)
        {
            if (SegmentIntersectsBounds(path[index - 1], path[index], bounds))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PolygonContainsBounds(IReadOnlyList<Vector2> polygon, RenderBounds bounds)
    {
        if (polygon.Count < 3 || bounds.IsEmpty)
        {
            return false;
        }

        Span<Vector2> corners = stackalloc Vector2[4];
        GetBoundsCorners(bounds, corners);
        for (var index = 0; index < corners.Length; index++)
        {
            if (!PolygonContainsPoint(polygon, corners[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PolygonIntersectsBounds(IReadOnlyList<Vector2> polygon, RenderBounds bounds)
    {
        if (polygon.Count < 3 || bounds.IsEmpty)
        {
            return false;
        }

        Span<Vector2> corners = stackalloc Vector2[4];
        GetBoundsCorners(bounds, corners);
        for (var index = 0; index < corners.Length; index++)
        {
            if (PolygonContainsPoint(polygon, corners[index]))
            {
                return true;
            }
        }

        for (var index = 0; index < polygon.Count; index++)
        {
            if (bounds.Contains(polygon[index]))
            {
                return true;
            }
        }

        for (var index = 0; index < polygon.Count; index++)
        {
            var start = polygon[index];
            var end = polygon[(index + 1) % polygon.Count];
            if (SegmentIntersectsBounds(start, end, bounds))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SegmentIntersectsBounds(Vector2 start, Vector2 end, RenderBounds bounds)
    {
        if (bounds.Contains(start) || bounds.Contains(end))
        {
            return true;
        }

        var topLeft = bounds.Min;
        var topRight = new Vector2(bounds.MaxX, bounds.MinY);
        var bottomRight = bounds.Max;
        var bottomLeft = new Vector2(bounds.MinX, bounds.MaxY);
        return TryIntersectSegments(start, end, topLeft, topRight, out _) ||
               TryIntersectSegments(start, end, topRight, bottomRight, out _) ||
               TryIntersectSegments(start, end, bottomRight, bottomLeft, out _) ||
               TryIntersectSegments(start, end, bottomLeft, topLeft, out _);
    }

    private static bool PolygonContainsPoint(IReadOnlyList<Vector2> polygon, Vector2 point)
    {
        var contains = false;
        for (var index = 0; index < polygon.Count; index++)
        {
            var current = polygon[index];
            var previous = polygon[(index + polygon.Count - 1) % polygon.Count];
            var intersects = ((current.Y > point.Y) != (previous.Y > point.Y)) &&
                             (point.X < ((previous.X - current.X) * (point.Y - current.Y) / ((previous.Y - current.Y) + 1e-8f)) + current.X);
            if (intersects)
            {
                contains = !contains;
            }
        }

        return contains;
    }

    private static void GetBoundsCorners(RenderBounds bounds, Span<Vector2> corners)
    {
        corners[0] = bounds.Min;
        corners[1] = new Vector2(bounds.MaxX, bounds.MinY);
        corners[2] = bounds.Max;
        corners[3] = new Vector2(bounds.MinX, bounds.MaxY);
    }

    private static RenderBounds CreateBounds(IReadOnlyList<Vector2> points)
    {
        var bounds = RenderBounds.Empty;
        for (var index = 0; index < points.Count; index++)
        {
            bounds = bounds.Expand(points[index]);
        }

        return bounds;
    }

    private static CadSelectionMode ResolveSelectionMode(CadInputModifiers modifiers)
    {
        if (modifiers.HasFlag(CadInputModifiers.Control) &&
            modifiers.HasFlag(CadInputModifiers.Shift))
        {
            return CadSelectionMode.Remove;
        }

        if (modifiers.HasFlag(CadInputModifiers.Control))
        {
            return CadSelectionMode.Toggle;
        }

        if (modifiers.HasFlag(CadInputModifiers.Shift))
        {
            return CadSelectionMode.Add;
        }

        return CadSelectionMode.Replace;
    }

    private void NormalizeSelectionForSession(ICadEditorSession? session)
    {
        if (session is null || _selectionService.SelectedObjects.Count == 0)
        {
            return;
        }

        var normalized = new List<object>(_selectionService.SelectedObjects.Count);
        var changed = false;
        foreach (var item in _selectionService.SelectedObjects)
        {
            if (item is Entity entity)
            {
                if (!TryResolveSessionEntity(session, entity, out var canonicalEntity))
                {
                    changed = true;
                    continue;
                }

                if (!ReferenceEquals(entity, canonicalEntity))
                {
                    changed = true;
                }

                normalized.Add(canonicalEntity);
                continue;
            }

            normalized.Add(item);
        }

        if (!changed)
        {
            return;
        }

        if (normalized.Count == 0)
        {
            _selectionService.ClearSelection();
            _annotationService.UpdateSelection(null, null);
            return;
        }

        _selectionService.ApplySelection(normalized, CadSelectionMode.Replace);
    }

    private static bool TryResolveSessionEntity(
        ICadEditorSession? session,
        Entity entity,
        out Entity canonicalEntity)
    {
        canonicalEntity = entity;
        if (session is null)
        {
            return true;
        }

        if (session.EntityIndex.TryGetId(entity, out _))
        {
            canonicalEntity = entity;
            return true;
        }

        if (entity.Handle != 0 &&
            session.EntityIndex.TryGetByHandle(entity.Handle, out var byHandle, out _))
        {
            canonicalEntity = byHandle;
            return true;
        }

        return false;
    }

    private void UpdateSelectionAnnotationFromService(ICadEditorSession? session)
    {
        var selectedEntity = _selectionService.SelectedObjects.OfType<Entity>().FirstOrDefault();
        if (selectedEntity is null)
        {
            _annotationService.UpdateSelection(null, null);
            return;
        }

        RenderBounds? bounds = null;
        var useSceneGeometry = true;
        if (session is not null &&
            session.EntityIndex.TryGetId(selectedEntity, out var entityId) &&
            TryResolveEntityBounds(session, entityId, out var resolvedBounds))
        {
            bounds = resolvedBounds;
            useSceneGeometry = false;
        }
        else if (_annotationService.TryGetBounds(selectedEntity, out var annotationBounds))
        {
            bounds = annotationBounds;
        }

        _annotationService.UpdateSelection(selectedEntity, bounds, primitive: null, useSceneGeometry);
    }

    private static bool TryResolveEntityBounds(
        ICadEditorSession session,
        CadEntityId entityId,
        out RenderBounds bounds)
    {
        bounds = RenderBounds.Empty;
        if (!session.EntityIndex.TryGetEntity(entityId, out var entity))
        {
            return false;
        }

        var box = entity.GetBoundingBox();
        var min = new Vector2((float)box.Min.X, (float)box.Min.Y);
        var max = new Vector2((float)box.Max.X, (float)box.Max.Y);
        if (!float.IsFinite(min.X) ||
            !float.IsFinite(min.Y) ||
            !float.IsFinite(max.X) ||
            !float.IsFinite(max.Y))
        {
            return false;
        }

        bounds = CreateBounds(min, max);
        return !bounds.IsEmpty;
    }

    private static bool TryResolveEntityBounds(Entity entity, out RenderBounds bounds)
    {
        bounds = RenderBounds.Empty;
        var box = entity.GetBoundingBox();
        var min = new Vector2((float)box.Min.X, (float)box.Min.Y);
        var max = new Vector2((float)box.Max.X, (float)box.Max.Y);
        if (!float.IsFinite(min.X) ||
            !float.IsFinite(min.Y) ||
            !float.IsFinite(max.X) ||
            !float.IsFinite(max.Y))
        {
            return false;
        }

        bounds = CreateBounds(min, max);
        return !bounds.IsEmpty;
    }

    private static RenderBounds CreateBounds(Vector2 start, Vector2 end)
    {
        var min = new Vector2(MathF.Min(start.X, end.X), MathF.Min(start.Y, end.Y));
        var max = new Vector2(MathF.Max(start.X, end.X), MathF.Max(start.Y, end.Y));
        return new RenderBounds(min, max);
    }

    private static bool ContainsBounds(RenderBounds outer, RenderBounds inner)
    {
        if (outer.IsEmpty || inner.IsEmpty)
        {
            return false;
        }

        return inner.MinX >= outer.MinX &&
               inner.MinY >= outer.MinY &&
               inner.MaxX <= outer.MaxX &&
               inner.MaxY <= outer.MaxY;
    }

    private void AppendSnapAndTrackingHints(ICollection<CadToolVisualHint> hints, Vector2 anchor)
    {
        if (_activeSnapResult is { } snap)
        {
            hints.Add(new CadToolVisualHint(
                Kind: ResolveSnapHintKind(snap.Mode),
                Anchor: snap.Point,
                SecondaryAnchor: null,
                Text: snap.Label));

            if (_lastPointerWorldPoint is { } pointerPoint &&
                Vector2.DistanceSquared(pointerPoint, snap.Point) > 1e-6f)
            {
                hints.Add(new CadToolVisualHint(
                    Kind: "SnapGuide",
                    Anchor: pointerPoint,
                    SecondaryAnchor: snap.Point,
                    Text: snap.Label));
            }
        }

        if (_trackingBasePoint is { } basePoint &&
            Vector2.DistanceSquared(basePoint, anchor) > 1e-6f)
        {
            hints.Add(new CadToolVisualHint(
                Kind: "TrackingGuide",
                Anchor: basePoint,
                SecondaryAnchor: anchor,
                Text: null));
        }
    }

    private static string ResolveSnapHintKind(CadSnapMode mode)
    {
        return mode switch
        {
            CadSnapMode.Endpoint => "SnapEndpoint",
            CadSnapMode.Midpoint => "SnapMidpoint",
            CadSnapMode.Center => "SnapCenter",
            CadSnapMode.Node => "SnapNode",
            CadSnapMode.Quadrant => "SnapQuadrant",
            CadSnapMode.Intersection => "SnapIntersection",
            CadSnapMode.Perpendicular => "SnapPerpendicular",
            CadSnapMode.Tangent => "SnapTangent",
            CadSnapMode.Nearest => "SnapNearest",
            CadSnapMode.ApparentIntersection => "SnapApparentIntersection",
            CadSnapMode.Extension => "SnapExtension",
            CadSnapMode.Parallel => "SnapParallel",
            _ => "SnapMarker"
        };
    }

    private void BuildSnapCandidates(
        Vector2 pickPoint,
        IReadOnlyList<RenderHitTestResult> hits,
        List<CadSnapCandidate> candidates)
    {
        for (var index = 0; index < hits.Count; index++)
        {
            AppendPrimitiveSnapCandidates(hits[index].Primitive, pickPoint, candidates);
        }

        AppendIntersectionCandidates(hits, candidates);
    }

    private void AppendPrimitiveSnapCandidates(
        IRenderPrimitive primitive,
        Vector2 pickPoint,
        List<CadSnapCandidate> candidates)
    {
        switch (primitive)
        {
            case RenderLine line:
                AppendLineSnapCandidates(line.Start, line.End, pickPoint, candidates);
                break;
            case RenderPolyline polyline:
                AppendPolylineSnapCandidates(polyline, pickPoint, candidates);
                break;
            case RenderCircle circle:
                AppendCircleSnapCandidates(circle, pickPoint, candidates);
                break;
            case RenderArc arc:
                AppendArcSnapCandidates(arc, pickPoint, candidates);
                break;
            case RenderPoint point:
                AddCandidate(candidates, point.Point, CadSnapMode.Node, "NODE");
                AddCandidate(candidates, point.Point, CadSnapMode.Nearest, "NEAREST", priorityBias: 0.1f);
                break;
            case RenderText text:
                AddCandidate(candidates, text.Anchor, CadSnapMode.Node, "TEXT");
                AddCandidate(candidates, text.Bounds.Center, CadSnapMode.Nearest, "NEAREST", priorityBias: 0.3f);
                break;
            default:
                AddCandidate(candidates, primitive.Bounds.Center, CadSnapMode.Nearest, "NEAREST", priorityBias: 0.5f);
                break;
        }
    }

    private void AppendLineSnapCandidates(
        Vector2 start,
        Vector2 end,
        Vector2 pickPoint,
        List<CadSnapCandidate> candidates)
    {
        AddCandidate(candidates, start, CadSnapMode.Endpoint, "END");
        AddCandidate(candidates, end, CadSnapMode.Endpoint, "END");
        AddCandidate(candidates, (start + end) * 0.5f, CadSnapMode.Midpoint, "MID");

        var nearest = ClosestPointOnSegment(pickPoint, start, end, out var segmentParameter);
        AddCandidate(candidates, nearest, CadSnapMode.Nearest, "NEAREST", priorityBias: 0.2f);

        if (_trackingBasePoint.HasValue)
        {
            AppendDirectionalSnapCandidates(
                _trackingBasePoint.Value,
                pickPoint,
                start,
                end,
                segmentParameter,
                candidates);
        }
    }

    private void AppendPolylineSnapCandidates(
        RenderPolyline polyline,
        Vector2 pickPoint,
        List<CadSnapCandidate> candidates)
    {
        var points = polyline.Points;
        if (points.Count == 0)
        {
            return;
        }

        if (!polyline.IsClosed)
        {
            AddCandidate(candidates, points[0], CadSnapMode.Endpoint, "END");
            AddCandidate(candidates, points[^1], CadSnapMode.Endpoint, "END");
        }

        for (var index = 0; index < points.Count; index++)
        {
            AddCandidate(candidates, points[index], CadSnapMode.Node, "NODE");
        }

        var hasBest = false;
        var bestDistanceSquared = float.PositiveInfinity;
        Vector2 bestNearest = default;
        Vector2 bestStart = default;
        Vector2 bestEnd = default;
        float bestT = 0f;
        var segmentCount = polyline.IsClosed ? points.Count : points.Count - 1;
        for (var index = 0; index < segmentCount; index++)
        {
            var start = points[index];
            var end = points[(index + 1) % points.Count];
            AddCandidate(candidates, (start + end) * 0.5f, CadSnapMode.Midpoint, "MID");

            var nearest = ClosestPointOnSegment(pickPoint, start, end, out var t);
            var delta = nearest - pickPoint;
            var distanceSquared = Vector2.Dot(delta, delta);
            if (distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            hasBest = true;
            bestDistanceSquared = distanceSquared;
            bestNearest = nearest;
            bestStart = start;
            bestEnd = end;
            bestT = t;
        }

        if (!hasBest)
        {
            return;
        }

        AddCandidate(candidates, bestNearest, CadSnapMode.Nearest, "NEAREST", priorityBias: 0.2f);
        if (_trackingBasePoint.HasValue)
        {
            AppendDirectionalSnapCandidates(
                _trackingBasePoint.Value,
                pickPoint,
                bestStart,
                bestEnd,
                bestT,
                candidates);
        }
    }

    private void AppendCircleSnapCandidates(
        RenderCircle circle,
        Vector2 pickPoint,
        List<CadSnapCandidate> candidates)
    {
        var center = circle.Center;
        var radius = MathF.Max(0f, circle.Radius);
        if (radius <= float.Epsilon)
        {
            return;
        }

        AddCandidate(candidates, center, CadSnapMode.Center, "CEN");
        AddCandidate(candidates, center + new Vector2(radius, 0f), CadSnapMode.Quadrant, "QUAD");
        AddCandidate(candidates, center + new Vector2(-radius, 0f), CadSnapMode.Quadrant, "QUAD");
        AddCandidate(candidates, center + new Vector2(0f, radius), CadSnapMode.Quadrant, "QUAD");
        AddCandidate(candidates, center + new Vector2(0f, -radius), CadSnapMode.Quadrant, "QUAD");

        var direction = pickPoint - center;
        if (direction.LengthSquared() <= float.Epsilon)
        {
            direction = new Vector2(1f, 0f);
        }

        direction = Vector2.Normalize(direction);
        AddCandidate(candidates, center + direction * radius, CadSnapMode.Nearest, "NEAREST", priorityBias: 0.2f);

        if (_trackingBasePoint.HasValue)
        {
            var basePoint = _trackingBasePoint.Value;
            var fromCenter = basePoint - center;
            var distanceToCenter = fromCenter.Length();
            if (distanceToCenter > radius + 1e-4f)
            {
                var baseAngle = MathF.Atan2(fromCenter.Y, fromCenter.X);
                var alpha = MathF.Acos(radius / distanceToCenter);
                AddCandidate(candidates, center + Unit(baseAngle + alpha) * radius, CadSnapMode.Tangent, "TAN");
                AddCandidate(candidates, center + Unit(baseAngle - alpha) * radius, CadSnapMode.Tangent, "TAN");
            }

            if (fromCenter.LengthSquared() > float.Epsilon)
            {
                var perpendicular = center + Vector2.Normalize(fromCenter) * radius;
                AddCandidate(candidates, perpendicular, CadSnapMode.Perpendicular, "PER");
            }
        }
    }

    private void AppendArcSnapCandidates(
        RenderArc arc,
        Vector2 pickPoint,
        List<CadSnapCandidate> candidates)
    {
        var center = arc.Center;
        var radius = MathF.Max(0f, arc.Radius);
        if (radius <= float.Epsilon)
        {
            return;
        }

        AddCandidate(candidates, center, CadSnapMode.Center, "CEN");
        var start = PointAtAngle(center, radius, arc.StartAngle);
        var end = PointAtAngle(center, radius, arc.EndAngle);
        AddCandidate(candidates, start, CadSnapMode.Endpoint, "END");
        AddCandidate(candidates, end, CadSnapMode.Endpoint, "END");

        var midAngle = ComputeArcMidAngle(arc.StartAngle, arc.EndAngle);
        AddCandidate(candidates, PointAtAngle(center, radius, midAngle), CadSnapMode.Midpoint, "MID");

        var pickAngle = MathF.Atan2(pickPoint.Y - center.Y, pickPoint.X - center.X);
        var clamped = ClampAngleToArc(pickAngle, arc.StartAngle, arc.EndAngle);
        AddCandidate(candidates, PointAtAngle(center, radius, clamped), CadSnapMode.Nearest, "NEAREST", priorityBias: 0.2f);

        foreach (var quadrant in new[] { 0f, MathF.PI * 0.5f, MathF.PI, MathF.PI * 1.5f })
        {
            if (IsAngleOnArc(quadrant, arc.StartAngle, arc.EndAngle))
            {
                AddCandidate(candidates, PointAtAngle(center, radius, quadrant), CadSnapMode.Quadrant, "QUAD");
            }
        }
    }

    private static void AddCandidate(
        ICollection<CadSnapCandidate> candidates,
        Vector2 point,
        CadSnapMode mode,
        string label,
        float priorityBias = 0f)
    {
        if (float.IsNaN(point.X) || float.IsNaN(point.Y) || float.IsInfinity(point.X) || float.IsInfinity(point.Y))
        {
            return;
        }

        candidates.Add(new CadSnapCandidate(point, mode, label, priorityBias));
    }

    private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 start, Vector2 end, out float t)
    {
        var axis = end - start;
        var lengthSquared = axis.LengthSquared();
        if (lengthSquared <= float.Epsilon)
        {
            t = 0f;
            return start;
        }

        t = Vector2.Dot(point - start, axis) / lengthSquared;
        t = Math.Clamp(t, 0f, 1f);
        return start + axis * t;
    }

    private static Vector2 ClosestPointOnLine(Vector2 point, Vector2 start, Vector2 end, out float t)
    {
        var axis = end - start;
        var lengthSquared = axis.LengthSquared();
        if (lengthSquared <= float.Epsilon)
        {
            t = 0f;
            return start;
        }

        t = Vector2.Dot(point - start, axis) / lengthSquared;
        return start + axis * t;
    }

    private static Vector2 Unit(float angle)
    {
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    private static Vector2 PointAtAngle(Vector2 center, float radius, float angle)
    {
        return center + Unit(angle) * radius;
    }

    private static float NormalizeAngle(float angle)
    {
        const float twoPi = MathF.PI * 2f;
        angle %= twoPi;
        if (angle < 0f)
        {
            angle += twoPi;
        }

        return angle;
    }

    private static float NormalizeDegrees(float degrees)
    {
        const float fullTurn = 360f;
        degrees %= fullTurn;
        if (degrees < 0f)
        {
            degrees += fullTurn;
        }

        return degrees;
    }

    private static bool IsAngleOnArc(float test, float start, float end)
    {
        const float twoPi = MathF.PI * 2f;
        start = NormalizeAngle(start);
        end = NormalizeAngle(end);
        test = NormalizeAngle(test);

        if (end < start)
        {
            end += twoPi;
            if (test < start)
            {
                test += twoPi;
            }
        }

        return test >= start && test <= end;
    }

    private static float ClampAngleToArc(float test, float start, float end)
    {
        const float twoPi = MathF.PI * 2f;
        var normalizedStart = NormalizeAngle(start);
        var normalizedEnd = NormalizeAngle(end);
        var normalizedTest = NormalizeAngle(test);
        if (normalizedEnd < normalizedStart)
        {
            normalizedEnd += twoPi;
            if (normalizedTest < normalizedStart)
            {
                normalizedTest += twoPi;
            }
        }

        if (normalizedTest >= normalizedStart && normalizedTest <= normalizedEnd)
        {
            return NormalizeAngle(normalizedTest);
        }

        var distanceToStart = MathF.Abs(normalizedTest - normalizedStart);
        var distanceToEnd = MathF.Abs(normalizedTest - normalizedEnd);
        return NormalizeAngle(distanceToStart <= distanceToEnd ? normalizedStart : normalizedEnd);
    }

    private static float ComputeArcMidAngle(float start, float end)
    {
        const float twoPi = MathF.PI * 2f;
        var normalizedStart = NormalizeAngle(start);
        var normalizedEnd = NormalizeAngle(end);
        if (normalizedEnd < normalizedStart)
        {
            normalizedEnd += twoPi;
        }

        return NormalizeAngle(normalizedStart + (normalizedEnd - normalizedStart) * 0.5f);
    }

    private static bool TryIntersectSegments(
        Vector2 aStart,
        Vector2 aEnd,
        Vector2 bStart,
        Vector2 bEnd,
        out Vector2 intersection)
    {
        intersection = default;
        var r = aEnd - aStart;
        var s = bEnd - bStart;
        var denominator = Cross(r, s);
        if (MathF.Abs(denominator) <= 1e-6f)
        {
            return false;
        }

        var startOffset = bStart - aStart;
        var t = Cross(startOffset, s) / denominator;
        var u = Cross(startOffset, r) / denominator;
        if (t < 0f || t > 1f || u < 0f || u > 1f)
        {
            return false;
        }

        intersection = aStart + r * t;
        return true;
    }

    private static float Cross(Vector2 left, Vector2 right)
    {
        return (left.X * right.Y) - (left.Y * right.X);
    }

    private void AppendIntersectionCandidates(
        IReadOnlyList<RenderHitTestResult> hits,
        List<CadSnapCandidate> candidates)
    {
        Span<(Vector2 Start, Vector2 End)> segmentBuffer = stackalloc (Vector2 Start, Vector2 End)[32];
        var segmentCount = 0;
        for (var index = 0; index < hits.Count && segmentCount < segmentBuffer.Length; index++)
        {
            switch (hits[index].Primitive)
            {
                case RenderLine line:
                    segmentBuffer[segmentCount++] = (line.Start, line.End);
                    break;
                case RenderPolyline polyline:
                    var points = polyline.Points;
                    if (points.Count < 2)
                    {
                        break;
                    }

                    var count = polyline.IsClosed ? points.Count : points.Count - 1;
                    for (var segmentIndex = 0;
                         segmentIndex < count && segmentCount < segmentBuffer.Length;
                         segmentIndex++)
                    {
                        var start = points[segmentIndex];
                        var end = points[(segmentIndex + 1) % points.Count];
                        segmentBuffer[segmentCount++] = (start, end);
                    }
                    break;
            }
        }

        for (var left = 0; left < segmentCount; left++)
        {
            for (var right = left + 1; right < segmentCount; right++)
            {
                var a = segmentBuffer[left];
                var b = segmentBuffer[right];
                if (!TryIntersectSegments(a.Start, a.End, b.Start, b.End, out var intersection))
                {
                    continue;
                }

                AddCandidate(candidates, intersection, CadSnapMode.Intersection, "INT");
                AddCandidate(candidates, intersection, CadSnapMode.ApparentIntersection, "APPINT", priorityBias: 0.02f);
            }
        }
    }

    private void AppendDirectionalSnapCandidates(
        Vector2 basePoint,
        Vector2 pickPoint,
        Vector2 start,
        Vector2 end,
        float segmentParameter,
        ICollection<CadSnapCandidate> candidates)
    {
        var perpendicular = ClosestPointOnLine(basePoint, start, end, out var lineParameter);
        AddCandidate(candidates, perpendicular, CadSnapMode.Perpendicular, "PER");

        if (lineParameter < 0f || lineParameter > 1f || segmentParameter <= 0f || segmentParameter >= 1f)
        {
            AddCandidate(candidates, perpendicular, CadSnapMode.Extension, "EXT");
        }

        var axis = end - start;
        var axisLength = axis.Length();
        if (axisLength <= float.Epsilon)
        {
            return;
        }

        var direction = axis / axisLength;
        var projection = Vector2.Dot(pickPoint - basePoint, direction);
        var parallel = basePoint + direction * projection;
        AddCandidate(candidates, parallel, CadSnapMode.Parallel, "PAR");
    }

    private void UpdateGripFeedback(ICadEditorSession? session, Vector2 pickPoint, float tolerance)
    {
        RebuildGripSet(session);
        var activeGripSet = _activeGripSet;
        if (activeGripSet.Count == 0)
        {
            _hotGrip = null;
            return;
        }

        _hotGrip = _gripService.TryResolveHotGrip(
            pickPoint,
            ResolveGripTolerance(tolerance),
            activeGripSet,
            out var grip)
            ? grip
            : null;
    }

    private Vector2 ResolveCurrentAnchor(ICadEditorSession? session)
    {
        if (_lastPointerWorldPoint.HasValue)
        {
            return _lastPointerWorldPoint.Value;
        }

        if (TryGetSelectionBounds(session, out var selectionBounds) &&
            !selectionBounds.IsEmpty)
        {
            return new Vector2(
                (selectionBounds.MinX + selectionBounds.MaxX) * 0.5f,
                (selectionBounds.MinY + selectionBounds.MaxY) * 0.5f);
        }

        return Vector2.Zero;
    }

    private bool TryGetSelectionBounds(ICadEditorSession? session, out RenderBounds bounds)
    {
        bounds = RenderBounds.Empty;
        var found = false;
        foreach (var entity in EnumerateSelectedEntities(session))
        {
            RenderBounds entityBounds;
            if (session is not null &&
                session.EntityIndex.TryGetId(entity, out var entityId) &&
                TryResolveEntityBounds(session, entityId, out var indexedBounds))
            {
                entityBounds = indexedBounds;
            }
            else if (!TryResolveEntityBounds(entity, out entityBounds))
            {
                continue;
            }

            bounds = found ? bounds.Expand(entityBounds) : entityBounds;
            found = true;
        }

        return found;
    }

    private void RebuildGripSet(ICadEditorSession? session)
    {
        var gripSeeds = new List<CadGripPoint>();
        var gripBindings = new Dictionary<string, GripBinding>(StringComparer.Ordinal);
        foreach (var entity in EnumerateSelectedEntities(session))
        {
            AppendGripSeeds(session, entity, gripSeeds, gripBindings);
        }

        _gripBindings = gripBindings;
        _activeGripSet = _gripService.BuildGripSet(gripSeeds);
    }

    private IEnumerable<Entity> EnumerateSelectedEntities(ICadEditorSession? session)
    {
        if (session is not null)
        {
            var yieldedFromUiSelection = false;
            var seen = new HashSet<Entity>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
            foreach (var item in _selectionService.SelectedObjects)
            {
                if (item is not Entity entity ||
                    !TryResolveSessionEntity(session, entity, out var canonicalEntity) ||
                    !seen.Add(canonicalEntity))
                {
                    continue;
                }

                yieldedFromUiSelection = true;
                yield return canonicalEntity;
            }

            if (yieldedFromUiSelection)
            {
                yield break;
            }

            foreach (var item in session.SelectionSet.Items)
            {
                if (item is Entity entity &&
                    seen.Add(entity))
                {
                    yield return entity;
                }
            }

            yield break;
        }

        foreach (var item in _selectionService.SelectedObjects)
        {
            if (item is Entity entity)
            {
                yield return entity;
            }
        }
    }

    private bool IsEntitySelected(ICadEditorSession? session, Entity entity)
    {
        return TryResolveSelectedEntityReference(session, entity, out _);
    }

    private bool TryResolveSelectedEntityReference(
        ICadEditorSession? session,
        Entity entity,
        out Entity selectedEntity)
    {
        if (session is not null)
        {
            foreach (var item in session.SelectionSet.Items)
            {
                if (item is Entity candidate &&
                    AreEquivalentEntities(session, candidate, entity))
                {
                    selectedEntity = candidate;
                    return true;
                }
            }

            selectedEntity = entity;
            return false;
        }

        foreach (var item in _selectionService.SelectedObjects)
        {
            if (item is Entity candidate &&
                AreEquivalentEntities(null, candidate, entity))
            {
                selectedEntity = candidate;
                return true;
            }
        }

        selectedEntity = entity;
        return false;
    }

    private static bool AreEquivalentEntities(ICadEditorSession? session, Entity left, Entity right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Handle != 0 &&
            right.Handle != 0 &&
            left.Handle == right.Handle)
        {
            return true;
        }

        if (session is null)
        {
            return false;
        }

        return session.EntityIndex.TryGetId(left, out var leftId) &&
               session.EntityIndex.TryGetId(right, out var rightId) &&
               leftId.Equals(rightId);
    }

    private void AppendGripSeeds(
        ICadEditorSession? session,
        Entity entity,
        ICollection<CadGripPoint> target,
        IDictionary<string, GripBinding> gripBindings)
    {
        switch (entity)
        {
            case Line line:
            {
                var start = ToVector2(line.StartPoint);
                var end = ToVector2(line.EndPoint);
                AddGripSeed(session, entity, start, "Endpoint", "Start", 0, target, gripBindings);
                AddGripSeed(session, entity, end, "Endpoint", "End", 1, target, gripBindings);
                AddGripSeed(session, entity, (start + end) * 0.5f, "Midpoint", "Mid", 0, target, gripBindings);
                break;
            }
            case Arc arc:
            {
                var center = ToVector2(arc.Center);
                var radius = (float)arc.Radius;
                var start = PointAtAngle(center, radius, (float)arc.StartAngle);
                var end = PointAtAngle(center, radius, (float)arc.EndAngle);
                AddGripSeed(session, entity, center, "Center", "Center", 0, target, gripBindings);
                AddGripSeed(session, entity, start, "Endpoint", "Start", 0, target, gripBindings);
                AddGripSeed(session, entity, end, "Endpoint", "End", 1, target, gripBindings);
                AddGripSeed(
                    session,
                    entity,
                    PointAtAngle(center, radius, ComputeArcMidAngle((float)arc.StartAngle, (float)arc.EndAngle)),
                    "Midpoint",
                    "Mid",
                    2,
                    target,
                    gripBindings);
                break;
            }
            case Circle circle:
            {
                var center = ToVector2(circle.Center);
                var radius = (float)circle.Radius;
                AddGripSeed(session, entity, center, "Center", "Center", 0, target, gripBindings);
                AddGripSeed(session, entity, center + new Vector2(radius, 0f), "Quadrant", "Quadrant", 0, target, gripBindings);
                AddGripSeed(session, entity, center + new Vector2(0f, radius), "Quadrant", "Quadrant", 1, target, gripBindings);
                AddGripSeed(session, entity, center + new Vector2(-radius, 0f), "Quadrant", "Quadrant", 2, target, gripBindings);
                AddGripSeed(session, entity, center + new Vector2(0f, -radius), "Quadrant", "Quadrant", 3, target, gripBindings);
                break;
            }
            case LwPolyline polyline:
            {
                var vertices = polyline.Vertices
                    .Select(vertex => new XYZ(vertex.Location.X, vertex.Location.Y, polyline.Elevation))
                    .ToArray();
                if (vertices.Length == 0)
                {
                    break;
                }

                for (var index = 0; index < vertices.Length; index++)
                {
                    AddGripSeed(session, entity, ToVector2(vertices[index]), "Vertex", "Vertex", index, target, gripBindings);
                }

                var segmentCount = polyline.IsClosed ? vertices.Length : vertices.Length - 1;
                for (var index = 0; index < segmentCount; index++)
                {
                    var start = ToVector2(vertices[index]);
                    var end = ToVector2(vertices[(index + 1) % vertices.Length]);
                    AddGripSeed(session, entity, (start + end) * 0.5f, "Midpoint", "SegmentMidpoint", index, target, gripBindings);
                }

                break;
            }
            case Point point:
                AddGripSeed(session, entity, ToVector2(point.Location), "Node", "Node", 0, target, gripBindings);
                break;
            case TextEntity text:
            {
                if (TryResolvePrimaryRenderText(session, entity, out var renderedText))
                {
                    var renderedInsert = ResolveRenderTextBaselineStart(renderedText);
                    var baselineEnd = ResolveRenderTextBaselineEnd(renderedText);
                    var heightPoint = ResolveRenderTextHeightHandle(renderedText);
                    var renderedAlignment = ResolveRenderTextCenter(renderedText);
                    AddGripSeed(session, entity, renderedInsert, "Text", "Insert", 0, target, gripBindings);
                    AddGripSeed(session, entity, renderedAlignment, "Text", "Alignment", 1, target, gripBindings);
                    AddGripSeed(session, entity, baselineEnd, "Text", "BaselineEnd", 2, target, gripBindings);
                    AddGripSeed(session, entity, heightPoint, "Text", "Height", 3, target, gripBindings);
                    break;
                }

                var direction = ResolveTextDirectionVector(text);
                var normal = new Vector2(-direction.Y, direction.X);
                var insert = ToVector2(text.InsertPoint);
                var alignment = ToVector2(text.AlignmentPoint);
                var textHeight = MathF.Max((float)text.Height, 0.1f);
                var baselineLength = EstimateTextWidth(text.Value, textHeight, (float)text.WidthFactor);
                if (TryResolveEntityRenderBounds(session, entity, out var renderBounds))
                {
                    insert = ResolveBoundedAnchor(renderBounds, insert);
                    baselineLength = MathF.Max(
                        ResolveProjectedExtent(renderBounds, insert, direction),
                        MathF.Max(renderBounds.Size.X, renderBounds.Size.Y) * 0.45f);
                    var resolvedHeight = MathF.Max(
                        ResolveProjectedExtent(renderBounds, insert, normal),
                        MathF.Max(renderBounds.Size.Y, renderBounds.Size.X * 0.25f) * 0.5f);
                    textHeight = MathF.Max(resolvedHeight, 0.1f);
                    alignment = ResolveAlignmentGripPosition(renderBounds, insert, alignment, normal, textHeight);
                }
                else
                {
                    alignment = ResolveTextAlignmentGripPosition(text, insert, direction, normal, baselineLength, textHeight);
                }

                AddGripSeed(session, entity, insert, "Text", "Insert", 0, target, gripBindings);
                AddGripSeed(session, entity, alignment, "Text", "Alignment", 1, target, gripBindings);
                AddGripSeed(session, entity, insert + direction * baselineLength, "Text", "BaselineEnd", 2, target, gripBindings);
                AddGripSeed(session, entity, insert + direction * baselineLength + normal * textHeight, "Text", "Height", 3, target, gripBindings);
                break;
            }
            case MText mtext:
            {
                var direction = ResolveMTextDirectionVector(mtext);
                var insert = ToVector2(mtext.InsertPoint);
                var textHeight = MathF.Max((float)mtext.Height, 0.1f);
                var width = ResolveMTextGripWidth(mtext, textHeight);
                var height = ResolveMTextGripHeight(mtext, textHeight);
                if (TryResolveEntityRenderGeometry(session, entity, out var geometry))
                {
                    if (TryResolvePrimaryRenderText(geometry, out var primaryText))
                    {
                        var baselineStart = ResolveRenderTextBaselineStart(primaryText);
                        var baselineEnd = ResolveRenderTextBaselineEnd(primaryText);
                        var renderDirection = baselineEnd - baselineStart;
                        if (renderDirection.LengthSquared() > 1e-6f)
                        {
                            direction = NormalizeOrFallback(renderDirection, direction);
                        }

                        insert = baselineStart;
                    }

                    if (TryResolveRenderTextBounds(geometry, out var renderBounds))
                    {
                        insert = ResolveBoundedAnchor(renderBounds, insert);
                        var renderNormal = new Vector2(-direction.Y, direction.X);
                        width = MathF.Max(
                            ResolveProjectedExtent(renderBounds, insert, direction),
                            MathF.Max(renderBounds.Size.X, renderBounds.Size.Y) * 0.5f);
                        height = MathF.Max(
                            ResolveProjectedExtent(renderBounds, insert, renderNormal),
                            MathF.Max(renderBounds.Size.Y, renderBounds.Size.X * 0.25f) * 0.5f);
                        textHeight = MathF.Max(height, textHeight);
                    }
                }
                else if (TryResolveEntityRenderBounds(session, entity, out var renderBounds))
                {
                    insert = ResolveBoundedAnchor(renderBounds, insert);
                    var renderNormal = new Vector2(-direction.Y, direction.X);
                    width = MathF.Max(
                        ResolveProjectedExtent(renderBounds, insert, direction),
                        MathF.Max(renderBounds.Size.X, renderBounds.Size.Y) * 0.5f);
                    height = MathF.Max(
                        ResolveProjectedExtent(renderBounds, insert, renderNormal),
                        MathF.Max(renderBounds.Size.Y, renderBounds.Size.X * 0.25f) * 0.5f);
                    textHeight = MathF.Max(height, textHeight);
                }

                var normal = new Vector2(-direction.Y, direction.X);
                var directionHandleDistance = MathF.Max(width * 0.5f, textHeight * 1.5f);
                AddGripSeed(session, entity, insert, "Text", "Insert", 0, target, gripBindings);
                AddGripSeed(session, entity, insert + direction * directionHandleDistance, "Text", "Direction", 1, target, gripBindings);
                AddGripSeed(session, entity, insert + direction * width, "Text", "Width", 2, target, gripBindings);
                AddGripSeed(session, entity, insert + direction * width + normal * height, "Text", "Height", 3, target, gripBindings);
                break;
            }
        }
    }

    private void AddGripSeed(
        ICadEditorSession? session,
        Entity entity,
        Vector2 position,
        string kind,
        string role,
        int segmentIndex,
        ICollection<CadGripPoint> target,
        IDictionary<string, GripBinding> gripBindings)
    {
        string? tag = null;
        if (session is not null)
        {
            if (!session.EntityIndex.TryGetId(entity, out var id))
            {
                id = session.EntityIndex.Register(entity);
            }

            var binding = new GripBinding(id, entity.GetType().Name, role, segmentIndex);
            tag = CreateGripTag(binding);
            gripBindings[tag] = binding;
        }

        target.Add(new CadGripPoint(position, kind, tag));
    }

    private static string CreateGripTag(GripBinding binding)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{binding.EntityId.Value:D}|{binding.EntityType}|{binding.Role}|{binding.SegmentIndex}");
    }

    private void AppendGripHints(ICollection<CadToolVisualHint> hints, bool includeAll = false)
    {
        var activeGripSet = _activeGripSet;
        var hotGrip = _hotGrip;
        if (activeGripSet.Count == 0)
        {
            return;
        }

        var limit = includeAll ? Math.Min(activeGripSet.Count, 256) : Math.Min(activeGripSet.Count, 96);
        for (var index = 0; index < limit; index++)
        {
            var grip = activeGripSet[index];
            var isHot = hotGrip.HasValue &&
                        Vector2.DistanceSquared(hotGrip.Value.Position, grip.Position) <= 1e-6f;
            hints.Add(new CadToolVisualHint(
                Kind: isHot ? "HotGrip" : "Grip",
                Anchor: grip.Position,
                SecondaryAnchor: null,
                Text: isHot ? grip.Kind : null));
        }
    }

    private static float ResolveGripTolerance(float tolerance)
    {
        if (tolerance > 0f)
        {
            return tolerance;
        }

        return 4f;
    }

    private static Vector2 ToVector2(XYZ point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }

    private static XYZ ToXyz(Vector2 point, double z = 0.0)
    {
        return new XYZ(point.X, point.Y, z);
    }

    private static XYZ Translate(XYZ value, XYZ delta)
    {
        return new XYZ(value.X + delta.X, value.Y + delta.Y, value.Z + delta.Z);
    }

    private static Vector2 ResolveTextDirectionVector(TextEntity text)
    {
        return NormalizeOrFallback(
            new Vector2(MathF.Cos((float)text.Rotation), MathF.Sin((float)text.Rotation)),
            Vector2.UnitX);
    }

    private static Vector2 ResolveMTextDirectionVector(MText mtext)
    {
        var direction = new Vector2((float)mtext.AlignmentPoint.X, (float)mtext.AlignmentPoint.Y);
        if (direction.LengthSquared() <= 1e-8f)
        {
            direction = new Vector2(MathF.Cos((float)mtext.Rotation), MathF.Sin((float)mtext.Rotation));
        }

        return NormalizeOrFallback(direction, Vector2.UnitX);
    }

    private static Vector2 ResolveTextAlignmentGripPosition(
        TextEntity text,
        Vector2 insert,
        Vector2 direction,
        Vector2 normal,
        float baselineLength,
        float height)
    {
        var alignment = ToVector2(text.AlignmentPoint);
        if (Vector2.DistanceSquared(alignment, insert) > 1e-8f)
        {
            return alignment;
        }

        var offset = MathF.Max(baselineLength * 0.35f, height * 0.75f);
        return insert + (normal * offset) - (direction * (height * 0.25f));
    }

    private bool TryResolveEntityRenderGeometry(
        ICadEditorSession? session,
        Entity entity,
        out IReadOnlyList<IRenderPrimitive> geometry)
    {
        if (_annotationService.TryGetGeometry(entity, out geometry) && geometry.Count > 0)
        {
            return true;
        }

        if (session is null || entity.Handle == 0)
        {
            geometry = Array.Empty<IRenderPrimitive>();
            return false;
        }

        if (session.EntityIndex.TryGetByHandle(entity.Handle, out var canonicalEntity, out _) &&
            !ReferenceEquals(canonicalEntity, entity) &&
            _annotationService.TryGetGeometry(canonicalEntity, out geometry) &&
            geometry.Count > 0)
        {
            return true;
        }

        geometry = Array.Empty<IRenderPrimitive>();
        return false;
    }

    private bool TryResolvePrimaryRenderText(ICadEditorSession? session, Entity entity, out RenderText renderText)
    {
        if (TryResolveEntityRenderGeometry(session, entity, out var geometry))
        {
            return TryResolvePrimaryRenderText(geometry, out renderText);
        }

        renderText = null!;
        return false;
    }

    private static bool TryResolvePrimaryRenderText(
        IReadOnlyList<IRenderPrimitive> geometry,
        out RenderText renderText)
    {
        RenderText? best = null;
        var bestArea = float.NegativeInfinity;
        for (var index = 0; index < geometry.Count; index++)
        {
            if (geometry[index] is not RenderText candidate)
            {
                continue;
            }

            var area = candidate.Bounds.Area;
            if (area > bestArea)
            {
                bestArea = area;
                best = candidate;
            }
        }

        if (best is null)
        {
            renderText = null!;
            return false;
        }

        renderText = best;
        return true;
    }

    private static bool TryResolveRenderTextBounds(
        IReadOnlyList<IRenderPrimitive> geometry,
        out RenderBounds bounds)
    {
        bounds = RenderBounds.Empty;
        var found = false;
        for (var index = 0; index < geometry.Count; index++)
        {
            if (geometry[index] is not RenderText candidate || candidate.Bounds.IsEmpty)
            {
                continue;
            }

            bounds = found ? bounds.Expand(candidate.Bounds) : candidate.Bounds;
            found = true;
        }

        return found;
    }

    private static Vector2 ResolveRenderTextBaselineStart(RenderText text)
    {
        var localBaselineStart = new Vector2(text.Offset.X, text.Offset.Y + text.LayoutHeight);
        return ResolveRenderTextPoint(text, localBaselineStart);
    }

    private static Vector2 ResolveRenderTextBaselineEnd(RenderText text)
    {
        var localBaselineEnd = new Vector2(text.Offset.X + text.LayoutWidth, text.Offset.Y + text.LayoutHeight);
        return ResolveRenderTextPoint(text, localBaselineEnd);
    }

    private static Vector2 ResolveRenderTextHeightHandle(RenderText text)
    {
        var localTopRight = new Vector2(text.Offset.X + text.LayoutWidth, text.Offset.Y);
        return ResolveRenderTextPoint(text, localTopRight);
    }

    private static Vector2 ResolveRenderTextCenter(RenderText text)
    {
        var localCenter = new Vector2(
            text.Offset.X + text.LayoutWidth * 0.5f,
            text.Offset.Y + text.LayoutHeight * 0.5f);
        return ResolveRenderTextPoint(text, localCenter);
    }

    private static Vector2 ResolveRenderTextPoint(RenderText text, Vector2 localPoint)
    {
        var scaleX = text.WidthFactor * (text.MirrorX ? -1f : 1f);
        var scaleY = text.MirrorY ? 1f : -1f;
        var transformed = new Vector2(localPoint.X * scaleX, localPoint.Y * scaleY);
        if (MathF.Abs(text.ObliqueAngle) > 0.0001f)
        {
            transformed = new Vector2(
                transformed.X + transformed.Y * MathF.Tan(text.ObliqueAngle),
                transformed.Y);
        }

        var sin = MathF.Sin(text.Rotation);
        var cos = MathF.Cos(text.Rotation);
        var rotated = new Vector2(
            transformed.X * cos - transformed.Y * sin,
            transformed.X * sin + transformed.Y * cos);
        return text.Anchor + rotated;
    }

    private bool TryResolveEntityRenderBounds(ICadEditorSession? session, Entity entity, out RenderBounds bounds)
    {
        if (_annotationService.TryGetBounds(entity, out bounds) && !bounds.IsEmpty)
        {
            return true;
        }

        if (session is null || entity.Handle == 0)
        {
            bounds = default;
            return false;
        }

        if (session.EntityIndex.TryGetByHandle(entity.Handle, out var canonicalEntity, out _) &&
            !ReferenceEquals(canonicalEntity, entity) &&
            _annotationService.TryGetBounds(canonicalEntity, out bounds) &&
            !bounds.IsEmpty)
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private static Vector2 ResolveBoundedAnchor(RenderBounds bounds, Vector2 preferred)
    {
        if (bounds.Contains(preferred))
        {
            return preferred;
        }

        return new Vector2(
            Math.Clamp(preferred.X, bounds.MinX, bounds.MaxX),
            Math.Clamp(preferred.Y, bounds.MinY, bounds.MaxY));
    }

    private static Vector2 ResolveAlignmentGripPosition(
        RenderBounds bounds,
        Vector2 insert,
        Vector2 preferredAlignment,
        Vector2 normal,
        float fallbackDistance)
    {
        var threshold = MathF.Max(MathF.Max(bounds.Size.X, bounds.Size.Y) * 0.5f, 0.5f);
        var clampedAlignment = ResolveBoundedAnchor(bounds, preferredAlignment);
        if (Vector2.DistanceSquared(clampedAlignment, insert) > 1e-6f &&
            Vector2.Distance(clampedAlignment, preferredAlignment) <= threshold)
        {
            return clampedAlignment;
        }

        Span<Vector2> corners = stackalloc Vector2[4];
        GetBoundsCorners(bounds, corners);
        var farthest = corners[0];
        var farthestDistance = Vector2.DistanceSquared(farthest, insert);
        for (var index = 1; index < corners.Length; index++)
        {
            var corner = corners[index];
            var distance = Vector2.DistanceSquared(corner, insert);
            if (distance > farthestDistance)
            {
                farthest = corner;
                farthestDistance = distance;
            }
        }

        if (farthestDistance > 1e-6f)
        {
            return farthest;
        }

        return insert + normal * MathF.Max(fallbackDistance, 0.5f);
    }

    private static float ResolveProjectedExtent(RenderBounds bounds, Vector2 origin, Vector2 axis)
    {
        var direction = NormalizeOrFallback(axis, Vector2.UnitX);
        Span<Vector2> corners = stackalloc Vector2[4];
        GetBoundsCorners(bounds, corners);
        var maxProjection = 0f;
        for (var index = 0; index < corners.Length; index++)
        {
            var projection = Vector2.Dot(corners[index] - origin, direction);
            if (projection > maxProjection)
            {
                maxProjection = projection;
            }
        }

        if (maxProjection > 1e-4f)
        {
            return maxProjection;
        }

        var size = bounds.Size;
        return MathF.Max(MathF.Max(size.X, size.Y) * 0.5f, 0.5f);
    }

    private static float ResolveMTextGripWidth(MText mtext, float height)
    {
        if (mtext.RectangleWidth > 1e-4d)
        {
            return (float)mtext.RectangleWidth;
        }

        var estimated = EstimateTextWidth(mtext.PlainText, height, 1f);
        return MathF.Max(estimated, height * 2f);
    }

    private static float ResolveMTextGripHeight(MText mtext, float height)
    {
        if (mtext.RectangleHeight > 1e-4d)
        {
            return (float)mtext.RectangleHeight;
        }

        var lineCount = CountTextLines(mtext.Value);
        return MathF.Max(height * MathF.Max(1f, lineCount * 1.2f), height);
    }

    private static float EstimateTextWidth(string? text, float height, float widthFactor)
    {
        var safeHeight = MathF.Max(height, 0.1f);
        var safeWidthFactor = MathF.Max(widthFactor, 0.25f);
        var glyphCount = Math.Max(1, text?.Length ?? 0);
        var averageGlyphWidth = safeHeight * safeWidthFactor * 0.6f;
        return MathF.Max(averageGlyphWidth * glyphCount, safeHeight * 1.5f);
    }

    private static int CountTextLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        var lines = 1;
        for (var index = 0; index < text.Length; index++)
        {
            var value = text[index];
            if (value == '\n')
            {
                lines++;
                continue;
            }

            if (value == '\r')
            {
                lines++;
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                continue;
            }

            if (value == '\\' &&
                index + 1 < text.Length &&
                (text[index + 1] == 'P' || text[index + 1] == 'p'))
            {
                lines++;
                index++;
            }
        }

        return Math.Max(lines, 1);
    }

    private static Vector2 NormalizeOrFallback(Vector2 value, Vector2 fallback)
    {
        var lengthSquared = value.LengthSquared();
        if (lengthSquared <= 1e-8f)
        {
            return fallback;
        }

        return value / MathF.Sqrt(lengthSquared);
    }

    private static IReadOnlyList<XYZ> ToPolylineVertices(LwPolyline polyline)
    {
        return polyline.Vertices
            .Select(vertex => new XYZ(vertex.Location.X, vertex.Location.Y, polyline.Elevation))
            .ToArray();
    }

    private static XYZ NormalizeDirection(XYZ direction)
    {
        var length = Math.Sqrt(
            (direction.X * direction.X) +
            (direction.Y * direction.Y) +
            (direction.Z * direction.Z));
        if (length <= double.Epsilon)
        {
            return XYZ.Zero;
        }

        return new XYZ(direction.X / length, direction.Y / length, direction.Z / length);
    }

    private static int GetRequiredSelectionCount(string? activeCommand)
    {
        if (string.IsNullOrWhiteSpace(activeCommand))
        {
            return 0;
        }

        return activeCommand.Trim().ToUpperInvariant() switch
        {
            "MOVE" => 1,
            "COPY" => 1,
            "ROTATE" => 1,
            "SCALE" => 1,
            "MIRROR" => 1,
            "ERASE" => 1,
            "OFFSET" => 1,
            "STRETCH" => 1,
            "BREAK" => 1,
            "BOUNDARY" => 1,
            "HATCH" => 1,
            "COPYCLIP" => 1,
            "CUT" => 1,
            "EXPLODE" => 1,
            "ARRAY" => 1,
            "ALIGN" => 1,
            "MATCHPROP" => 2,
            "JOIN" => 2,
            "FILLET" => 2,
            "CHAMFER" => 2,
            "TRIM" => 2,
            "EXTEND" => 2,
            _ => 0
        };
    }

    private static int GetSessionSelectionCount(ICadEditorSession? session)
    {
        if (session is null)
        {
            return 0;
        }

        var count = 0;
        foreach (var item in session.SelectionSet.Items)
        {
            if (item is Entity)
            {
                count++;
            }
        }

        return count;
    }
}
