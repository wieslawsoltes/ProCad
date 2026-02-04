using System;
using System.Collections.Generic;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public abstract partial class CadDynamicBlockParameterViewModelBase : ViewModelBase
{
    public string Name { get; }
    public string Type { get; }
    public string? Label { get; }

    protected CadDynamicBlockParameterViewModelBase(string name, string type, string? label)
    {
        Name = name;
        Type = type;
        Label = label;
    }
}

public sealed partial class CadDynamicBlockLinearParameterViewModel : CadDynamicBlockParameterViewModelBase
{
    public string? ElementName { get; }
    public int? Id { get; }
    public int? Value1071 { get; }
    public double BaseLength { get; }
    public double CurrentValue { get; }
    public string CurrentValueLabel => CurrentValue.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    [Reactive]
    public partial bool OverrideEnabled { get; set; }

    [Reactive]
    public partial double OverrideValue { get; set; }

    public CadDynamicBlockLinearParameterViewModel(
        string name,
        string? label,
        string? elementName,
        int? id,
        int? value1071,
        double baseLength,
        double currentValue)
        : base(name, "Linear", label)
    {
        ElementName = elementName;
        Id = id;
        Value1071 = value1071;
        BaseLength = baseLength;
        CurrentValue = currentValue;
        OverrideValue = currentValue;
    }
}

public sealed partial class CadDynamicBlockFlipParameterViewModel : CadDynamicBlockParameterViewModelBase
{
    public string? ElementName { get; }
    public int? Id { get; }
    public int? Value1071 { get; }
    public string? BaseStateName { get; }
    public string? FlippedStateName { get; }
    public bool CurrentValue { get; }

    public string CurrentStateLabel => CurrentValue ? "Flipped" : "Base";

    [Reactive]
    public partial bool OverrideEnabled { get; set; }

    [Reactive]
    public partial bool IsFlipped { get; set; }

    public CadDynamicBlockFlipParameterViewModel(
        string name,
        string? label,
        string? elementName,
        int? id,
        int? value1071,
        string? baseStateName,
        string? flippedStateName,
        bool currentValue)
        : base(name, "Flip", label)
    {
        ElementName = elementName;
        Id = id;
        Value1071 = value1071;
        BaseStateName = baseStateName;
        FlippedStateName = flippedStateName;
        CurrentValue = currentValue;
        IsFlipped = currentValue;
    }
}

public sealed partial class CadDynamicBlockVisibilityParameterViewModel : CadDynamicBlockParameterViewModelBase
{
    public IReadOnlyList<string> States { get; }
    public string? CurrentState { get; }

    [Reactive]
    public partial bool OverrideEnabled { get; set; }

    [Reactive]
    public partial string? SelectedState { get; set; }

    public CadDynamicBlockVisibilityParameterViewModel(
        string name,
        string? label,
        IReadOnlyList<string> states,
        string? currentState)
        : base(name, "Visibility", label)
    {
        States = states;
        CurrentState = currentState;
        SelectedState = currentState;
    }
}

public sealed class CadDynamicBlockActionViewModel : ViewModelBase
{
    public string Name { get; }
    public string Type { get; }
    public int EntityCount { get; }

    public CadDynamicBlockActionViewModel(string name, string type, int entityCount)
    {
        Name = name;
        Type = type;
        EntityCount = entityCount;
    }
}
