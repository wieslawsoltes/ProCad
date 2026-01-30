using System;
using Avalonia.Controls;

namespace ACadInspector.ViewModels;

internal static class CadDwgClassColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("DXF Name", nameof(CadDwgClassRowViewModel.DxfName), static row => row.DxfName),
            CreateTextColumn("C++ Class", nameof(CadDwgClassRowViewModel.CppClassName), static row => row.CppClassName),
            CreateTextColumn("Application", nameof(CadDwgClassRowViewModel.ApplicationName), static row => row.ApplicationName),
            CreateTextColumn("Class #", nameof(CadDwgClassRowViewModel.ClassNumber), static row => row.ClassNumber),
            CreateTextColumn("DWG Version", nameof(CadDwgClassRowViewModel.DwgVersion), static row => row.DwgVersion),
            CreateTextColumn("Proxy Flags", nameof(CadDwgClassRowViewModel.ProxyFlags), static row => row.ProxyFlags),
            CreateTextColumn("Is Entity", nameof(CadDwgClassRowViewModel.IsAnEntity), static row => row.IsAnEntity),
            CreateTextColumn("Was Zombie", nameof(CadDwgClassRowViewModel.WasZombie), static row => row.WasZombie),
            CreateTextColumn("Instances", nameof(CadDwgClassRowViewModel.InstanceCount), static row => row.InstanceCount),
            CreateTextColumn("Item Class Id", nameof(CadDwgClassRowViewModel.ItemClassId), static row => row.ItemClassId)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn<T>(
        object header,
        string propertyName,
        Func<CadDwgClassRowViewModel, T> getter)
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
