using System;
using Avalonia.Controls;

namespace ACadInspector.ViewModels;

internal static class CadRenderLayerColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("Name", nameof(CadRenderLayerRowViewModel.Name), static row => row.Name),
            CreateCheckBoxColumn("On", nameof(CadRenderLayerRowViewModel.IsOn), static row => row.IsOn, static (row, value) => row.IsOn = value),
            CreateCheckBoxColumn("Frozen", nameof(CadRenderLayerRowViewModel.IsFrozen), static row => row.IsFrozen, static (row, value) => row.IsFrozen = value),
            CreateCheckBoxColumn("Locked", nameof(CadRenderLayerRowViewModel.IsLocked), static row => row.IsLocked, static (row, value) => row.IsLocked = value),
            CreateCheckBoxColumn("Plot", nameof(CadRenderLayerRowViewModel.IsPlottable), static row => row.IsPlottable, static (row, value) => row.IsPlottable = value),
            CreateTextColumn("Color", nameof(CadRenderLayerRowViewModel.Color), static row => row.Color),
            CreateTextColumn("LineType", nameof(CadRenderLayerRowViewModel.LineType), static row => row.LineType),
            CreateTextColumn("LineWeight", nameof(CadRenderLayerRowViewModel.LineWeight), static row => row.LineWeight)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<CadRenderLayerRowViewModel, string> getter,
        Action<CadRenderLayerRowViewModel, string>? setter = null)
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
        Func<CadRenderLayerRowViewModel, bool> getter,
        Action<CadRenderLayerRowViewModel, bool> setter)
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
