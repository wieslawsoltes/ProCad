using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

namespace ACadInspector.Behaviors;

public sealed class DataGridBringSelectedIntoViewBehavior : Behavior<DataGrid>
{

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is not null)
        {
            AssociatedObject.SelectionChanged += OnSelectionChanged;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.SelectionChanged -= OnSelectionChanged;
        }
        base.OnDetaching();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }

        var grid = AssociatedObject;
        var selected = grid?.SelectedItem;
        if (grid is null || selected is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var selectedItem = grid.SelectedItem;
            if (selectedItem is null)
            {
                return;
            }

            var column = grid.CurrentColumn;
            if (column is null && grid.Columns.Count > 0)
            {
                column = grid.Columns[0];
            }

            if (column is not null)
            {
                grid.ScrollIntoView(selectedItem, column);
            }
        }, DispatcherPriority.Background);
    }
}
