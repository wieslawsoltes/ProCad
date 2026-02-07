using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;

namespace ACadInspector.Behaviors;

public sealed class CadCommandLineInputBehavior : Behavior<TextBox>
{
    public static readonly StyledProperty<ICommand?> SubmitCommandProperty =
        AvaloniaProperty.Register<CadCommandLineInputBehavior, ICommand?>(nameof(SubmitCommand));

    public static readonly StyledProperty<ICommand?> NextCompletionCommandProperty =
        AvaloniaProperty.Register<CadCommandLineInputBehavior, ICommand?>(nameof(NextCompletionCommand));

    public static readonly StyledProperty<ICommand?> PreviousCompletionCommandProperty =
        AvaloniaProperty.Register<CadCommandLineInputBehavior, ICommand?>(nameof(PreviousCompletionCommand));

    public static readonly StyledProperty<ICommand?> HistoryPreviousCommandProperty =
        AvaloniaProperty.Register<CadCommandLineInputBehavior, ICommand?>(nameof(HistoryPreviousCommand));

    public static readonly StyledProperty<ICommand?> HistoryNextCommandProperty =
        AvaloniaProperty.Register<CadCommandLineInputBehavior, ICommand?>(nameof(HistoryNextCommand));

    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<CadCommandLineInputBehavior, ICommand?>(nameof(CancelCommand));

    public static readonly StyledProperty<ICommand?> AcceptCompletionCommandProperty =
        AvaloniaProperty.Register<CadCommandLineInputBehavior, ICommand?>(nameof(AcceptCompletionCommand));

    public ICommand? SubmitCommand
    {
        get => GetValue(SubmitCommandProperty);
        set => SetValue(SubmitCommandProperty, value);
    }

    public ICommand? NextCompletionCommand
    {
        get => GetValue(NextCompletionCommandProperty);
        set => SetValue(NextCompletionCommandProperty, value);
    }

    public ICommand? PreviousCompletionCommand
    {
        get => GetValue(PreviousCompletionCommandProperty);
        set => SetValue(PreviousCompletionCommandProperty, value);
    }

    public ICommand? HistoryPreviousCommand
    {
        get => GetValue(HistoryPreviousCommandProperty);
        set => SetValue(HistoryPreviousCommandProperty, value);
    }

    public ICommand? HistoryNextCommand
    {
        get => GetValue(HistoryNextCommandProperty);
        set => SetValue(HistoryNextCommandProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public ICommand? AcceptCompletionCommand
    {
        get => GetValue(AcceptCompletionCommandProperty);
        set => SetValue(AcceptCompletionCommandProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is not null)
        {
            AssociatedObject.KeyDown += OnKeyDown;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.KeyDown -= OnKeyDown;
        }

        base.OnDetaching();
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        switch (args.Key)
        {
            case Key.Enter:
                args.Handled = TryExecute(SubmitCommand);
                break;
            case Key.Tab:
                args.Handled = args.KeyModifiers.HasFlag(KeyModifiers.Shift)
                    ? TryExecute(PreviousCompletionCommand)
                    : TryExecute(NextCompletionCommand);
                break;
            case Key.Up:
                if (args.KeyModifiers == KeyModifiers.None)
                {
                    args.Handled = TryExecute(HistoryPreviousCommand);
                }
                break;
            case Key.Down:
                if (args.KeyModifiers == KeyModifiers.None)
                {
                    args.Handled = TryExecute(HistoryNextCommand);
                }
                break;
            case Key.Escape:
                args.Handled = TryExecute(CancelCommand);
                break;
            case Key.Right:
                if (args.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    args.Handled = TryExecute(AcceptCompletionCommand);
                }
                break;
        }
    }

    private static bool TryExecute(ICommand? command, object? parameter = null)
    {
        if (command is null || !command.CanExecute(parameter))
        {
            return false;
        }

        command.Execute(parameter);
        return true;
    }
}
