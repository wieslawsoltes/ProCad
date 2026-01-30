using System;
using ACadSharp;
using ACadSharp.Tables;
using ReactiveUI;

namespace ACadInspector.ViewModels;

public sealed class CadRenderLayerRowViewModel : ReactiveObject
{
    private readonly Layer _layer;
    private bool _isOn;
    private bool _isFrozen;
    private bool _isLocked;
    private bool _isPlottable;

    public string Name { get; }
    public string Color { get; }
    public string LineType { get; }
    public string LineWeight { get; }

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _isOn, value))
            {
                return;
            }

            _layer.IsOn = value;
            RaiseVisibilityChanged();
        }
    }

    public bool IsFrozen
    {
        get => _isFrozen;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _isFrozen, value))
            {
                return;
            }

            UpdateFlag(LayerFlags.Frozen, value);
            RaiseVisibilityChanged();
        }
    }

    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _isLocked, value))
            {
                return;
            }

            UpdateFlag(LayerFlags.Locked, value);
        }
    }

    public bool IsPlottable
    {
        get => _isPlottable;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _isPlottable, value))
            {
                return;
            }

            _layer.PlotFlag = value;
            if (_layer.PlotFlag != _isPlottable)
            {
                _isPlottable = _layer.PlotFlag;
                this.RaisePropertyChanged();
            }
        }
    }

    public bool IsVisible => _isOn && !_isFrozen;

    public event EventHandler? VisibilityChanged;

    public CadRenderLayerRowViewModel(Layer layer)
    {
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));
        Name = layer.Name;
        Color = FormatColor(layer.Color);
        LineType = layer.LineType?.Name ?? string.Empty;
        LineWeight = layer.LineWeight.ToString();
        _isOn = layer.IsOn;
        _isFrozen = layer.Flags.HasFlag(LayerFlags.Frozen);
        _isLocked = layer.Flags.HasFlag(LayerFlags.Locked);
        _isPlottable = layer.PlotFlag;
    }

    private void UpdateFlag(LayerFlags flag, bool enabled)
    {
        var flags = _layer.Flags;
        flags = enabled ? flags | flag : flags & ~flag;
        _layer.Flags = flags;
    }

    private void RaiseVisibilityChanged()
    {
        this.RaisePropertyChanged(nameof(IsVisible));
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatColor(Color color)
    {
        if (color.IsTrueColor)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        return color.Index < 0 ? "ByLayer" : color.Index.ToString();
    }
}
