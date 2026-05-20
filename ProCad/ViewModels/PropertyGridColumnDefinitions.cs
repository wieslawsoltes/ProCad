using System;
using Avalonia.Controls;

namespace ProCad.ViewModels;

internal static class PropertyGridColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("Name", nameof(PropertyGridRowViewModel.Name), static row => row.Name),
            CreateTextColumn("Value", nameof(PropertyGridRowViewModel.ValueText), static row => row.ValueText, static (row, value) => row.ValueText = value),
            CreateTextColumn("Type", nameof(PropertyGridRowViewModel.TypeName), static row => row.TypeName),
            CreateTextColumn("DXF Codes", nameof(PropertyGridRowViewModel.DxfCodes), static row => row.DxfCodes),
            CreateTextColumn("DXF Ref", nameof(PropertyGridRowViewModel.DxfReferenceTypeText), static row => row.DxfReferenceTypeText),
            CreateTextColumn("Validation", nameof(PropertyGridRowViewModel.ValidationMessageText), static row => row.ValidationMessageText)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<PropertyGridRowViewModel, string> getter,
        Action<PropertyGridRowViewModel, string>? setter = null)
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
