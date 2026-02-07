using System;
using Avalonia.Controls;

namespace ACadInspector.ViewModels;

internal static class CadDxfPropertyColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("Property", nameof(CadDxfPropertyRowViewModel.Name), static row => row.Name),
            CreateTextColumn("DXF Codes", nameof(CadDxfPropertyRowViewModel.Codes), static row => row.Codes),
            CreateTextColumn("DXF Ref", nameof(CadDxfPropertyRowViewModel.ReferenceType), static row => row.ReferenceType),
            CreateTextColumn("Value", nameof(CadDxfPropertyRowViewModel.Value), static row => row.Value)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<CadDxfPropertyRowViewModel, string> getter)
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
