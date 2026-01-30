using System;
using Avalonia.Controls;

namespace ACadInspector.ViewModels;

internal static class CadDwgHeaderColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("Variable", nameof(CadDwgHeaderRowViewModel.Variable), static row => row.Variable),
            CreateTextColumn("Property", nameof(CadDwgHeaderRowViewModel.Property), static row => row.Property),
            CreateTextColumn("Codes", nameof(CadDwgHeaderRowViewModel.Codes), static row => row.Codes),
            CreateTextColumn("Reference", nameof(CadDwgHeaderRowViewModel.Reference), static row => row.Reference),
            CreateTextColumn("Type", nameof(CadDwgHeaderRowViewModel.Type), static row => row.Type),
            CreateTextColumn("Value", nameof(CadDwgHeaderRowViewModel.Value), static row => row.Value)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<CadDwgHeaderRowViewModel, string> getter)
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
