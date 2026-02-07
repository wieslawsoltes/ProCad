using System;
using Avalonia.Controls;

namespace ACadInspector.ViewModels;

internal static class CadDiffSummaryColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("Change", nameof(CadDiffSummaryRowViewModel.Category), static row => row.Category),
            CreateTextColumn("Count", nameof(CadDiffSummaryRowViewModel.CountText), static row => row.CountText)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<CadDiffSummaryRowViewModel, string> getter)
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
