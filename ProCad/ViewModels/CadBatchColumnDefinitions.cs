using System;
using Avalonia.Controls;

namespace ProCad.ViewModels;

internal static class CadBatchColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("File", nameof(CadBatchItemViewModel.FileName), static row => row.FileName),
            CreateTextColumn("Format", nameof(CadBatchItemViewModel.FormatText), static row => row.FormatText),
            CreateTextColumn("Status", nameof(CadBatchItemViewModel.StatusText), static row => row.StatusText),
            CreateTextColumn("Duration", nameof(CadBatchItemViewModel.DurationText), static row => row.DurationText),
            CreateTextColumn("Message", nameof(CadBatchItemViewModel.Message), static row => row.Message),
            CreateTextColumn("Path", nameof(CadBatchItemViewModel.PathText), static row => row.PathText)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<CadBatchItemViewModel, string> getter)
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
