using System;
using Avalonia.Controls;

namespace ACadInspector.ViewModels;

internal static class CadDwgSummaryColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("Category", nameof(CadDwgSummaryRowViewModel.Category), static row => row.Category),
            CreateTextColumn("Name", nameof(CadDwgSummaryRowViewModel.Name), static row => row.Name),
            CreateTextColumn("Value", nameof(CadDwgSummaryRowViewModel.Value), static row => row.Value)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<CadDwgSummaryRowViewModel, string> getter)
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
