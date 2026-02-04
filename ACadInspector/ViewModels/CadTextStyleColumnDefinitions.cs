using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ACadInspector.ViewModels;

internal static class CadTextStyleColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateImageColumn("Preview", nameof(CadTextStyleRowViewModel.Preview), static row => row.Preview),
            CreateTextColumn("Name", nameof(CadTextStyleRowViewModel.Name), static row => row.Name),
            CreateTextColumn("Handle", nameof(CadTextStyleRowViewModel.Handle), static row => row.Handle),
            CreateTextColumn("Font", nameof(CadTextStyleRowViewModel.Font), static row => row.Font),
            CreateTextColumn("Big Font", nameof(CadTextStyleRowViewModel.BigFont), static row => row.BigFont),
            CreateTextColumn("Height", nameof(CadTextStyleRowViewModel.Height), static row => row.Height),
            CreateTextColumn("Width", nameof(CadTextStyleRowViewModel.Width), static row => row.Width),
            CreateTextColumn("Oblique", nameof(CadTextStyleRowViewModel.ObliqueAngle), static row => row.ObliqueAngle),
            CreateCheckBoxColumn("Shape", nameof(CadTextStyleRowViewModel.IsShapeFile), static row => row.IsShapeFile)
        };

        return columns;
    }

    private static DataGridImageColumnDefinition CreateImageColumn(
        object header,
        string propertyName,
        Func<CadTextStyleRowViewModel, Bitmap?> getter)
    {
        var binding = DataGridBindingFactory.CreateBinding(propertyName, getter);
        var accessor = DataGridBindingFactory.CreateValueAccessor(getter, setter: null);
        var column = new DataGridImageColumnDefinition
        {
            Header = header,
            Binding = binding,
            ColumnKey = propertyName,
            CanUserSort = false,
            CanUserReorder = true,
            CanUserResize = true,
            ImageWidth = 48,
            ImageHeight = 48,
            Stretch = Stretch.Uniform
        };

        column.Options = new DataGridColumnDefinitionOptions
        {
            IsSearchable = false,
            FilterValueAccessor = accessor
        };

        return column;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<CadTextStyleRowViewModel, string> getter)
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
        Func<CadTextStyleRowViewModel, bool> getter)
    {
        var binding = DataGridBindingFactory.CreateBinding(propertyName, getter);
        var accessor = DataGridBindingFactory.CreateValueAccessor(getter, setter: null);
        var column = new DataGridCheckBoxColumnDefinition
        {
            Header = header,
            Binding = binding,
            ColumnKey = propertyName,
            SortMemberPath = propertyName,
            CanUserSort = true,
            CanUserReorder = true,
            CanUserResize = true,
            IsReadOnly = true
        };

        column.Options = new DataGridColumnDefinitionOptions
        {
            IsSearchable = true,
            FilterValueAccessor = accessor
        };

        return column;
    }
}
