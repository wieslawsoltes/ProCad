using System;
using Avalonia.Controls;

namespace ACadInspector.ViewModels;

internal static class DxfTextDiffColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("Change", nameof(DxfTextDiffRowViewModel.Change), static row => row.Change),
            CreateTextColumn("Left", nameof(DxfTextDiffRowViewModel.LeftLine), static row => row.LeftLine),
            CreateTextColumn("Right", nameof(DxfTextDiffRowViewModel.RightLine), static row => row.RightLine),
            CreateTextColumn("Left Text", nameof(DxfTextDiffRowViewModel.LeftText), static row => row.LeftText),
            CreateTextColumn("Right Text", nameof(DxfTextDiffRowViewModel.RightText), static row => row.RightText)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<DxfTextDiffRowViewModel, string> getter)
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
            CanUserReorder = true,
            CanUserResize = true
        };

        column.Options = new DataGridColumnDefinitionOptions
        {
            IsSearchable = true,
            FilterValueAccessor = accessor
        };

        return column;
    }
}
