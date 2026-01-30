using System;
using Avalonia.Controls;

namespace ACadInspector.ViewModels;

internal static class CadIoOptionsColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("Name", nameof(CadOptionRowViewModel.Name), static row => row.Name),
            CreateCheckBoxColumn("Value", nameof(CadOptionRowViewModel.Value), static row => row.Value, static (row, value) => row.Value = value),
            CreateTextColumn("Description", nameof(CadOptionRowViewModel.Description), static row => row.Description)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<CadOptionRowViewModel, string> getter,
        Action<CadOptionRowViewModel, string>? setter = null)
    {
        var binding = DataGridBindingFactory.CreateBinding(propertyName, getter, setter);
        var accessor = DataGridBindingFactory.CreateValueAccessor(getter, setter);
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

    private static DataGridCheckBoxColumnDefinition CreateCheckBoxColumn(
        object header,
        string propertyName,
        Func<CadOptionRowViewModel, bool> getter,
        Action<CadOptionRowViewModel, bool> setter)
    {
        var binding = DataGridBindingFactory.CreateBinding(propertyName, getter, setter);
        var accessor = DataGridBindingFactory.CreateValueAccessor(getter, setter);
        var column = new DataGridCheckBoxColumnDefinition
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
