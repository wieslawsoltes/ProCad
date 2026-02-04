using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Rendering;
using ACadInspector.Services;
using ACadSharp;
using ACadSharp.Objects.Evaluations;
using ACadSharp.Tables;
using ACadSharp.Types.Units;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadBlockEditorViewModel : CadDocumentViewModelBase
{
    private const string AllVisibilityState = "(All)";

    public CadDocument Document { get; }
    public BlockRecord Block { get; }
    public CadRenderViewModel Render { get; }
    public IReadOnlyList<UnitsType> UnitOptions { get; }
    public IReadOnlyList<string> VisibilityStateOptions { get; }
    public bool HasVisibilityStates => VisibilityStateOptions.Count > 0;
    public ReadOnlyObservableCollection<CadDynamicBlockParameterViewModelBase> DynamicParameters { get; }
    public ReadOnlyObservableCollection<CadDynamicBlockActionViewModel> DynamicActions { get; }
    public bool HasDynamicMetadata => DynamicParameters.Count > 0 || DynamicActions.Count > 0;

    private readonly ICadRenderSceneBuilder _sceneBuilder;
    private readonly CadRenderSceneSettings _baseSettings;
    private readonly CadRenderLayoutSelection _layoutSelection;
    private readonly string? _documentPath;
    private readonly BlockEditorDynamicOverrideProvider _overrideProvider;
    private CancellationTokenSource? _rebuildCts;
    private bool _suppressUpdates;

    [Reactive]
    public partial string BlockName { get; set; }

    [Reactive]
    public partial string BlockDescription { get; set; }

    [Reactive]
    public partial UnitsType BlockUnits { get; set; }

    [Reactive]
    public partial bool ShowAttributes { get; set; } = true;

    [Reactive]
    public partial bool ShowAttributeDefinitions { get; set; } = true;

    [Reactive]
    public partial string SelectedVisibilityState { get; set; } = AllVisibilityState;

    public CadBlockEditorViewModel(
        CadDocument document,
        BlockRecord block,
        CadRenderViewModel render,
        ICadRenderSceneBuilder sceneBuilder,
        CadRenderSceneSettings baseSettings,
        CadRenderLayoutSelection layoutSelection,
        string? documentPath,
        BlockEditorDynamicOverrideProvider overrideProvider)
    {
        Document = document;
        Block = block;
        Render = render;
        _sceneBuilder = sceneBuilder;
        _baseSettings = baseSettings;
        _layoutSelection = layoutSelection;
        _documentPath = documentPath;
        _overrideProvider = overrideProvider;
        UnitOptions = (UnitsType[])Enum.GetValues(typeof(UnitsType));
        VisibilityStateOptions = BuildVisibilityStateOptions(block);
        var parameters = new ObservableCollection<CadDynamicBlockParameterViewModelBase>();
        var actions = new ObservableCollection<CadDynamicBlockActionViewModel>();
        DynamicParameters = new ReadOnlyObservableCollection<CadDynamicBlockParameterViewModelBase>(parameters);
        DynamicActions = new ReadOnlyObservableCollection<CadDynamicBlockActionViewModel>(actions);
        BuildDynamicMetadata(block, parameters, actions);
        BlockName = block.Name;
        BlockDescription = block.BlockEntity?.Comments ?? string.Empty;
        BlockUnits = block.Units;
        Title = $"Block: {block.Name}";

        if (VisibilityStateOptions.Count > 0)
        {
            SelectedVisibilityState = VisibilityStateOptions[0];
        }

        this.WhenAnyValue(x => x.BlockName)
            .Skip(1)
            .Subscribe(UpdateBlockName);

        this.WhenAnyValue(x => x.BlockDescription)
            .Skip(1)
            .Subscribe(UpdateBlockDescription);

        this.WhenAnyValue(x => x.BlockUnits)
            .Skip(1)
            .Subscribe(UpdateBlockUnits);

        this.WhenAnyValue(x => x.ShowAttributes, x => x.ShowAttributeDefinitions)
            .Skip(1)
            .Subscribe(_ => QueueRebuild());

        this.WhenAnyValue(x => x.SelectedVisibilityState)
            .Skip(1)
            .Subscribe(_ => QueueRebuild());

        foreach (var parameter in DynamicParameters)
        {
            switch (parameter)
            {
                case CadDynamicBlockLinearParameterViewModel linear:
                    linear.WhenAnyValue(x => x.OverrideEnabled, x => x.OverrideValue)
                        .Subscribe(_ => QueueRebuild());
                    break;
                case CadDynamicBlockFlipParameterViewModel flip:
                    flip.WhenAnyValue(x => x.OverrideEnabled, x => x.IsFlipped)
                        .Subscribe(_ => QueueRebuild());
                    break;
            }
        }
    }

    private void QueueRebuild()
    {
        _ = RebuildSceneAsync();
    }

    private void UpdateBlockName(string? value)
    {
        if (_suppressUpdates)
        {
            return;
        }

        var next = value?.Trim();
        if (string.IsNullOrWhiteSpace(next))
        {
            _suppressUpdates = true;
            BlockName = Block.Name;
            _suppressUpdates = false;
            return;
        }

        if (!string.Equals(Block.Name, next, StringComparison.Ordinal))
        {
            Block.Name = next;
            Title = $"Block: {next}";
        }
    }

    private void UpdateBlockDescription(string? value)
    {
        if (_suppressUpdates)
        {
            return;
        }

        var next = value?.Trim() ?? string.Empty;
        if (Block.BlockEntity is null)
        {
            return;
        }

        if (!string.Equals(Block.BlockEntity.Comments, next, StringComparison.Ordinal))
        {
            Block.BlockEntity.Comments = next;
        }
    }

    private void UpdateBlockUnits(UnitsType units)
    {
        if (_suppressUpdates)
        {
            return;
        }

        if (Block.Units != units)
        {
            Block.Units = units;
        }
    }

    private async Task RebuildSceneAsync()
    {
        _rebuildCts?.Cancel();
        var cts = new CancellationTokenSource();
        _rebuildCts = cts;

        var stateName = string.Equals(SelectedVisibilityState, AllVisibilityState, StringComparison.Ordinal)
            ? null
            : SelectedVisibilityState;
        var overrides = BuildOverrideSet(stateName);
        _overrideProvider.Overrides = overrides;
        var baseSettings = _baseSettings
            .WithDynamicBlockOverrides(_overrideProvider)
            .WithAttributeVisibility(ShowAttributes, ShowAttributeDefinitions)
            .WithDynamicBlockVisibilityState(stateName);
        var settings = CadRenderSettingsBuilder.Build(Document, _documentPath, baseSettings, _layoutSelection);

        RenderScene? scene = null;
        try
        {
            scene = await Task.Run(() => _sceneBuilder.BuildBlock(Document, Block, settings), cts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested)
        {
            return;
        }

        RxApp.MainThreadScheduler.Schedule(() => Render.Scene = scene);
    }

    private DynamicBlockOverrideSet? BuildOverrideSet(string? stateName)
    {
        DynamicBlockOverrideSet? overrides = null;

        void EnsureOverrides()
        {
            overrides ??= new DynamicBlockOverrideSet();
        }

        if (!string.IsNullOrWhiteSpace(stateName))
        {
            EnsureOverrides();
            overrides!.VisibilityStateName = stateName;
        }

        foreach (var parameter in DynamicParameters)
        {
            switch (parameter)
            {
                case CadDynamicBlockLinearParameterViewModel linear when linear.OverrideEnabled:
                    EnsureOverrides();
                    overrides!.SetNumericOverride(
                        linear.ElementName,
                        linear.Label,
                        linear.Id,
                        linear.Value1071,
                        linear.OverrideValue);
                    break;
                case CadDynamicBlockFlipParameterViewModel flip when flip.OverrideEnabled:
                    EnsureOverrides();
                    overrides!.SetFlipOverride(flip.FlippedStateName, flip.BaseStateName, flip.IsFlipped);
                    break;
            }
        }

        return overrides;
    }

    private static void BuildDynamicMetadata(
        BlockRecord block,
        ObservableCollection<CadDynamicBlockParameterViewModelBase> parameters,
        ObservableCollection<CadDynamicBlockActionViewModel> actions)
    {
        if (block?.EvaluationGraph is null)
        {
            return;
        }

        foreach (var node in block.EvaluationGraph.Nodes)
        {
            if (node?.Expression is null)
            {
                continue;
            }

            switch (node.Expression)
            {
                case BlockLinearParameter linear:
                    var delta = linear.SecondPoint - linear.FirstPoint;
                    var baseLength = Math.Sqrt((delta.X * delta.X) + (delta.Y * delta.Y) + (delta.Z * delta.Z));
                    parameters.Add(new CadDynamicBlockLinearParameterViewModel(
                        linear.ElementName ?? string.Empty,
                        linear.Label,
                        linear.ElementName,
                        linear.Id,
                        linear.Value1071,
                        baseLength,
                        baseLength));
                    break;
                case BlockFlipParameter flip:
                    parameters.Add(new CadDynamicBlockFlipParameterViewModel(
                        flip.ElementName ?? string.Empty,
                        flip.Caption,
                        flip.ElementName,
                        flip.Id,
                        flip.Value1071,
                        flip.BaseStateName,
                        flip.FlippedStateName,
                        currentValue: false));
                    break;
                case BlockVisibilityParameter visibility:
                    parameters.Add(new CadDynamicBlockVisibilityParameterViewModel(
                        visibility.Name ?? visibility.ElementName ?? "Visibility",
                        visibility.Description,
                        new List<string>(visibility.States.Keys),
                        currentState: null));
                    break;
                case BlockAction action:
                    actions.Add(new CadDynamicBlockActionViewModel(
                        action.ElementName ?? action.GetType().Name,
                        action.GetType().Name,
                        action.Entities?.Count ?? 0));
                    break;
            }
        }
    }

    private static IReadOnlyList<string> BuildVisibilityStateOptions(BlockRecord block)
    {
        if (block?.EvaluationGraph is null)
        {
            return Array.Empty<string>();
        }

        BlockVisibilityParameter? parameter = null;
        foreach (var node in block.EvaluationGraph.Nodes)
        {
            if (node?.Expression is BlockVisibilityParameter visibility)
            {
                parameter = visibility;
                break;
            }
        }

        if (parameter is null || parameter.States.Count == 0)
        {
            return Array.Empty<string>();
        }

        var states = new List<string>(parameter.States.Count + 1)
        {
            AllVisibilityState
        };
        foreach (var name in parameter.States.Keys)
        {
            states.Add(name);
        }

        return states;
    }
}
