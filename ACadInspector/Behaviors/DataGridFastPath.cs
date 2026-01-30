using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;

namespace ACadInspector.Behaviors;

public sealed class DataGridFastPath
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGridFastPath, DataGrid, bool>("IsEnabled");

    static DataGridFastPath()
    {
        IsEnabledProperty.Changed.AddClassHandler<DataGrid>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(AvaloniaObject element) => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(AvaloniaObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is not bool enabled || !enabled)
        {
            return;
        }

        var options = grid.FastPathOptions ?? new DataGridFastPathOptions();
        options.StrictMode = true;
        grid.FastPathOptions = options;

        grid.FilteringAdapterFactory ??= new DataGridAccessorFilteringAdapterFactory();
        grid.SearchAdapterFactory ??= new DataGridAccessorSearchAdapterFactory();
    }
}
