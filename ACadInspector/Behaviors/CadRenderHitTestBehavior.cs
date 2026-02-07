using System;
using System.Numerics;
using System.Windows.Input;
using ACadInspector.Controls;
using ACadInspector.Editing.Interaction;
using Avalonia;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;

namespace ACadInspector.Behaviors;

public sealed class CadRenderHitTestBehavior : Behavior<CadRenderControl>
{
    public static readonly StyledProperty<ICommand?> InteractionCommandProperty =
        AvaloniaProperty.Register<CadRenderHitTestBehavior, ICommand?>(nameof(InteractionCommand));

    public static readonly StyledProperty<double> HoverTolerancePixelsProperty =
        AvaloniaProperty.Register<CadRenderHitTestBehavior, double>(nameof(HoverTolerancePixels), 4.0);

    public static readonly StyledProperty<double> SelectionTolerancePixelsProperty =
        AvaloniaProperty.Register<CadRenderHitTestBehavior, double>(nameof(SelectionTolerancePixels), 6.0);

    public static readonly StyledProperty<int> HoverThrottleMsProperty =
        AvaloniaProperty.Register<CadRenderHitTestBehavior, int>(nameof(HoverThrottleMs), 40);

    private DateTime _lastHoverUtc;
    private Point _lastScreenPoint;
    private Vector2 _lastWorldPoint;

    public ICommand? InteractionCommand
    {
        get => GetValue(InteractionCommandProperty);
        set => SetValue(InteractionCommandProperty, value);
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

        AssociatedObject.Focusable = true;
        AssociatedObject.PointerMoved += OnPointerMoved;
        AssociatedObject.PointerPressed += OnPointerPressed;
        AssociatedObject.PointerReleased += OnPointerReleased;
        AssociatedObject.PointerWheelChanged += OnPointerWheelChanged;
        AssociatedObject.KeyDown += OnKeyDown;
        AssociatedObject.KeyUp += OnKeyUp;
        AssociatedObject.TextInput += OnTextInput;
        AssociatedObject.PointerExited += OnPointerExited;
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.PointerMoved -= OnPointerMoved;
            AssociatedObject.PointerPressed -= OnPointerPressed;
            AssociatedObject.PointerReleased -= OnPointerReleased;
            AssociatedObject.PointerWheelChanged -= OnPointerWheelChanged;
            AssociatedObject.KeyDown -= OnKeyDown;
            AssociatedObject.KeyUp -= OnKeyUp;
            AssociatedObject.TextInput -= OnTextInput;
            AssociatedObject.PointerExited -= OnPointerExited;
        }

