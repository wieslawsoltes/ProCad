using System;
using Avalonia.Controls;

namespace ACadInspector.ViewModels;

internal static class CadPropertyDiffColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("Property", nameof(CadPropertyDiffRowViewModel.Property), static row => row.Property),
            CreateTextColumn("Left", nameof(CadPropertyDiffRowViewModel.LeftValue), static row => row.LeftValue),
            CreateTextColumn("Right", nameof(CadPropertyDiffRowViewModel.RightValue), static row => row.RightValue)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<CadPropertyDiffRowViewModel, string> getter)
    {
        var binding = DataGridBindingFactory.CreateBinding(propertyName, getter);
        var accessor = DataGridBindingFactory.CreateValueAccessor(getter);
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
}
