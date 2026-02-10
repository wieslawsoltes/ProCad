using System;
using ACadInspector.Diagnostics;
using Avalonia.Controls;

namespace ACadInspector.ViewModels;

internal static class CadLogOutputColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("Time", nameof(AppLogEntry.TimestampLocal), static row => row.TimestampLocal),
            CreateTextColumn("Level", nameof(AppLogEntry.LevelText), static row => row.LevelText),
            CreateTextColumn("Category", nameof(AppLogEntry.Category), static row => row.Category),
            CreateTextColumn("Message", nameof(AppLogEntry.Message), static row => row.Message),
            CreateTextColumn("Exception", nameof(AppLogEntry.ExceptionText), static row => row.ExceptionText),
            CreateTextColumn("Thread", nameof(AppLogEntry.ThreadText), static row => row.ThreadText)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<AppLogEntry, string> getter)
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
}
