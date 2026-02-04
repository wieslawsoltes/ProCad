using System;
using Avalonia.Controls;
using Avalonia.Data;

namespace ACadInspector.ViewModels;

internal static class CadRenderEntityTypeColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("Entity Type", nameof(CadRenderEntityTypeRowViewModel.EntityType), static row => row.EntityType),
            CreateTextColumn("Count", nameof(CadRenderEntityTypeRowViewModel.Count), static row => row.Count),
            CreateCheckBoxColumn("Visible", nameof(CadRenderEntityTypeRowViewModel.IsVisible), static row => row.IsVisible, static (row, value) => row.IsVisible = value)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn<TValue>(
        object header,
        string propertyName,
        Func<CadRenderEntityTypeRowViewModel, TValue> getter,
        Action<CadRenderEntityTypeRowViewModel, TValue>? setter = null)
    {
        var binding = DataGridBindingFactory.CreateBinding(
            propertyName,
            getter,
            setter,
            mode: BindingMode.TwoWay,
            updateSourceTrigger: UpdateSourceTrigger.PropertyChanged);
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
        Func<CadRenderEntityTypeRowViewModel, bool> getter,
        Action<CadRenderEntityTypeRowViewModel, bool> setter)
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
