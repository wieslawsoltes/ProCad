using System;
using Avalonia.Controls;

namespace ProCad.ViewModels;

internal static class CadViewportColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("Layout", nameof(CadViewportRowViewModel.LayoutName), static row => row.LayoutName),
            CreateTextColumn("Id", nameof(CadViewportRowViewModel.Id), static row => row.Id),
            CreateTextColumn("Handle", nameof(CadViewportRowViewModel.Handle), static row => row.Handle),
            CreateTextColumn("Layer", nameof(CadViewportRowViewModel.LayerName), static row => row.LayerName),
            CreateCheckBoxColumn("Paper", nameof(CadViewportRowViewModel.IsPaper), static row => row.IsPaper),
            CreateTextColumn("Center", nameof(CadViewportRowViewModel.Center), static row => row.Center),
            CreateTextColumn("Size", nameof(CadViewportRowViewModel.Size), static row => row.Size),
            CreateTextColumn("View Center", nameof(CadViewportRowViewModel.ViewCenter), static row => row.ViewCenter),
            CreateTextColumn("View Size", nameof(CadViewportRowViewModel.ViewSize), static row => row.ViewSize),
            CreateTextColumn("Scale", nameof(CadViewportRowViewModel.ScaleFactor), static row => row.ScaleFactor),
            CreateTextColumn("Twist", nameof(CadViewportRowViewModel.TwistAngle), static row => row.TwistAngle),
            CreateTextColumn("Status", nameof(CadViewportRowViewModel.Status), static row => row.Status)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<CadViewportRowViewModel, string> getter)
    {
        var binding = DataGridBindingFactory.CreateBinding(propertyName, getter);
        var accessor = DataGridBindingFactory.CreateValueAccessor(getter, setter: null);
        var column = new DataGridTextColumnDefinition
        {
            Header = header,
            Binding = binding,
            ColumnKey = propertyName,
            SortMemberPath = propertyName,
            CanUserSort = true,
            CanUserReorder = false,
            CanUserResize = true
        };

        DataGridColumnDefinitionThreadSafety.SetOptions(column, new DataGridColumnDefinitionOptions
        {
            IsSearchable = true,
            FilterValueAccessor = accessor
        });

        return column;
    }

    private static DataGridCheckBoxColumnDefinition CreateCheckBoxColumn(
        object header,
        string propertyName,
        Func<CadViewportRowViewModel, bool> getter)
    {
        var binding = DataGridBindingFactory.CreateBinding(propertyName, getter);
        var accessor = DataGridBindingFactory.CreateValueAccessor(getter, setter: null);
        var column = new DataGridCheckBoxColumnDefinition
        {
            Header = header,
            Binding = binding,
            ColumnKey = propertyName,
            SortMemberPath = propertyName,
            CanUserSort = true,
            CanUserReorder = false,
            CanUserResize = true,
            IsReadOnly = true
        };

        DataGridColumnDefinitionThreadSafety.SetOptions(column, new DataGridColumnDefinitionOptions
        {
            IsSearchable = true,
            FilterValueAccessor = accessor
        });

        return column;
    }
}
