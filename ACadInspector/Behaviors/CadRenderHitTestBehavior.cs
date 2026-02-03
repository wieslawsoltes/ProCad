using System;
using System.Numerics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;
using ACadInspector.Controls;
using ACadInspector.ViewModels;

namespace ACadInspector.Behaviors;

public sealed class CadRenderHitTestBehavior : Behavior<CadRenderControl>
{
    public static readonly StyledProperty<ICommand?> HoverCommandProperty =
        AvaloniaProperty.Register<CadRenderHitTestBehavior, ICommand?>(nameof(HoverCommand));

    public static readonly StyledProperty<ICommand?> SelectCommandProperty =
        AvaloniaProperty.Register<CadRenderHitTestBehavior, ICommand?>(nameof(SelectCommand));

    public static readonly StyledProperty<ICommand?> ClearHoverCommandProperty =
        AvaloniaProperty.Register<CadRenderHitTestBehavior, ICommand?>(nameof(ClearHoverCommand));

    public static readonly StyledProperty<double> HoverTolerancePixelsProperty =
        AvaloniaProperty.Register<CadRenderHitTestBehavior, double>(nameof(HoverTolerancePixels), 4.0);

    public static readonly StyledProperty<double> SelectionTolerancePixelsProperty =
        AvaloniaProperty.Register<CadRenderHitTestBehavior, double>(nameof(SelectionTolerancePixels), 6.0);

    public static readonly StyledProperty<int> HoverThrottleMsProperty =
        AvaloniaProperty.Register<CadRenderHitTestBehavior, int>(nameof(HoverThrottleMs), 40);

    private DateTime _lastHoverUtc;

    public ICommand? HoverCommand
    {
        get => GetValue(HoverCommandProperty);
        set => SetValue(HoverCommandProperty, value);
    }

    public ICommand? SelectCommand
    {
        get => GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    public ICommand? ClearHoverCommand
    {
        get => GetValue(ClearHoverCommandProperty);
        set => SetValue(ClearHoverCommandProperty, value);
    }

    public double HoverTolerancePixels
    {
        get => GetValue(HoverTolerancePixelsProperty);
        set => SetValue(HoverTolerancePixelsProperty, value);
    }

    public double SelectionTolerancePixels
    {
        get => GetValue(SelectionTolerancePixelsProperty);
        set => SetValue(SelectionTolerancePixelsProperty, value);
    }

    public int HoverThrottleMs
    {
        get => GetValue(HoverThrottleMsProperty);
        set => SetValue(HoverThrottleMsProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject is null)
        {
            return;
        }

        AssociatedObject.PointerMoved += OnPointerMoved;
        AssociatedObject.PointerPressed += OnPointerPressed;
        AssociatedObject.PointerExited += OnPointerLeave;
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.PointerMoved -= OnPointerMoved;
            AssociatedObject.PointerPressed -= OnPointerPressed;
            AssociatedObject.PointerExited -= OnPointerLeave;
        }

        base.OnDetaching();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var target = AssociatedObject;
        if (target is null || HoverCommand is null)
        {
            return;
        }

        var current = e.GetCurrentPoint(target);
        if (current.Properties.IsLeftButtonPressed ||
            current.Properties.IsMiddleButtonPressed ||
            current.Properties.IsRightButtonPressed)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (HoverThrottleMs > 0 && (now - _lastHoverUtc).TotalMilliseconds < HoverThrottleMs)
        {
            return;
        }

        _lastHoverUtc = now;
        if (!TryBuildRequest(target, current.Position, HoverTolerancePixels, CadHitTestKind.Hover, e.KeyModifiers, out var request))
        {
            return;
        }

        if (HoverCommand.CanExecute(request))
        {
            HoverCommand.Execute(request);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var target = AssociatedObject;
        if (target is null || SelectCommand is null)
        {
            return;
        }

        var current = e.GetCurrentPoint(target);
        if (!current.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (!TryBuildRequest(target, current.Position, SelectionTolerancePixels, CadHitTestKind.Select, e.KeyModifiers, out var request))
        {
            return;
        }

        if (SelectCommand.CanExecute(request))
        {
            SelectCommand.Execute(request);
        }
    }

    private void OnPointerLeave(object? sender, PointerEventArgs e)
    {
        var command = ClearHoverCommand;
        if (command is null)
        {
            return;
        }

        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private static bool TryBuildRequest(
        CadRenderControl target,
        Point position,
        double tolerancePixels,
        CadHitTestKind kind,
        KeyModifiers modifiers,
        out CadRenderHitTestRequest request)
    {
        request = default;
        if (target.Scene is null)
        {
            return false;
        }

        if (!target.TryScreenToWorld(position, out var world))
        {
            return false;
        }

        var toleranceWorld = (float)target.PixelsToWorld((float)tolerancePixels);
        var mapped = MapModifiers(modifiers);
        request = new CadRenderHitTestRequest(world, toleranceWorld, kind, mapped);
        return true;
    }

    private static CadInputModifiers MapModifiers(KeyModifiers modifiers)
    {
        var result = CadInputModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            result |= CadInputModifiers.Shift;
        }
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            result |= CadInputModifiers.Control;
        }
        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            result |= CadInputModifiers.Alt;
        }

        return result;
    }
}
