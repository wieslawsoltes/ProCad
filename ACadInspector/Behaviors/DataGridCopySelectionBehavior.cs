using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;

namespace ACadInspector.Behaviors;

public sealed class DataGridCopySelectionBehavior : Behavior<Button>
{
    public static readonly StyledProperty<string> GridNameProperty =
        AvaloniaProperty.Register<DataGridCopySelectionBehavior, string>(
            nameof(GridName),
            defaultValue: "LogGrid");

    public string GridName
    {
        get => GetValue(GridNameProperty);
        set => SetValue(GridNameProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is not null)
        {
            AssociatedObject.Click += OnClick;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.Click -= OnClick;
        }

        base.OnDetaching();
    }

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (AssociatedObject is null || string.IsNullOrWhiteSpace(GridName))
        {
            return;
        }

        var container = AssociatedObject.FindAncestorOfType<UserControl>();
        var grid = container?.FindControl<DataGrid>(GridName);
        grid?.CopySelectionToClipboard();
    }
}
