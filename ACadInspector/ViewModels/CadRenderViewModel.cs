using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Core;
using ACadInspector.Editing.Interaction;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Prompt;
using ACadInspector.Rendering;
using ACadInspector.Services;
using ACadSharp;
using ACadSharp.Objects;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadRenderViewModel : ViewModelBase
{
    private readonly CadDocument _document;
    private readonly string? _documentPath;
    private readonly ICadRenderSceneBuilder _sceneBuilder;
    private readonly CadRenderSceneSettings _baseSettings;
    private readonly IDynamicBlockOverrideProvider? _dynamicBlockOverrides;
    private readonly CadSelectionService _selectionService;
    private readonly CadSelectionAnnotationService _annotationService;
    private readonly CadToolManager _toolManager;
    private readonly CadInteractionRouter _interactionRouter;
    private readonly CadEditorInteractionController _interactionController;
    private readonly CadSelectionFocusService _focusService;
    private readonly CadEditorSessionHostService? _sessionHost;
    private readonly ICadCommandRuntime _commandRuntime;
    private readonly CadCollaborationWorkspaceService? _collaborationWorkspace;
    private readonly List<RenderBounds> _bvhBounds = new();
    private IReadOnlyList<CadToolVisualHint> _localToolVisualHints = Array.Empty<CadToolVisualHint>();
    private long _lastKnownRevision = -1;
    private RenderSpatialIndex? _hitTestIndex;
    private CancellationTokenSource? _layoutRebuildCts;
    private readonly bool _allowLayoutUpdates;
    private bool _suppressLayoutUpdates;
    private bool _modelShowGrid = true;
    private bool _modelShowAxes = true;
    private bool _initialAutoFitCompleted;

    [Reactive]
    public partial RenderScene? Scene { get; set; }

    [Reactive]
    public partial bool ShowGrid { get; set; } = true;

    [Reactive]
    public partial bool ShowAxes { get; set; } = true;

    [Reactive]
    public partial bool EnableDashPatternRendering { get; set; } = true;

    [Reactive]
    public partial bool EnableColorRendering { get; set; } = true;

    [Reactive]
    public partial bool ShowLayoutTabs { get; set; } = true;

    [Reactive]
    public partial bool FitOnLoad { get; set; } = true;

    [Reactive]
    public partial bool EnableInteractionOptimization { get; set; } = false;

    [Reactive]
    public partial bool OsnapEnabled { get; set; } = true;

    [Reactive]
    public partial bool OtrackEnabled { get; set; } = true;

    [Reactive]
    public partial bool OrthoEnabled { get; set; }

    [Reactive]
    public partial bool PolarEnabled { get; set; }

    [Reactive]
    public partial bool DynamicInputEnabled { get; set; } = true;

    [Reactive]
    public partial int FitRequest { get; set; }

    [Reactive]
    public partial int ResetRequest { get; set; }

    public CadRenderLayerListViewModel LayerList { get; }
    public CadRenderEntityTypeListViewModel EntityTypeList { get; }
    public IReadOnlyList<CadRenderLayoutViewModel> Layouts { get; }
    public CadToolManager ToolManager => _toolManager;

    [Reactive]
    public partial CadRenderLayoutViewModel? SelectedLayout { get; set; }

    [Reactive]
    public partial object? HoveredObject { get; set; }

    [Reactive]
    public partial object? SelectedObject { get; set; }

    [Reactive]
    public partial bool ShowDebugOverlay { get; set; }

    [Reactive]
    public partial int DebugBvhDepth { get; set; } = 1;

    [Reactive]
    public partial RenderBounds? HoverBounds { get; set; }

    [Reactive]
    public partial RenderBounds? SelectionBounds { get; set; }

    [Reactive]
    public partial RenderAnnotation? HoverAnnotation { get; set; }

    [Reactive]
    public partial RenderAnnotation? SelectionAnnotation { get; set; }

    [Reactive]
    public partial IReadOnlyList<RenderBounds>? DebugBvhBounds { get; set; }

    [Reactive]
    public partial CadRenderFocusRequest? FocusRequest { get; set; }

    [Reactive]
    public partial string ActiveCommandPrompt { get; set; } = "Command";

    [Reactive]
    public partial string ActiveCommandHelp { get; set; } = string.Empty;

    [Reactive]
    public partial string InteractionStatus { get; set; } = string.Empty;

    [Reactive]
    public partial IReadOnlyList<CadToolVisualHint> ToolVisualHints { get; set; } = Array.Empty<CadToolVisualHint>();

    [Reactive]
    public partial IReadOnlyList<CadVisualHelperBadgeViewModel> ActiveVisualHelpers { get; set; } = Array.Empty<CadVisualHelperBadgeViewModel>();

    public bool HasActiveVisualHelpers => ActiveVisualHelpers.Count > 0;

    [Reactive]
    public partial IReadOnlyList<CadCommandCompletionItemViewModel> CanvasCompletions { get; set; } = Array.Empty<CadCommandCompletionItemViewModel>();

    public bool HasCanvasCompletions => CanvasCompletions.Count > 0;

    [Reactive]
    public partial RenderOverlayScene OverlayScene { get; set; } = RenderOverlayScene.Empty;

    [Reactive]
    public partial CadDynamicInputPayload? DynamicInput { get; set; }

    public ReactiveCommand<Unit, Unit> FitCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportStatsCommand { get; }
    public ReactiveCommand<string, Unit> StartCommand { get; }
    public ReactiveCommand<CadInteractionEvent, Unit> InteractionCommand { get; }
    public ReactiveCommand<CadInsertDropRequest, Unit> InsertDroppedBlockCommand { get; }
    public ReactiveCommand<CadCommandCompletionItemViewModel, Unit> ApplyCanvasCompletionCommand { get; }

    private readonly IRenderStatsExportService _statsExportService;
    private readonly string _statsFileName;

    public CadRenderViewModel(
        CadDocument document,
        RenderScene? scene,
        ICadRenderSceneBuilder sceneBuilder,
        CadRenderSceneSettings baseSettings,
        CadRenderLayoutSelection selection,
        string? documentPath,
        IDynamicBlockOverrideProvider? dynamicBlockOverrides,
        IObservable<int>? dynamicBlockOverrideChanges,
        CadSelectionService selectionService,
        CadSelectionFocusService focusService,
        IRenderStatsExportService statsExportService,
        string? statsFileName = null,
        bool allowLayoutUpdates = true)
        : this(
            document,
            scene,
            sceneBuilder,
            baseSettings,
            selection,
            documentPath,
            dynamicBlockOverrides,
            dynamicBlockOverrideChanges,
            selectionService,
            focusService,
            sessionHost: null,
            commandRuntime: null,
            interactiveAdapterRegistry: null,
            shortcutBindings: null,
            collaborationWorkspace: null,
            statsExportService: statsExportService,
            statsFileName: statsFileName,
            allowLayoutUpdates: allowLayoutUpdates)
    {
    }

    public CadRenderViewModel(
        CadDocument document,
        RenderScene? scene,
        ICadRenderSceneBuilder sceneBuilder,
        CadRenderSceneSettings baseSettings,
        CadRenderLayoutSelection selection,
        string? documentPath,
        IDynamicBlockOverrideProvider? dynamicBlockOverrides,
        IObservable<int>? dynamicBlockOverrideChanges,
        CadSelectionService selectionService,
        CadSelectionFocusService focusService,
        CadEditorSessionHostService? sessionHost,
        ICadCommandRuntime? commandRuntime,
        ICadInteractiveCommandAdapterRegistry? interactiveAdapterRegistry = null,
        IReadOnlyList<CadShortcutBinding>? shortcutBindings = null,
        CadCollaborationWorkspaceService? collaborationWorkspace = null,
        IRenderStatsExportService? statsExportService = null,
        string? statsFileName = null,
        bool allowLayoutUpdates = true)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _documentPath = documentPath;
        _sceneBuilder = sceneBuilder ?? throw new ArgumentNullException(nameof(sceneBuilder));
        _baseSettings = baseSettings ?? throw new ArgumentNullException(nameof(baseSettings));
        _dynamicBlockOverrides = dynamicBlockOverrides;
        _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
        _focusService = focusService ?? throw new ArgumentNullException(nameof(focusService));
        _sessionHost = sessionHost;
        _commandRuntime = commandRuntime ?? NullCadCommandRuntime.Instance;
        _collaborationWorkspace = collaborationWorkspace;
        _allowLayoutUpdates = allowLayoutUpdates;
        _annotationService = new CadSelectionAnnotationService();
        _toolManager = new CadToolManager();
        _interactionRouter = new CadInteractionRouter(
            _toolManager,
            _selectionService,
            _annotationService,
            _commandRuntime,
            interactiveAdapterRegistry,
            shortcutBindings);
        _interactionController = new CadEditorInteractionController(
            _document,
            _sessionHost,
            _commandRuntime,
            _interactionRouter,
            _collaborationWorkspace);
        Scene = scene;
        EnableDashPatternRendering = _baseSettings.EnableDashPatternRendering;
        EnableColorRendering = _baseSettings.EnableColorRendering;
        LayerList = new CadRenderLayerListViewModel(document);
        EntityTypeList = new CadRenderEntityTypeListViewModel(document);
        Layouts = BuildLayouts(document);
        _statsExportService = statsExportService ?? NullRenderStatsExportService.Instance;
        _statsFileName = EnsureStatsFileName(statsFileName);

        var canExport = this.WhenAnyValue(x => x.Scene)
            .Select(scene => scene is not null);
        FitCommand = ReactiveCommand.Create(RequestFit);
        ResetCommand = ReactiveCommand.Create(ResetView);
        ExportStatsCommand = ReactiveCommand.CreateFromTask(ExportStatsAsync, canExport);
        StartCommand = ReactiveCommand.Create<string>(StartInteractiveCommand);
        InteractionCommand = ReactiveCommand.CreateFromTask<CadInteractionEvent>(HandleInteractionAsync);
        InsertDroppedBlockCommand = ReactiveCommand.CreateFromTask<CadInsertDropRequest>(HandleInsertDropAsync);
        ApplyCanvasCompletionCommand = ReactiveCommand.CreateFromTask<CadCommandCompletionItemViewModel>(ApplyCanvasCompletionAsync);

        _suppressLayoutUpdates = true;
        SelectedLayout = ResolveSelectedLayout(selection);
        _suppressLayoutUpdates = false;
        if (SelectedLayout is not null)
        {
            UpdateLayoutViewOptions(SelectedLayout);
        }

        this.WhenAnyValue(x => x.SelectedLayout)
            .Where(layout => layout is not null)
            .Subscribe(layout => OnLayoutChanged(layout!));

        this.WhenAnyValue(x => x.Scene)
            .Subscribe(_ => OnSceneChanged());

        this.WhenAnyValue(x => x.LayerList.LayerVisibilityOverrides)
            .Subscribe(_ => UpdateHitTestIndex());

        this.WhenAnyValue(x => x.EntityTypeList.EntityTypeVisibilityOverrides)
            .Subscribe(_ => OnEntityTypeVisibilityChanged());

        this.WhenAnyValue(x => x.EnableDashPatternRendering, x => x.EnableColorRendering)
            .Skip(1)
            .Subscribe(_ => OnRenderStyleOptionsChanged());

        this.WhenAnyValue(x => x.ShowDebugOverlay, x => x.DebugBvhDepth, x => x.Scene)
            .Subscribe(_ => UpdateDebugOverlay());

        this.WhenAnyValue(x => x.OsnapEnabled)
            .Subscribe(enabled => _interactionController.UpdateSnapEnabled(enabled));

        this.WhenAnyValue(x => x.OtrackEnabled, x => x.OrthoEnabled, x => x.PolarEnabled)
            .Subscribe(tuple => _interactionController.UpdateTracking(tuple.Item1, tuple.Item2, tuple.Item3));

        _selectionService.WhenAnyValue(x => x.SelectedObject)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(UpdateSelectionFromService);

        _annotationService.WhenAnyValue(x => x.HoverAnnotation)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(annotation =>
            {
                HoverAnnotation = annotation;
                HoverBounds = annotation?.Bounds;
                UpdateDebugOverlay();
            });

        _annotationService.WhenAnyValue(x => x.SelectionAnnotation)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(annotation =>
            {
                SelectionAnnotation = annotation;
                SelectionBounds = annotation?.Bounds;
                UpdateDebugOverlay();
            });

        _annotationService.WhenAnyValue(x => x.HoveredObject)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(obj => HoveredObject = obj);

        if (dynamicBlockOverrideChanges is not null)
        {
            dynamicBlockOverrideChanges
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(stamp =>
                {
                    if (SelectedLayout is not null)
                    {
                        _ = RebuildSceneAsync(SelectedLayout);
                    }
                });
        }

        _focusService.WhenAnyValue(x => x.FocusRequest)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(HandleFocusRequest);

        _commandRuntime.StateChanged += OnCommandRuntimeStateChanged;
        _interactionController.InteractionStatusChanged += OnInteractionStatusChanged;
        UpdateRuntimeState(_commandRuntime.State);
        if (_sessionHost is not null)
        {
            _sessionHost.SessionChanged += OnSessionChanged;
            _sessionHost.SessionRemoved += OnSessionRemoved;
            if (_sessionHost.TryGet(_document, out var existingSession))
            {
                _lastKnownRevision = existingSession.Revision;
            }
        }
        if (_collaborationWorkspace is not null)
        {
            _collaborationWorkspace.PresenceChanged += OnCollaborationPresenceChanged;
        }

        RegisterDefaultTools();

        if (Scene is null && SelectedLayout is not null)
        {
            _ = RebuildSceneAsync(SelectedLayout);
        }
    }

    private void OnEntityTypeVisibilityChanged()
    {
        UpdateHitTestIndex();
        if (!_allowLayoutUpdates)
        {
            return;
        }
        if (SelectedLayout is null)
        {
            return;
        }

        _ = RebuildSceneAsync(SelectedLayout, preserveView: true);
    }

    private void OnRenderStyleOptionsChanged()
    {
        if (!_allowLayoutUpdates)
        {
            return;
        }

        if (SelectedLayout is null)
        {
            return;
        }

        _ = RebuildSceneAsync(SelectedLayout, preserveView: true);
    }

    private void RequestFit()
    {
        FitRequest++;
    }

    private void ResetView()
    {
        ResetRequest++;
    }

    private void OnSceneChanged()
    {
        _annotationService.UpdateScene(Scene);
        _annotationService.ClearHover();
        UpdateHitTestIndex();
        _interactionController.UpdateScene(Scene, _hitTestIndex);
        UpdateDebugOverlay();
        UpdateSelectionFromService(_selectionService.SelectedObject);

        if (!_initialAutoFitCompleted && Scene is not null && FitOnLoad)
        {
            _initialAutoFitCompleted = true;
            FitOnLoad = false;
        }
    }

    private IReadOnlyList<CadRenderLayoutViewModel> BuildLayouts(CadDocument document)
    {
        var layouts = new List<CadRenderLayoutViewModel>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var modelName = Layout.ModelLayoutName;
        layouts.Add(new CadRenderLayoutViewModel(modelName, isPaperSpace: false, displayName: "Model"));
        names.Add(modelName);

        if (document.Layouts is not null)
        {
            var ordered = new List<Layout>();
            foreach (var layout in document.Layouts)
            {
                if (layout.IsPaperSpace)
                {
                    ordered.Add(layout);
                }
            }

            ordered.Sort(static (left, right) => left.TabOrder.CompareTo(right.TabOrder));
            foreach (var layout in ordered)
            {
                if (names.Add(layout.Name))
                {
                    layouts.Add(new CadRenderLayoutViewModel(layout.Name, isPaperSpace: true));
                }
            }
        }

        return layouts;
    }

    private CadRenderLayoutViewModel? ResolveSelectedLayout(CadRenderLayoutSelection selection)
    {
        if (!selection.IsPaperSpace)
        {
            return Layouts.Count > 0 ? Layouts[0] : null;
        }

        if (!string.IsNullOrWhiteSpace(selection.LayoutName))
        {
            foreach (var layout in Layouts)
            {
                if (layout.IsPaperSpace && string.Equals(layout.Name, selection.LayoutName, StringComparison.OrdinalIgnoreCase))
                {
                    return layout;
                }
            }
        }

        foreach (var layout in Layouts)
        {
            if (layout.IsPaperSpace)
            {
                return layout;
            }
        }

        return Layouts.Count > 0 ? Layouts[0] : null;
    }

    private void OnLayoutChanged(CadRenderLayoutViewModel layout)
    {
        if (_suppressLayoutUpdates)
        {
            return;
        }

        ClearTransientInteractionVisuals();
        UpdateLayoutViewOptions(layout);

        if (!_allowLayoutUpdates)
        {
            return;
        }

        _ = RebuildSceneAsync(layout);
    }

    private void UpdateLayoutViewOptions(CadRenderLayoutViewModel layout)
    {
        if (layout.IsPaperSpace)
        {
            _modelShowGrid = ShowGrid;
            _modelShowAxes = ShowAxes;
            ShowGrid = false;
            ShowAxes = false;
        }
        else
        {
            ShowGrid = _modelShowGrid;
            ShowAxes = _modelShowAxes;
        }
    }

    private async Task RebuildSceneAsync(CadRenderLayoutViewModel layout, bool preserveView = false)
    {
        _layoutRebuildCts?.Cancel();
        var cts = new CancellationTokenSource();
        _layoutRebuildCts = cts;

        var selection = layout.IsPaperSpace
            ? new CadRenderLayoutSelection(true, layout.Name)
            : CadRenderLayoutSelection.ModelSpace;
        var baseSettings = _dynamicBlockOverrides is null
            ? _baseSettings
            : _baseSettings.WithDynamicBlockOverrides(_dynamicBlockOverrides);
        baseSettings = baseSettings.WithEntityTypeVisibilityOverrides(EntityTypeList.EntityTypeVisibilityOverrides);
        baseSettings = baseSettings.WithRenderStyleOptions(EnableDashPatternRendering, EnableColorRendering);
        var settings = CadRenderSettingsBuilder.Build(_document, _documentPath, baseSettings, selection);

        RenderScene? scene = null;
        try
        {
            scene = await Task.Run(() => _sceneBuilder.Build(_document, settings), cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested)
        {
            return;
        }

        RxApp.MainThreadScheduler.Schedule(() =>
        {
            Scene = scene;
            if (!preserveView)
            {
                RequestFit();
            }
        });
    }

    private void HandleToolInput(CadToolInput input)
    {
        _toolManager.HandleInput(input, BuildToolContext());
    }

    private void StartInteractiveCommand(string? commandName)
    {
        _interactionController.BeginCommand(commandName);
    }

    private async Task HandleInsertDropAsync(CadInsertDropRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BlockName))
        {
            return;
        }

        var session = _interactionController.ResolveSession();
        if (session is null)
        {
            InteractionStatus = "No active editor session.";
            return;
        }

        static string EscapeToken(string value)
        {
            return value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        var blockName = EscapeToken(request.BlockName.Trim());
        var pointToken = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{request.WorldPoint.X:0.###},{request.WorldPoint.Y:0.###}");
        var command = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"INSERT \"{blockName}\" {pointToken}");
        var resolution = await _commandRuntime.SubmitAsync(command, session).ConfigureAwait(true);

        if (_sessionHost is not null)
        {
            _sessionHost.SyncSelectionToUi(session);
            _sessionHost.NotifySessionChanged(session);
        }

        if (!_allowLayoutUpdates && SelectedLayout is not null)
        {
            _ = RebuildSceneAsync(SelectedLayout, preserveView: true);
        }

        UpdateRuntimeState(resolution.State);
        if (!string.IsNullOrWhiteSpace(resolution.Result?.Message))
        {
            InteractionStatus = resolution.Result!.Message!;
        }
        else if (!string.IsNullOrWhiteSpace(resolution.State.LastMessage))
        {
            InteractionStatus = resolution.State.LastMessage!;
        }
    }

    private async Task HandleInteractionAsync(CadInteractionEvent interactionEvent)
    {
        if (TryHandleDraftingToggleHotkey(interactionEvent, out var hotkeyStatus))
        {
            if (!string.IsNullOrWhiteSpace(hotkeyStatus))
            {
                InteractionStatus = hotkeyStatus;
            }

            return;
        }

        var visual = await _interactionController
            .HandleInteractionAsync(interactionEvent)
            .ConfigureAwait(true);
        ApplyToolVisualSnapshot(visual);
    }

    private bool TryHandleDraftingToggleHotkey(CadInteractionEvent interactionEvent, out string status)
    {
        status = string.Empty;
        if (interactionEvent.Kind != CadInteractionEventKind.KeyDown ||
            interactionEvent.Modifiers != CadInteractionModifiers.None ||
            string.IsNullOrWhiteSpace(interactionEvent.Key))
        {
            return false;
        }

        switch (interactionEvent.Key.Trim())
        {
            case "F3":
                OsnapEnabled = !OsnapEnabled;
                status = OsnapEnabled ? "OSNAP on." : "OSNAP off.";
                return true;
            case "F7":
                ShowGrid = !ShowGrid;
                status = ShowGrid ? "GRID on." : "GRID off.";
                return true;
            case "F8":
                OrthoEnabled = !OrthoEnabled;
                status = OrthoEnabled ? "ORTHO on." : "ORTHO off.";
                return true;
            case "F10":
                PolarEnabled = !PolarEnabled;
                status = PolarEnabled ? "POLAR on." : "POLAR off.";
                return true;
            case "F11":
                OtrackEnabled = !OtrackEnabled;
                status = OtrackEnabled ? "OTRACK on." : "OTRACK off.";
                return true;
            case "F12":
                DynamicInputEnabled = !DynamicInputEnabled;
                status = DynamicInputEnabled ? "DYNINPUT on." : "DYNINPUT off.";
                return true;
            default:
                return false;
        }
    }

    private void OnSessionChanged(object? sender, CadEditorSessionChangedEventArgs args)
    {
        if (!ReferenceEquals(args.Document, _document))
        {
            return;
        }

        if (args.Revision <= _lastKnownRevision)
        {
            return;
        }

        _lastKnownRevision = args.Revision;
        if (!_allowLayoutUpdates || SelectedLayout is null)
        {
            return;
        }

        _ = RebuildSceneAsync(SelectedLayout, preserveView: true);
    }

    private void OnSessionRemoved(object? sender, ACadInspector.Editing.Sessions.ICadEditorSession session)
    {
        if (!ReferenceEquals(session.Document, _document))
        {
            return;
        }

        ClearTransientInteractionVisuals();
    }

    private CadToolContext BuildToolContext()
    {
        return new CadToolContext(Scene, _hitTestIndex, _selectionService, _annotationService);
    }

    private void RegisterDefaultTools()
    {
        var context = BuildToolContext();
        _toolManager.RegisterTool(new CadSelectionTool(), context, activate: true);
    }

    private void UpdateSelectionFromService(object? selected)
    {
        SelectedObject = selected;
        _annotationService.UpdateSelection(selected, null);
    }

    private void HandleFocusRequest(CadSelectionFocusRequest? request)
    {
        if (request?.Target is not ACadSharp.Entities.Entity entity)
        {
            return;
        }

        if (entity.Document is not null && !ReferenceEquals(entity.Document, _document))
        {
            return;
        }

        if (!_annotationService.TryGetBounds(entity, out var bounds) || bounds.IsEmpty)
        {
            return;
        }

        FocusRequest = new CadRenderFocusRequest(bounds, padding: 24.0);
    }

    private void UpdateHitTestIndex()
    {
        var scene = Scene;
        if (scene is null)
        {
            _hitTestIndex = null;
            _interactionController.UpdateScene(Scene, _hitTestIndex);
            return;
        }

        var overrides = LayerList.LayerVisibilityOverrides;
        var entityOverrides = EntityTypeList.EntityTypeVisibilityOverrides;
        if (overrides is null && entityOverrides is null)
        {
            _hitTestIndex = scene.SpatialIndex;
            _interactionController.UpdateScene(Scene, _hitTestIndex);
            return;
        }

        var visibleLayers = new List<RenderLayer>(scene.Layers.Count);
        foreach (var layer in scene.Layers)
        {
            var visible = layer.IsVisible;
            if (overrides is not null && overrides.TryGetValue(layer.Name, out var overrideVisible))
            {
                visible = overrideVisible;
            }

            if (visible)
            {
                if (entityOverrides is null)
                {
                    visibleLayers.Add(layer);
                }
                else
                {
                    var filteredPrimitives = FilterPrimitivesByEntityType(scene, layer, entityOverrides);
                    if (filteredPrimitives.Count == 0)
                    {
                        continue;
                    }

                    var bounds = RenderBounds.Empty;
                    foreach (var primitive in filteredPrimitives)
                    {
                        bounds = bounds.Expand(primitive.Bounds);
                    }

                    visibleLayers.Add(new RenderLayer(layer.Name, layer.Color, layer.IsVisible, filteredPrimitives, bounds));
                }
            }
        }

        _hitTestIndex = RenderSpatialIndex.Build(visibleLayers);
        _interactionController.UpdateScene(Scene, _hitTestIndex);
    }

    private void ApplyToolVisualSnapshot(CadToolVisualSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Prompt))
        {
            ActiveCommandPrompt = snapshot.Prompt;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Status))
        {
            InteractionStatus = snapshot.Status;
        }

        _localToolVisualHints = snapshot.Hints;
        var mergedHints = MergeToolVisualHints(snapshot.Hints);
        ToolVisualHints = mergedHints;
        OverlayScene = BuildOverlayScene(mergedHints);
        RefreshActiveVisualHelpers(mergedHints, _commandRuntime.State);
        if (TryResolveDynamicInputHint(snapshot.Hints, out var dynamicHint))
        {
            DynamicInput = new CadDynamicInputPayload(ActiveCommandPrompt, dynamicHint.Text, dynamicHint.Anchor);
        }
    }

    public void ClearTransientInteractionVisuals(bool preserveRemoteHints = true)
    {
        _interactionController.ResetTransientState();
        _localToolVisualHints = Array.Empty<CadToolVisualHint>();
        DynamicInput = null;
        var mergedHints = preserveRemoteHints
            ? MergeToolVisualHints(_localToolVisualHints)
            : Array.Empty<CadToolVisualHint>();
        ToolVisualHints = mergedHints;
        OverlayScene = mergedHints.Count == 0
            ? RenderOverlayScene.Empty
            : BuildOverlayScene(mergedHints);
        RefreshActiveVisualHelpers(mergedHints, _commandRuntime.State);
    }

    private void OnCommandRuntimeStateChanged(object? sender, CadPromptState state)
    {
        UpdateRuntimeState(state);
    }

    private void OnInteractionStatusChanged(object? sender, string status)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            InteractionStatus = status;
        }
    }

    private void UpdateRuntimeState(CadPromptState state)
    {
        ActiveCommandPrompt = state.Prompt;
        ActiveCommandHelp = state.ParameterHelp ?? string.Empty;
        RefreshCanvasCompletions(state);
        if (!string.IsNullOrWhiteSpace(state.LastMessage))
        {
            InteractionStatus = state.LastMessage;
        }

        if (!state.IsActive)
        {
            ClearTransientInteractionVisuals();
            return;
        }

        DynamicInput = new CadDynamicInputPayload(
            Prompt: state.Prompt,
            Value: state.ParameterHelp,
            Anchor: null);
        RefreshActiveVisualHelpers(ToolVisualHints, state);
    }

    private void RefreshCanvasCompletions(CadPromptState state)
    {
        if (state.Completions.Count == 0)
        {
            CanvasCompletions = Array.Empty<CadCommandCompletionItemViewModel>();
            this.RaisePropertyChanged(nameof(HasCanvasCompletions));
            return;
        }

        var count = Math.Min(8, state.Completions.Count);
        var completions = new CadCommandCompletionItemViewModel[count];
        for (var index = 0; index < count; index++)
        {
            completions[index] = new CadCommandCompletionItemViewModel(state.Completions[index]);
        }

        CanvasCompletions = completions;
        this.RaisePropertyChanged(nameof(HasCanvasCompletions));
    }

    private async Task ApplyCanvasCompletionAsync(CadCommandCompletionItemViewModel? completion)
    {
        if (completion is null)
        {
            return;
        }

        if (string.Equals(completion.Kind, "Command", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(completion.Kind, "Alias", StringComparison.OrdinalIgnoreCase))
        {
            _interactionController.BeginCommand(completion.Value);
            UpdateRuntimeState(_commandRuntime.State);
            return;
        }

        var session = _interactionController.ResolveSession();
        if (session is null)
        {
            InteractionStatus = "No active editor session.";
            return;
        }

        var tokenType = string.Equals(completion.Kind, "Keyword", StringComparison.OrdinalIgnoreCase)
            ? CadPromptTokenType.Keyword
            : CadPromptTokenType.Text;
        var resolution = await _commandRuntime
            .SubmitTokenAsync(new CadPromptToken(tokenType, completion.Value), session, commit: false)
            .ConfigureAwait(true);

        if (_sessionHost is not null)
        {
            _sessionHost.SyncSelectionToUi(session);
            if (resolution.Result?.Operations is { Count: > 0 })
            {
                _sessionHost.NotifySessionChanged(session);
            }
        }

        UpdateRuntimeState(resolution.State);
        if (!string.IsNullOrWhiteSpace(resolution.State.LastMessage))
        {
            InteractionStatus = resolution.State.LastMessage!;
        }
    }

    private IReadOnlyList<CadToolVisualHint> MergeToolVisualHints(IReadOnlyList<CadToolVisualHint> localHints)
    {
        if (_collaborationWorkspace is null)
        {
            return localHints;
        }

        var remoteHints = _collaborationWorkspace.GetRemoteGhostHints(_interactionController.ResolveSession());
        if (remoteHints.Count == 0)
        {
            return localHints;
        }

        if (localHints.Count == 0)
        {
            return remoteHints;
        }

        var merged = new List<CadToolVisualHint>(localHints.Count + remoteHints.Count);
        merged.AddRange(localHints);
        merged.AddRange(remoteHints);
        return merged;
    }

    private void RefreshActiveVisualHelpers(
        IReadOnlyList<CadToolVisualHint>? hints,
        CadPromptState state)
    {
        ActiveVisualHelpers = BuildActiveVisualHelpers(
            hints ?? Array.Empty<CadToolVisualHint>(),
            state);
        this.RaisePropertyChanged(nameof(HasActiveVisualHelpers));
    }

    private static IReadOnlyList<CadVisualHelperBadgeViewModel> BuildActiveVisualHelpers(
        IReadOnlyList<CadToolVisualHint> hints,
        CadPromptState state)
    {
        if (!state.IsActive && hints.Count == 0)
        {
            return Array.Empty<CadVisualHelperBadgeViewModel>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var badges = new List<CadVisualHelperBadgeViewModel>(8);

        void AddBadge(string key, string label)
        {
            if (seen.Add(key))
            {
                badges.Add(new CadVisualHelperBadgeViewModel(key, label));
            }
        }

        if (state.IsActive &&
            !string.IsNullOrWhiteSpace(state.ActiveCommand))
        {
            AddBadge("tool", string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Tool: {state.ActiveCommand!.ToUpperInvariant()}"));
        }

        for (var index = 0; index < hints.Count; index++)
        {
            var normalizedKind = NormalizeHintKind(hints[index].Kind, out var isRemote);
            if (TryMapHelperBadge(normalizedKind, isRemote, out var key, out var label))
            {
                AddBadge(key, label);
            }
        }

        if (state.IsActive && !string.IsNullOrWhiteSpace(state.ParameterHelp))
        {
            AddBadge("prompt", "Parameter Help");
        }

        if (badges.Count > 8)
        {
            var capped = new CadVisualHelperBadgeViewModel[8];
            for (var index = 0; index < capped.Length; index++)
            {
                capped[index] = badges[index];
            }

            return capped;
        }

        return badges;
    }

    private static bool TryMapHelperBadge(
        string normalizedKind,
        bool isRemote,
        out string key,
        out string label)
    {
        key = string.Empty;
        label = string.Empty;

        if (isRemote)
        {
            key = "remote";
            label = "Remote Ghost";
            return true;
        }

        if (normalizedKind.StartsWith("Snap", StringComparison.OrdinalIgnoreCase))
        {
            key = "snap";
            label = "OSNAP";
            return true;
        }

        switch (normalizedKind)
        {
            case "TrackingGuide":
            case "SnapGuide":
                key = "tracking";
                label = "Tracking";
                return true;
            case "SelectionWindow":
            case "SelectionCrossing":
            case "SelectionLasso":
            case "SelectionFence":
            case "SelectionPolygon":
            case "SelectionBounds":
                key = "selection";
                label = "Selection Adorner";
                return true;
            case "Grip":
            case "HotGrip":
                key = "grip";
                label = "Grips";
                return true;
            case "RubberBand":
            case "HelperLine":
            case "PreviewCircle":
            case "PreviewArc":
            case "PickPoint":
            case "Cursor":
                key = "preview";
                label = "Canvas Preview";
                return true;
            case "DynamicDimension":
                key = "dimension";
                label = "Dynamic Dimension";
                return true;
            case "TokenCallout":
            case "Prompt":
                key = "prompt";
                label = "Prompt Guide";
                return true;
            default:
                return false;
        }
    }

    private void OnCollaborationPresenceChanged(object? sender, EventArgs e)
    {
        RxApp.MainThreadScheduler.Schedule(() =>
        {
            var mergedHints = MergeToolVisualHints(_localToolVisualHints);
            ToolVisualHints = mergedHints;
            OverlayScene = BuildOverlayScene(mergedHints);
            RefreshActiveVisualHelpers(mergedHints, _commandRuntime.State);
        });
    }

    private static List<IRenderPrimitive> FilterPrimitivesByEntityType(
        RenderScene scene,
        RenderLayer layer,
        IReadOnlyDictionary<string, bool> overrides)
    {
        var filtered = new List<IRenderPrimitive>(layer.Primitives.Count);
        foreach (var primitive in layer.Primitives)
        {
            if (scene.PrimitiveMetadata.TryGetValue(primitive, out var metadata))
            {
                var entity = metadata.OwnerEntity ?? metadata.SourceEntity;
                if (entity is not null &&
                    overrides.TryGetValue(entity.GetType().Name, out var isVisible) &&
                    !isVisible)
                {
                    continue;
                }
            }

            filtered.Add(primitive);
        }

        return filtered;
    }

    private void UpdateDebugOverlay()
    {
        if (!ShowDebugOverlay)
        {
            DebugBvhBounds = null;
            return;
        }

        var scene = Scene;
        if (scene is null)
        {
            DebugBvhBounds = null;
            return;
        }

        var index = _hitTestIndex ?? scene.SpatialIndex;
        if (index is null)
        {
            DebugBvhBounds = null;
            return;
        }

        _bvhBounds.Clear();
        index.CollectNodeBounds(DebugBvhDepth, _bvhBounds);
        DebugBvhBounds = _bvhBounds.Count == 0 ? null : _bvhBounds.ToArray();
    }

    private async Task ExportStatsAsync(CancellationToken cancellationToken)
    {
        var scene = Scene;
        if (scene is null)
        {
            return;
        }

        var result = await _statsExportService
            .SaveStatsAsync(_statsFileName, cancellationToken)
            .ConfigureAwait(true);
        if (result is null)
        {
            return;
        }

        var json = RenderStatsExporter.ToJson(scene.Stats, indented: true);
        await using var stream = await result.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static string EnsureStatsFileName(string? statsFileName)
    {
        if (string.IsNullOrWhiteSpace(statsFileName))
        {
            return "render-stats.json";
        }

        var extension = Path.GetExtension(statsFileName);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return statsFileName;
        }

        return $"{Path.GetFileNameWithoutExtension(statsFileName)}.json";
    }

    private static RenderOverlayScene BuildOverlayScene(IReadOnlyList<CadToolVisualHint> hints)
    {
        if (hints.Count == 0)
        {
            return RenderOverlayScene.Empty;
        }

        var primitives = new List<RenderOverlayPrimitive>(hints.Count * 4);
        foreach (var hint in hints)
        {
            AppendOverlayPrimitives(hint, primitives);
        }

        return new RenderOverlayScene(primitives);
    }

    private static void AppendOverlayPrimitives(CadToolVisualHint hint, ICollection<RenderOverlayPrimitive> target)
    {
        var normalizedKind = NormalizeHintKind(hint.Kind, out var isRemote);
        var color = ResolveHintColor(hint, normalizedKind, isRemote);
        var secondary = hint.SecondaryAnchor;
        var tertiary = hint.TertiaryAnchor;
        var hasSecondary = secondary.HasValue;

        switch (normalizedKind)
        {
            case "SelectionWindow":
            case "SelectionBounds":
            {
                if (hasSecondary)
                {
                    target.Add(new RenderOverlayPrimitive(
                        Kind: RenderOverlayPrimitiveKind.FilledRectangle,
                        Start: hint.Anchor,
                        End: secondary.GetValueOrDefault(hint.Anchor),
                        Color: color,
                        StrokeWidth: 1.25f,
                        MarkerRadius: 0f,
                        StrokeStyle: RenderOverlayStrokeStyle.Solid,
                        FillColor: WithAlpha(color, 36),
                        Priority: 20));
                }

                AppendLabel(target, hint.Anchor, hint.Text, priority: 28, isRemote);
                return;
            }
            case "SelectionCrossing":
            {
                if (hasSecondary)
                {
                    target.Add(new RenderOverlayPrimitive(
                        Kind: RenderOverlayPrimitiveKind.FilledRectangle,
                        Start: hint.Anchor,
                        End: secondary.GetValueOrDefault(hint.Anchor),
                        Color: color,
                        StrokeWidth: 1.25f,
                        MarkerRadius: 0f,
                        StrokeStyle: RenderOverlayStrokeStyle.Dashed,
                        FillColor: WithAlpha(color, 30),
                        Priority: 20));
                }

                AppendLabel(target, hint.Anchor, hint.Text, priority: 28, isRemote);
                return;
            }
            case "SelectionLasso":
            case "SelectionFence":
            case "SelectionPolygon":
            {
                if (hasSecondary)
                {
                    target.Add(new RenderOverlayPrimitive(
                        Kind: RenderOverlayPrimitiveKind.Line,
                        Start: hint.Anchor,
                        End: secondary.GetValueOrDefault(hint.Anchor),
                        Color: color,
                        StrokeWidth: 1.3f,
                        MarkerRadius: 0f,
                        StrokeStyle: normalizedKind == "SelectionFence"
                            ? RenderOverlayStrokeStyle.Dotted
                            : RenderOverlayStrokeStyle.Dashed,
                        Priority: 22));
                }

                AppendLabel(target, hint.Anchor, hint.Text, priority: 30, isRemote);
                return;
            }
            case "Viewport":
            {
                if (hasSecondary)
                {
                    target.Add(new RenderOverlayPrimitive(
                        Kind: RenderOverlayPrimitiveKind.Rectangle,
                        Start: hint.Anchor,
                        End: secondary.GetValueOrDefault(hint.Anchor),
                        Color: color,
                        StrokeWidth: 1f,
                        MarkerRadius: 0f,
                        StrokeStyle: RenderOverlayStrokeStyle.Dotted,
                        Priority: 16));
                }

                AppendLabel(target, hint.Anchor, hint.Text, priority: 22, isRemote);
                return;
            }
            case "Cursor":
            {
                target.Add(new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.PointMarker,
                    Start: hint.Anchor,
                    End: hint.Anchor,
                    Color: color,
                    StrokeWidth: 1.5f,
                    MarkerRadius: 5f,
                    StrokeStyle: RenderOverlayStrokeStyle.Solid,
                    FillColor: null,
                    Priority: 95));
                AppendLabel(target, hint.Anchor, hint.Text, priority: 102, isRemote);
                return;
            }
            case "RubberBand":
            {
                if (hasSecondary)
                {
                    target.Add(new RenderOverlayPrimitive(
                        Kind: RenderOverlayPrimitiveKind.Line,
                        Start: hint.Anchor,
                        End: secondary.GetValueOrDefault(hint.Anchor),
                        Color: color,
                        StrokeWidth: 1.5f,
                        MarkerRadius: 0f,
                        StrokeStyle: RenderOverlayStrokeStyle.Dashed,
                        Priority: 80));
                }

                target.Add(new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.PointMarker,
                    Start: hint.Anchor,
                    End: hint.Anchor,
                    Color: color,
                    StrokeWidth: 1.25f,
                    MarkerRadius: 3f,
                    Priority: 82));
                AppendLabel(target, hint.Anchor, hint.Text, priority: 90, isRemote);
                return;
            }
            case "HelperLine":
            {
                if (hasSecondary)
                {
                    target.Add(new RenderOverlayPrimitive(
                        Kind: RenderOverlayPrimitiveKind.Line,
                        Start: hint.Anchor,
                        End: secondary.GetValueOrDefault(hint.Anchor),
                        Color: color,
                        StrokeWidth: 1.1f,
                        MarkerRadius: 0f,
                        StrokeStyle: RenderOverlayStrokeStyle.Dotted,
                        Priority: 72));
                }

                AppendLabel(target, hint.Anchor, hint.Text, priority: 78, isRemote);
                return;
            }
            case "PreviewCircle":
            {
                if (hasSecondary)
                {
                    AppendCirclePreview(
                        target,
                        center: hint.Anchor,
                        perimeterPoint: secondary.GetValueOrDefault(hint.Anchor),
                        color: color,
                        strokeStyle: RenderOverlayStrokeStyle.Dashed,
                        priority: 76);
                }

                AppendLabel(target, hint.Anchor, hint.Text, priority: 84, isRemote);
                return;
            }
            case "PreviewArc":
            {
                if (hasSecondary && tertiary.HasValue)
                {
                    AppendArcPreview(
                        target,
                        center: hint.Anchor,
                        startPoint: secondary.GetValueOrDefault(hint.Anchor),
                        endDirectionPoint: tertiary.GetValueOrDefault(hint.Anchor),
                        color: color,
                        strokeStyle: RenderOverlayStrokeStyle.Dashed,
                        priority: 77);
                }

                AppendLabel(target, hint.Anchor, hint.Text, priority: 85, isRemote);
                return;
            }
            case "TrackingGuide":
            {
                if (hasSecondary)
                {
                    target.Add(new RenderOverlayPrimitive(
                        Kind: RenderOverlayPrimitiveKind.Line,
                        Start: hint.Anchor,
                        End: secondary.GetValueOrDefault(hint.Anchor),
                        Color: color,
                        StrokeWidth: 1f,
                        MarkerRadius: 0f,
                        StrokeStyle: RenderOverlayStrokeStyle.Dotted,
                        Priority: 62));
                }

                AppendLabel(target, hint.Anchor, hint.Text, priority: 68, isRemote);
                return;
            }
            case "SnapGuide":
            {
                if (hasSecondary)
                {
                    target.Add(new RenderOverlayPrimitive(
                        Kind: RenderOverlayPrimitiveKind.Line,
                        Start: hint.Anchor,
                        End: secondary.GetValueOrDefault(hint.Anchor),
                        Color: color,
                        StrokeWidth: 1f,
                        MarkerRadius: 0f,
                        StrokeStyle: RenderOverlayStrokeStyle.Dotted,
                        Priority: 70));
                }

                AppendLabel(target, hint.Anchor, hint.Text, priority: 74, isRemote);
                return;
            }
            case "SnapMarker":
            {
                target.Add(new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.DiamondMarker,
                    Start: hint.Anchor,
                    End: hint.Anchor,
                    Color: color,
                    StrokeWidth: 1.5f,
                    MarkerRadius: 5f,
                    StrokeStyle: RenderOverlayStrokeStyle.Solid,
                    FillColor: WithAlpha(color, 48),
                    Priority: 88));
                AppendLabel(target, hint.Anchor, hint.Text, priority: 94, isRemote);
                return;
            }
            case "SnapEndpoint":
            {
                target.Add(new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.SquareMarker,
                    Start: hint.Anchor,
                    End: hint.Anchor,
                    Color: color,
                    StrokeWidth: 1.6f,
                    MarkerRadius: 5.2f,
                    StrokeStyle: RenderOverlayStrokeStyle.Solid,
                    FillColor: WithAlpha(color, 42),
                    Priority: 89));
                AppendLabel(target, hint.Anchor, hint.Text, priority: 95, isRemote);
                return;
            }
            case "SnapMidpoint":
            {
                target.Add(new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.DiamondMarker,
                    Start: hint.Anchor,
                    End: hint.Anchor,
                    Color: color,
                    StrokeWidth: 1.5f,
                    MarkerRadius: 5f,
                    StrokeStyle: RenderOverlayStrokeStyle.Solid,
                    FillColor: WithAlpha(color, 40),
                    Priority: 89));
                target.Add(new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.PointMarker,
                    Start: hint.Anchor,
                    End: hint.Anchor,
                    Color: color,
                    StrokeWidth: 1.1f,
                    MarkerRadius: 2.2f,
                    Priority: 90));
                AppendLabel(target, hint.Anchor, hint.Text, priority: 95, isRemote);
                return;
            }
            case "SnapCenter":
            {
                target.Add(new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.PointMarker,
                    Start: hint.Anchor,
                    End: hint.Anchor,
                    Color: color,
                    StrokeWidth: 1.6f,
                    MarkerRadius: 5.4f,
                    StrokeStyle: RenderOverlayStrokeStyle.Solid,
                    FillColor: null,
                    Priority: 89));
                AppendLabel(target, hint.Anchor, hint.Text, priority: 95, isRemote);
                return;
            }
            case "SnapIntersection":
            case "SnapApparentIntersection":
            {
                var d = 4f;
                target.Add(new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.Line,
                    Start: hint.Anchor + new System.Numerics.Vector2(-d, -d),
                    End: hint.Anchor + new System.Numerics.Vector2(d, d),
                    Color: color,
                    StrokeWidth: 1.2f,
                    MarkerRadius: 0f,
                    Priority: 89));
                target.Add(new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.Line,
                    Start: hint.Anchor + new System.Numerics.Vector2(-d, d),
                    End: hint.Anchor + new System.Numerics.Vector2(d, -d),
                    Color: color,
                    StrokeWidth: 1.2f,
                    MarkerRadius: 0f,
                    Priority: 89));
                AppendLabel(target, hint.Anchor, hint.Text, priority: 95, isRemote);
                return;
            }
            case "Grip":
            {
                target.Add(new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.SquareMarker,
                    Start: hint.Anchor,
                    End: hint.Anchor,
                    Color: color,
                    StrokeWidth: 1.4f,
                    MarkerRadius: 3.8f,
                    StrokeStyle: RenderOverlayStrokeStyle.Solid,
                    FillColor: WithAlpha(color, 96),
                    Priority: 84));
                AppendLabel(target, hint.Anchor, hint.Text, priority: 92, isRemote);
                return;
            }
            case "HotGrip":
            {
                target.Add(new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.SquareMarker,
                    Start: hint.Anchor,
                    End: hint.Anchor,
                    Color: color,
                    StrokeWidth: 1.6f,
                    MarkerRadius: 4.6f,
                    StrokeStyle: RenderOverlayStrokeStyle.Solid,
                    FillColor: WithAlpha(color, 110),
                    Priority: 85));
                AppendLabel(target, hint.Anchor, hint.Text, priority: 93, isRemote);
                return;
            }
            case "PickPoint":
            {
                target.Add(new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.SquareMarker,
                    Start: hint.Anchor,
                    End: hint.Anchor,
                    Color: color,
                    StrokeWidth: 1.6f,
                    MarkerRadius: 5.2f,
                    FillColor: WithAlpha(color, 92),
                    Priority: 83));
                target.Add(new RenderOverlayPrimitive(
                    Kind: RenderOverlayPrimitiveKind.PointMarker,
                    Start: hint.Anchor,
                    End: hint.Anchor,
                    Color: color,
                    StrokeWidth: 1.2f,
                    MarkerRadius: 3.8f,
                    Priority: 84));
                AppendLabel(target, hint.Anchor, hint.Text, priority: 91, isRemote);
                return;
            }
            case "Prompt":
            {
                AppendLabel(target, hint.Anchor, hint.Text, priority: 104, isRemote);
                return;
            }
            case "TokenCallout":
            {
                AppendLabel(target, hint.Anchor, hint.Text, priority: 106, isRemote);
                return;
            }
            case "DynamicDimension":
            {
                if (hasSecondary)
                {
                    target.Add(new RenderOverlayPrimitive(
                        Kind: RenderOverlayPrimitiveKind.Line,
                        Start: hint.Anchor,
                        End: secondary.GetValueOrDefault(hint.Anchor),
                        Color: color,
                        StrokeWidth: 1.1f,
                        MarkerRadius: 0f,
                        StrokeStyle: RenderOverlayStrokeStyle.Dotted,
                        Priority: 86));
                }

                AppendLabel(target, hint.Anchor, hint.Text, priority: 108, isRemote);
                return;
            }
        }

        if (normalizedKind.StartsWith("Snap", StringComparison.OrdinalIgnoreCase))
        {
            target.Add(new RenderOverlayPrimitive(
                Kind: RenderOverlayPrimitiveKind.DiamondMarker,
                Start: hint.Anchor,
                End: hint.Anchor,
                Color: color,
                StrokeWidth: 1.5f,
                MarkerRadius: 5f,
                FillColor: WithAlpha(color, 48),
                Priority: 88));
            AppendLabel(target, hint.Anchor, hint.Text, priority: 94, isRemote);
            return;
        }

        if (hasSecondary)
        {
            target.Add(new RenderOverlayPrimitive(
                Kind: RenderOverlayPrimitiveKind.Line,
                Start: hint.Anchor,
                End: secondary.GetValueOrDefault(hint.Anchor),
                Color: color,
                StrokeWidth: 1.25f,
                MarkerRadius: 0f,
                StrokeStyle: RenderOverlayStrokeStyle.Solid,
                Priority: 60));
        }
        else
        {
            target.Add(new RenderOverlayPrimitive(
                Kind: RenderOverlayPrimitiveKind.PointMarker,
                Start: hint.Anchor,
                End: hint.Anchor,
                Color: color,
                StrokeWidth: 1.25f,
                MarkerRadius: 3.5f,
                Priority: 60));
        }

        AppendLabel(target, hint.Anchor, hint.Text, priority: 70, isRemote);
    }

    private static void AppendCirclePreview(
        ICollection<RenderOverlayPrimitive> target,
        System.Numerics.Vector2 center,
        System.Numerics.Vector2 perimeterPoint,
        RenderColor color,
        RenderOverlayStrokeStyle strokeStyle,
        int priority)
    {
        var radius = System.Numerics.Vector2.Distance(center, perimeterPoint);
        if (!(radius > 1e-4f))
        {
            return;
        }

        var segmentCount = Math.Clamp((int)MathF.Round(radius * 0.5f), 24, 96);
        var points = new List<System.Numerics.Vector2>(segmentCount + 1);
        for (var index = 0; index <= segmentCount; index++)
        {
            var t = (MathF.PI * 2f) * index / segmentCount;
            points.Add(center + new System.Numerics.Vector2(MathF.Cos(t) * radius, MathF.Sin(t) * radius));
        }

        AppendPathLines(target, points, color, strokeStyle, priority);
    }

    private static void AppendArcPreview(
        ICollection<RenderOverlayPrimitive> target,
        System.Numerics.Vector2 center,
        System.Numerics.Vector2 startPoint,
        System.Numerics.Vector2 endDirectionPoint,
        RenderColor color,
        RenderOverlayStrokeStyle strokeStyle,
        int priority)
    {
        var radius = System.Numerics.Vector2.Distance(center, startPoint);
        if (!(radius > 1e-4f))
        {
            return;
        }

        var direction = endDirectionPoint - center;
        if (direction.LengthSquared() <= 1e-8f)
        {
            return;
        }

        var normalizedEnd = center + System.Numerics.Vector2.Normalize(direction) * radius;
        var startAngle = MathF.Atan2(startPoint.Y - center.Y, startPoint.X - center.X);
        var endAngle = MathF.Atan2(normalizedEnd.Y - center.Y, normalizedEnd.X - center.X);
        var sweep = endAngle - startAngle;
        while (sweep < 0f)
        {
            sweep += MathF.PI * 2f;
        }

        var segmentCount = Math.Clamp((int)MathF.Round((sweep / (MathF.PI * 2f)) * 96f), 8, 96);
        var points = new List<System.Numerics.Vector2>(segmentCount + 1);
        for (var index = 0; index <= segmentCount; index++)
        {
            var t = startAngle + (sweep * index / segmentCount);
            points.Add(center + new System.Numerics.Vector2(MathF.Cos(t) * radius, MathF.Sin(t) * radius));
        }

        AppendPathLines(target, points, color, strokeStyle, priority);
    }

    private static void AppendPathLines(
        ICollection<RenderOverlayPrimitive> target,
        IReadOnlyList<System.Numerics.Vector2> points,
        RenderColor color,
        RenderOverlayStrokeStyle strokeStyle,
        int priority)
    {
        if (points.Count < 2)
        {
            return;
        }

        for (var index = 1; index < points.Count; index++)
        {
            var start = points[index - 1];
            var end = points[index];
            if (System.Numerics.Vector2.DistanceSquared(start, end) <= 1e-8f)
            {
                continue;
            }

            target.Add(new RenderOverlayPrimitive(
                Kind: RenderOverlayPrimitiveKind.Line,
                Start: start,
                End: end,
                Color: color,
                StrokeWidth: 1.3f,
                MarkerRadius: 0f,
                StrokeStyle: strokeStyle,
                Priority: priority));
        }
    }

    private static string NormalizeHintKind(string? kind, out bool isRemote)
    {
        isRemote = false;
        if (string.IsNullOrWhiteSpace(kind))
        {
            return string.Empty;
        }

        var normalized = kind.Trim();
        if (normalized.StartsWith("Remote", StringComparison.OrdinalIgnoreCase))
        {
            isRemote = true;
            normalized = normalized["Remote".Length..];
        }

        if (normalized.StartsWith("Tool", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["Tool".Length..];
        }

        return normalized;
    }

    private static void AppendLabel(
        ICollection<RenderOverlayPrimitive> target,
        System.Numerics.Vector2 anchor,
        string? text,
        int priority,
        bool isRemote)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        target.Add(new RenderOverlayPrimitive(
            Kind: RenderOverlayPrimitiveKind.Text,
            Start: anchor + new System.Numerics.Vector2(6f, -6f),
            End: anchor,
            Color: RenderColor.FromRgb(247, 247, 247),
            StrokeWidth: 1f,
            MarkerRadius: 0f,
            Text: text,
            StrokeStyle: RenderOverlayStrokeStyle.Solid,
            FillColor: isRemote
                ? new RenderColor(14, 24, 38, 220)
                : new RenderColor(12, 16, 22, 216),
            Priority: priority));
    }

    private static RenderColor ResolveHintColor(CadToolVisualHint hint, string normalizedKind, bool isRemote)
    {
        if (!string.IsNullOrWhiteSpace(hint.Color) &&
            TryParseColor(hint.Color!, out var parsed))
        {
            return parsed;
        }

        if (isRemote)
        {
            return RenderColor.FromRgb(255, 184, 77);
        }

        return normalizedKind switch
        {
            "SelectionWindow" => RenderColor.FromRgb(102, 214, 112),
            "SelectionBounds" => RenderColor.FromRgb(102, 214, 112),
            "SelectionCrossing" => RenderColor.FromRgb(69, 150, 255),
            "SelectionLasso" => RenderColor.FromRgb(69, 150, 255),
            "SelectionFence" => RenderColor.FromRgb(255, 191, 89),
            "SelectionPolygon" => RenderColor.FromRgb(94, 207, 148),
            "Grip" => RenderColor.FromRgb(57, 189, 255),
            "HotGrip" => RenderColor.FromRgb(255, 109, 82),
            "SnapMarker" => RenderColor.FromRgb(242, 223, 78),
            "SnapEndpoint" => RenderColor.FromRgb(98, 229, 255),
            "SnapMidpoint" => RenderColor.FromRgb(130, 244, 162),
            "SnapCenter" => RenderColor.FromRgb(250, 214, 108),
            "SnapNode" => RenderColor.FromRgb(210, 210, 210),
            "SnapQuadrant" => RenderColor.FromRgb(173, 206, 255),
            "SnapIntersection" => RenderColor.FromRgb(255, 168, 122),
            "SnapApparentIntersection" => RenderColor.FromRgb(255, 132, 108),
            "SnapPerpendicular" => RenderColor.FromRgb(166, 231, 255),
            "SnapTangent" => RenderColor.FromRgb(255, 203, 133),
            "SnapNearest" => RenderColor.FromRgb(218, 218, 218),
            "SnapExtension" => RenderColor.FromRgb(186, 240, 124),
            "SnapParallel" => RenderColor.FromRgb(186, 240, 124),
            "TrackingGuide" => RenderColor.FromRgb(186, 240, 124),
            "SnapGuide" => RenderColor.FromRgb(242, 223, 78),
            "HelperLine" => RenderColor.FromRgb(148, 212, 255),
            "RubberBand" => RenderColor.FromRgb(84, 226, 255),
            "PickPoint" => RenderColor.FromRgb(255, 219, 74),
            "PreviewCircle" => RenderColor.FromRgb(84, 226, 255),
            "PreviewArc" => RenderColor.FromRgb(84, 226, 255),
            "DynamicDimension" => RenderColor.FromRgb(159, 230, 255),
            "TokenCallout" => RenderColor.FromRgb(216, 244, 255),
            "Prompt" => RenderColor.FromRgb(245, 245, 245),
            "Viewport" => RenderColor.FromRgb(158, 189, 255),
            _ => RenderColor.FromRgb(0, 255, 255)
        };
    }

    private static RenderColor WithAlpha(RenderColor color, byte alpha)
    {
        return new RenderColor(color.R, color.G, color.B, alpha);
    }

    private static bool TryParseColor(string value, out RenderColor color)
    {
        color = RenderColor.FromRgb(0, 255, 255);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hex = value.Trim();
        if (hex.StartsWith("#", StringComparison.Ordinal))
        {
            hex = hex[1..];
        }

        if (hex.Length == 8)
        {
            if (!byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var alpha) ||
                !byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var red8) ||
                !byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var green8) ||
                !byte.TryParse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var blue8))
            {
                return false;
            }

            color = new RenderColor(red8, green8, blue8, alpha);
            return true;
        }

        if (hex.Length != 6)
        {
            return false;
        }

        if (!byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        color = RenderColor.FromRgb(red, green, blue);
        return true;
    }

    private static bool TryResolveDynamicInputHint(
        IReadOnlyList<CadToolVisualHint> hints,
        out CadToolVisualHint hint)
    {
        hint = default!;
        var hasValue = false;
        var bestScore = int.MaxValue;
        for (var index = 0; index < hints.Count; index++)
        {
            var candidate = hints[index];
            if (string.IsNullOrWhiteSpace(candidate.Text))
            {
                continue;
            }

            var normalizedKind = NormalizeHintKind(candidate.Kind, out var isRemote);
            if (isRemote)
            {
                continue;
            }

            var score = normalizedKind switch
            {
                "TokenCallout" => 0,
                "Prompt" => 0,
                "DynamicDimension" => 1,
                "PreviewCircle" => 1,
                "PreviewArc" => 1,
                "RubberBand" => 2,
                "HelperLine" => 3,
                "SelectionLasso" => 4,
                "SelectionFence" => 4,
                "SelectionPolygon" => 4,
                "SnapMarker" => 4,
                "PickPoint" => 5,
                "TrackingGuide" => 6,
                _ => 10
            };

            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            hint = candidate;
            hasValue = true;
            if (score == 0)
            {
                break;
            }
        }

        return hasValue;
    }

    private sealed class NullRenderStatsExportService : IRenderStatsExportService
    {
        public static readonly NullRenderStatsExportService Instance = new();

        public Task<RenderStatsExportResult?> SaveStatsAsync(string? suggestedFileName, CancellationToken cancellationToken)
        {
            return Task.FromResult<RenderStatsExportResult?>(null);
        }
    }

    private sealed class NullCadCommandRuntime : ICadCommandRuntime
    {
        public static readonly NullCadCommandRuntime Instance = new();

        public CadPromptState State { get; private set; } = CadPromptState.Idle;
        public string? LastCommandInput => null;
        public event EventHandler<CadPromptState>? StateChanged;
        public event EventHandler<CadCommandExecutedEventArgs>? CommandExecuted
        {
            add { }
            remove { }
        }

        public void BeginCommand(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return;
            }

            var trimmed = commandName.Trim();
            State = CadPromptState.Idle with
            {
                Prompt = trimmed,
                ActiveCommand = trimmed,
                IsActive = true
            };
            StateChanged?.Invoke(this, State);
        }

        public void Cancel()
        {
            State = CadPromptState.Idle with
            {
                LastMessage = "*Cancel*"
            };
            StateChanged?.Invoke(this, State);
        }

        public CadPromptState Preview(string input, int cursorIndex)
        {
            return State;
        }

        public ValueTask<CadPromptResolution> SubmitAsync(
            string input,
            ACadInspector.Editing.Sessions.ICadEditorSession? session,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new CadPromptResolution(
                Handled: false,
                Result: null,
                State: State));
        }

        public ValueTask<CadPromptResolution> SubmitTokenAsync(
            CadPromptToken token,
            ACadInspector.Editing.Sessions.ICadEditorSession? session,
            bool commit = false,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new CadPromptResolution(
                Handled: false,
                Result: null,
                State: State));
        }
    }
}