        base.OnDetaching();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs args)
    {
        if (!TryBuildPointerEvent(args, CadInteractionEventKind.PointerMove, out var interactionEvent))
        {
            return;
        }

        if (interactionEvent.PointerButtons == CadInteractionPointerButtons.None)
        {
            var now = DateTime.UtcNow;
            if (HoverThrottleMs > 0 && (now - _lastHoverUtc).TotalMilliseconds < HoverThrottleMs)
            {
                return;
            }

            _lastHoverUtc = now;
        }

        Execute(interactionEvent);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.Focus();
        }

        if (TryBuildPointerEvent(args, CadInteractionEventKind.PointerDown, out var interactionEvent))
        {
            Execute(interactionEvent);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (TryBuildPointerEvent(args, CadInteractionEventKind.PointerUp, out var interactionEvent))
        {
            Execute(interactionEvent);
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs args)
    {
        if (!TryBuildPointerEvent(args, CadInteractionEventKind.PointerWheel, out var interactionEvent))
        {
            return;
        }

        interactionEvent = interactionEvent with
        {
            WheelDelta = (float)args.Delta.Y
        };
        Execute(interactionEvent);
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        Execute(new CadInteractionEvent(
            Kind: CadInteractionEventKind.KeyDown,
            WorldPoint: _lastWorldPoint,
            ScreenPoint: new Vector2((float)_lastScreenPoint.X, (float)_lastScreenPoint.Y),
            Modifiers: MapModifiers(args.KeyModifiers),
            PointerButtons: CadInteractionPointerButtons.None,
            Tolerance: 0f,
            WheelDelta: 0f,
            Key: args.Key.ToString(),
            Text: null,
            Viewport: TryGetViewportSummary()));
    }

    private void OnKeyUp(object? sender, KeyEventArgs args)
    {
        Execute(new CadInteractionEvent(
            Kind: CadInteractionEventKind.KeyUp,
            WorldPoint: _lastWorldPoint,
            ScreenPoint: new Vector2((float)_lastScreenPoint.X, (float)_lastScreenPoint.Y),
            Modifiers: MapModifiers(args.KeyModifiers),
            PointerButtons: CadInteractionPointerButtons.None,
            Tolerance: 0f,
            WheelDelta: 0f,
            Key: args.Key.ToString(),
            Text: null,
            Viewport: TryGetViewportSummary()));
    }

    private void OnTextInput(object? sender, TextInputEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Text))
        {
            return;
        }

        Execute(new CadInteractionEvent(
            Kind: CadInteractionEventKind.TextInput,
            WorldPoint: _lastWorldPoint,
            ScreenPoint: new Vector2((float)_lastScreenPoint.X, (float)_lastScreenPoint.Y),
            Modifiers: CadInteractionModifiers.None,
            PointerButtons: CadInteractionPointerButtons.None,
            Tolerance: 0f,
            WheelDelta: 0f,
            Key: null,
            Text: args.Text,
            Viewport: TryGetViewportSummary()));
    }

    private void OnPointerExited(object? sender, PointerEventArgs args)
    {
        Execute(new CadInteractionEvent(
            Kind: CadInteractionEventKind.PointerMove,
            WorldPoint: _lastWorldPoint,
            ScreenPoint: new Vector2((float)_lastScreenPoint.X, (float)_lastScreenPoint.Y),
            Modifiers: MapModifiers(args.KeyModifiers),
            PointerButtons: CadInteractionPointerButtons.None,
            Tolerance: 0f,
            WheelDelta: 0f,
            Key: null,
            Text: null,
            Viewport: TryGetViewportSummary()));
    }

    private bool TryBuildPointerEvent(
        PointerEventArgs args,
        CadInteractionEventKind kind,
        out CadInteractionEvent interactionEvent)
    {
        interactionEvent = default;
        var target = AssociatedObject;
        if (target is null || target.Scene is null)
        {
            return false;
        }

        var point = args.GetCurrentPoint(target);
        _lastScreenPoint = point.Position;
        if (!target.TryScreenToWorld(point.Position, out var worldPoint))
        {
            return false;
        }

        _lastWorldPoint = worldPoint;
        var tolerancePixels = kind == CadInteractionEventKind.PointerDown
            ? SelectionTolerancePixels
            : HoverTolerancePixels;

        interactionEvent = new CadInteractionEvent(
            Kind: kind,
            WorldPoint: worldPoint,
            ScreenPoint: new Vector2((float)point.Position.X, (float)point.Position.Y),
            Modifiers: MapModifiers(args.KeyModifiers),
            PointerButtons: GetPointerButtons(point.Properties),
            Tolerance: target.PixelsToWorld((float)tolerancePixels),
            WheelDelta: 0f,
            Key: null,
            Text: null,
            Viewport: TryGetViewportSummary(target));
        return true;
    }

    private void Execute(CadInteractionEvent interactionEvent)
    {
        var command = InteractionCommand;
        if (command is null)
        {
            return;
        }

        if (command.CanExecute(interactionEvent))
        {
            command.Execute(interactionEvent);
        }
    }

    private static CadInteractionPointerButtons GetPointerButtons(PointerPointProperties properties)
    {
        var result = CadInteractionPointerButtons.None;
        if (properties.IsLeftButtonPressed)
        {
            result |= CadInteractionPointerButtons.Left;
        }
        if (properties.IsRightButtonPressed)
        {
            result |= CadInteractionPointerButtons.Right;
        }
        if (properties.IsMiddleButtonPressed)
        {
            result |= CadInteractionPointerButtons.Middle;
        }

        return result;
    }

    private static CadInteractionModifiers MapModifiers(KeyModifiers modifiers)
    {
        var result = CadInteractionModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            result |= CadInteractionModifiers.Shift;
        }
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            result |= CadInteractionModifiers.Control;
        }
        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            result |= CadInteractionModifiers.Alt;
        }

        return result;
    }

    private CadInteractionViewport? TryGetViewportSummary(CadRenderControl? target = null)
    {
        target ??= AssociatedObject;
        if (target is null)
        {
            return null;
        }

        var bounds = target.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        var centerScreen = new Point(bounds.Width * 0.5, bounds.Height * 0.5);
        if (!target.TryScreenToWorld(centerScreen, out var centerWorld))
        {
            return null;
        }

        return new CadInteractionViewport(
            Center: centerWorld,
            Width: target.PixelsToWorld((float)bounds.Width),
            Height: target.PixelsToWorld((float)bounds.Height),
            Zoom: (float)target.Zoom);
    }
}
