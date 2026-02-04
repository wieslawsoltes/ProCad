using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ACadInspector.Rendering;
using ACadInspector.Services;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Objects.Evaluations;
using ACadSharp.Tables;
using ReactiveUI;

namespace ACadInspector.ViewModels;

public sealed class CadDynamicBlockInspectorViewModel : ViewModelBase, IDisposable
{
    private readonly CompositeDisposable _subscriptions = new();
    private readonly CadDynamicBlockOverrideService _overrideService;
    private readonly DynamicBlockOverrideSet _overrideSet;

    public Insert Insert { get; }
    public BlockRecord Block { get; }
    public ReadOnlyObservableCollection<CadDynamicBlockParameterViewModelBase> Parameters { get; }
    public ReadOnlyObservableCollection<CadDynamicBlockActionViewModel> Actions { get; }
    public bool HasParameters => Parameters.Count > 0;
    public bool HasActions => Actions.Count > 0;
    public bool HasOverrides => _overrideSet.NumericByName.Count > 0 ||
                                _overrideSet.NumericById.Count > 0 ||
                                _overrideSet.Strings.Count > 0 ||
                                !string.IsNullOrWhiteSpace(_overrideSet.VisibilityStateName);

    public CadDynamicBlockInspectorViewModel(
        Insert insert,
        BlockRecord block,
        CadDynamicBlockOverrideService overrideService)
    {
        Insert = insert;
        Block = block;
        _overrideService = overrideService;
        _overrideSet = overrideService.GetOrCreateOverrides(insert);

        var parameters = new ObservableCollection<CadDynamicBlockParameterViewModelBase>();
        var actions = new ObservableCollection<CadDynamicBlockActionViewModel>();
        Parameters = new ReadOnlyObservableCollection<CadDynamicBlockParameterViewModelBase>(parameters);
        Actions = new ReadOnlyObservableCollection<CadDynamicBlockActionViewModel>(actions);

        var propertySet = TryCreatePropertySet(insert);
        BuildMetadata(block, propertySet, parameters, actions);
        WireOverrides(parameters, propertySet);
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    private void BuildMetadata(
        BlockRecord block,
        DynamicBlockPropertySet? propertySet,
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
                    parameters.Add(CreateLinearParameter(linear, propertySet));
                    break;
                case BlockFlipParameter flip:
                    parameters.Add(CreateFlipParameter(flip, propertySet));
                    break;
                case BlockVisibilityParameter visibility:
                    parameters.Add(CreateVisibilityParameter(visibility, propertySet));
                    break;
                case BlockAction action:
                    actions.Add(CreateAction(action));
                    break;
            }
        }
    }

    private CadDynamicBlockLinearParameterViewModel CreateLinearParameter(
        BlockLinearParameter parameter,
        DynamicBlockPropertySet? propertySet)
    {
        var delta = parameter.SecondPoint - parameter.FirstPoint;
        var baseLength = Math.Sqrt((delta.X * delta.X) + (delta.Y * delta.Y) + (delta.Z * delta.Z));
        var currentValue = baseLength;
        if (propertySet is not null &&
            propertySet.TryGetNumericValue(parameter.ElementName, parameter.Label, parameter.Id, parameter.Value1071, out var value))
        {
            currentValue = value;
        }

        var viewModel = new CadDynamicBlockLinearParameterViewModel(
            parameter.ElementName ?? string.Empty,
            parameter.Label,
            parameter.ElementName,
            parameter.Id,
            parameter.Value1071,
            baseLength,
            currentValue);

        if (_overrideSet.TryGetNumericOverride(parameter.ElementName, parameter.Label, parameter.Id, parameter.Value1071, out var overrideValue))
        {
            viewModel.OverrideEnabled = true;
            viewModel.OverrideValue = overrideValue;
        }

        return viewModel;
    }

    private CadDynamicBlockFlipParameterViewModel CreateFlipParameter(
        BlockFlipParameter parameter,
        DynamicBlockPropertySet? propertySet)
    {
        var isFlipped = false;
        if (propertySet is not null && !string.IsNullOrWhiteSpace(parameter.FlippedStateName))
        {
            isFlipped = propertySet.ContainsString(parameter.FlippedStateName);
        }

        var viewModel = new CadDynamicBlockFlipParameterViewModel(
            parameter.ElementName ?? string.Empty,
            parameter.Caption,
            parameter.ElementName,
            parameter.Id,
            parameter.Value1071,
            parameter.BaseStateName,
            parameter.FlippedStateName,
            isFlipped);

        if (_overrideSet.TryGetFlipOverride(parameter.FlippedStateName, parameter.BaseStateName, out var overrideFlip))
        {
            viewModel.OverrideEnabled = true;
            viewModel.IsFlipped = overrideFlip;
        }

        return viewModel;
    }

    private CadDynamicBlockVisibilityParameterViewModel CreateVisibilityParameter(
        BlockVisibilityParameter parameter,
        DynamicBlockPropertySet? propertySet)
    {
        var states = new List<string>(parameter.States.Keys);
        var current = ResolveVisibilityState(parameter, propertySet);
        var viewModel = new CadDynamicBlockVisibilityParameterViewModel(
            parameter.Name ?? parameter.ElementName ?? "Visibility",
            parameter.Description,
            states,
            current);

        if (!string.IsNullOrWhiteSpace(_overrideSet.VisibilityStateName))
        {
            viewModel.OverrideEnabled = true;
            viewModel.SelectedState = _overrideSet.VisibilityStateName;
        }

        return viewModel;
    }

    private static CadDynamicBlockActionViewModel CreateAction(BlockAction action)
    {
        var name = action.ElementName ?? action.GetType().Name;
        var type = action.GetType().Name;
        var count = action.Entities?.Count ?? 0;
        return new CadDynamicBlockActionViewModel(name, type, count);
    }

    private void WireOverrides(
        IEnumerable<CadDynamicBlockParameterViewModelBase> parameters,
        DynamicBlockPropertySet? propertySet)
    {
        foreach (var parameter in parameters)
        {
            switch (parameter)
            {
                case CadDynamicBlockLinearParameterViewModel linear:
                    _subscriptions.Add(linear.WhenAnyValue(x => x.OverrideEnabled, x => x.OverrideValue)
                        .Subscribe(_ => ApplyLinearOverride(linear)));
                    break;
                case CadDynamicBlockFlipParameterViewModel flip:
                    _subscriptions.Add(flip.WhenAnyValue(x => x.OverrideEnabled, x => x.IsFlipped)
                        .Subscribe(_ => ApplyFlipOverride(flip)));
                    break;
                case CadDynamicBlockVisibilityParameterViewModel visibility:
                    _subscriptions.Add(visibility.WhenAnyValue(x => x.OverrideEnabled, x => x.SelectedState)
                        .Subscribe(_ => ApplyVisibilityOverride(visibility)));
                    break;
            }
        }
    }

    private void ApplyLinearOverride(CadDynamicBlockLinearParameterViewModel parameter)
    {
        if (parameter.OverrideEnabled)
        {
            _overrideSet.SetNumericOverride(
                parameter.ElementName,
                parameter.Label,
                parameter.Id,
                parameter.Value1071,
                parameter.OverrideValue);
        }
        else
        {
            _overrideSet.ClearNumericOverride(
                parameter.ElementName,
                parameter.Label,
                parameter.Id,
                parameter.Value1071);
        }

        _overrideService.NotifyChanged();
    }

    private void ApplyFlipOverride(CadDynamicBlockFlipParameterViewModel parameter)
    {
        if (parameter.OverrideEnabled)
        {
            _overrideSet.SetFlipOverride(parameter.FlippedStateName, parameter.BaseStateName, parameter.IsFlipped);
        }
        else
        {
            _overrideSet.ClearFlipOverride(parameter.FlippedStateName, parameter.BaseStateName);
        }

        _overrideService.NotifyChanged();
    }

    private void ApplyVisibilityOverride(CadDynamicBlockVisibilityParameterViewModel parameter)
    {
        if (parameter.OverrideEnabled && !string.IsNullOrWhiteSpace(parameter.SelectedState))
        {
            _overrideSet.VisibilityStateName = parameter.SelectedState;
        }
        else
        {
            _overrideSet.VisibilityStateName = null;
        }

        _overrideService.NotifyChanged();
    }

    private static string? ResolveVisibilityState(
        BlockVisibilityParameter parameter,
        DynamicBlockPropertySet? propertySet)
    {
        if (propertySet is null)
        {
            return null;
        }

        foreach (var value in propertySet.Strings)
        {
            if (parameter.States.ContainsKey(value))
            {
                return value;
            }
        }

        return null;
    }

    private static DynamicBlockPropertySet? TryCreatePropertySet(Insert insert)
    {
        if (insert.XDictionary is null)
        {
            return null;
        }

        if (!insert.XDictionary.TryGetEntry<CadDictionary>("AcDbBlockRepresentation", out var representation))
        {
            return null;
        }

        if (!representation.TryGetEntry<CadDictionary>("AppDataCache", out var appDataCache))
        {
            return null;
        }

        if (!appDataCache.TryGetEntry<CadDictionary>("ACAD_ENHANCEDBLOCKDATA", out var enhancedBlockData))
        {
            return null;
        }

        return DynamicBlockPropertySet.Create(enhancedBlockData);
    }
}
