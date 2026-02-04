using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ACadInspector.ViewModels;

internal static class CadDimensionStyleColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateImageColumn("Preview", nameof(CadDimensionStyleRowViewModel.Preview), static row => row.Preview),
            CreateTextColumn("Name", nameof(CadDimensionStyleRowViewModel.Name), static row => row.Name),
            CreateTextColumn("Handle", nameof(CadDimensionStyleRowViewModel.Handle), static row => row.Handle),
            CreateTextColumn("Text Height", nameof(CadDimensionStyleRowViewModel.TextHeight), static row => row.TextHeight),
            CreateTextColumn("Arrow Size", nameof(CadDimensionStyleRowViewModel.ArrowSize), static row => row.ArrowSize),
            CreateTextColumn("Decimals", nameof(CadDimensionStyleRowViewModel.DecimalPlaces), static row => row.DecimalPlaces),
            CreateTextColumn("Scale", nameof(CadDimensionStyleRowViewModel.ScaleFactor), static row => row.ScaleFactor),
            CreateTextColumn("Text Style", nameof(CadDimensionStyleRowViewModel.TextStyle), static row => row.TextStyle),
            CreateCheckBoxColumn("Alt Units", nameof(CadDimensionStyleRowViewModel.AlternateUnits), static row => row.AlternateUnits)
        };

        return columns;
    }

    private static DataGridImageColumnDefinition CreateImageColumn(
        object header,
        string propertyName,
        Func<CadDimensionStyleRowViewModel, Bitmap?> getter)
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
        Func<CadDimensionStyleRowViewModel, string> getter)
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
        Func<CadDimensionStyleRowViewModel, bool> getter)
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
