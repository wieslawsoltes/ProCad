using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;

namespace ACadInspector.Behaviors;

public sealed class DataGridRowDoubleClickBehavior : Behavior<DataGrid>
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<DataGridRowDoubleClickBehavior, ICommand?>(nameof(Command));

    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<DataGridRowDoubleClickBehavior, object?>(nameof(CommandParameter));

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is not null)
        {
            AssociatedObject.DoubleTapped += OnDoubleTapped;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.DoubleTapped -= OnDoubleTapped;
        }
        base.OnDetaching();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var command = Command;
        if (command is null)
        {
            return;
        }

        var parameter = CommandParameter ?? ResolveRowItem(e.Source) ?? AssociatedObject?.SelectedItem;
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
            e.Handled = true;
        }
    }

    private static object? ResolveRowItem(object? source)
    {
        if (source is not Visual visual)
        {
            return null;
        }

        var row = visual.FindAncestorOfType<DataGridRow>();
        return row?.DataContext;
    }
}
